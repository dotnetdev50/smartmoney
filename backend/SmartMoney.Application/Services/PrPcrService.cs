using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartMoney.Application.Options;
using SmartMoney.Domain.Entities;
using System.Globalization;
using System.IO.Compression;

namespace SmartMoney.Application.Services;

/// <summary>
/// Downloads the NSE PR (options bhavcopy) ZIP and computes Put-Call Ratios
/// for NIFTY and BANKNIFTY (fallback PCR source after op-bhavcopy).
///
/// Source page : https://www.nseindia.com/all-reports-derivatives
/// ZIP pattern : {PrBaseUrl}/PR{DDMMYY}.zip   (e.g. PR040326.zip  — 6-digit year)
/// CSV inside  : pr{DDMMYYYY}.csv             (e.g. pr04032026.csv — 8-digit year)
///
/// Columns: SYMBOL, EXPIRY_DT, OPTION_TYP, STRIKE_PR, OPEN, HIGH, LOW, CLOSE,
///          SETTLE_PR, CONTRACTS, VAL_INLAKH, OPEN_INT, CHG_IN_OI, TIMESTAMP
///
/// PCR (OI)     = Sum(OPEN_INT  for PE) / Sum(OPEN_INT  for CE)
/// PCR (Volume) = Sum(CONTRACTS for PE) / Sum(CONTRACTS for CE)
/// </summary>
public sealed class PrPcrService(
    HttpClient http,
    IOptions<NseOptions> options,
    ILogger<PrPcrService> logger)
{
    private readonly NseOptions _opt = options.Value;

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

    /// <summary>
    /// Fetches PCR values for the given date. Returns null on failure. Never throws.
    /// </summary>
    public async Task<PrPcrResult?> FetchPcrAsync(DateTime date, CancellationToken ct)
    {
        var urls = BuildPrUrlCandidates(date).ToList();
        logger.LogInformation("Fetching NSE PR file PCR for {Date} from candidates: {Urls}",
            date.ToString("yyyy-MM-dd"), string.Join(", ", urls));

        foreach (var url in urls)
        {
            try
            {
                await using var zipStream = await DownloadAsync(url, ct);
                return await ParsePcrFromZipAsync(zipStream, date, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                logger.LogDebug("PR ZIP not found at {Url}", url);
                continue;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Failed to fetch/parse NSE PR file for {Date} from {Url}: {Msg}",
                    date.ToString("yyyy-MM-dd"), url, ex.Message);
                return null;
            }
        }

        logger.LogWarning("NSE PR file not found for {Date} — likely holiday or data not yet published.",
            date.ToString("yyyy-MM-dd"));
        return null;
    }

    /// <summary>
    /// ZIP uses 6-digit year: PR{DDMMYY}.zip  e.g. PR040326.zip
    /// Tries uppercase (canonical NSE naming) then lowercase fallback.
    /// </summary>
    private IEnumerable<string> BuildPrUrlCandidates(DateTime date)
    {
        var dd = date.ToString("dd", CultureInfo.InvariantCulture);
        var mm = date.ToString("MM", CultureInfo.InvariantCulture);
        var yy = date.ToString("yy", CultureInfo.InvariantCulture);

        var fileUpper = $"PR{dd}{mm}{yy}.zip";  // e.g. PR040326.zip  (canonical NSE casing)
        var fileLower = $"pr{dd}{mm}{yy}.zip";  // e.g. pr040326.zip  (lowercase fallback)

        var bases = new List<string>();
        var configBase = _opt.PrBaseUrl?.TrimEnd('/');
        if (!string.IsNullOrEmpty(configBase)) bases.Add(configBase);
        bases.Add("https://nsearchives.nseindia.com/content/fo");
        bases.Add("https://archives.nseindia.com/content/fo");

        foreach (var b in bases.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return $"{b}/{fileUpper}";
            yield return $"{b}/{fileLower}";
        }
    }

    private async Task<Stream> DownloadAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        req.Headers.TryAddWithoutValidation("Accept", "application/zip,application/octet-stream,*/*");
        req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        req.Headers.TryAddWithoutValidation("Referer", "https://www.nseindia.com/");

        var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var ms = new MemoryStream();
        await using var s = await resp.Content.ReadAsStreamAsync(ct);
        await s.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }

    private async Task<PrPcrResult?> ParsePcrFromZipAsync(Stream zipStream, DateTime date, CancellationToken ct)
    {
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        // CSV inside uses 8-digit year: pr{DDMMYYYY}.csv  e.g. pr04032026.csv
        var dd = date.ToString("dd", CultureInfo.InvariantCulture);
        var mm = date.ToString("MM", CultureInfo.InvariantCulture);
        var yyyy = date.ToString("yyyy", CultureInfo.InvariantCulture);
        var expectedName = $"pr{dd}{mm}{yyyy}.csv";

        var entry = archive.Entries.FirstOrDefault(e =>
                        e.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
                    ?? archive.Entries.FirstOrDefault(e =>
                        e.Name.StartsWith("pr", StringComparison.OrdinalIgnoreCase) &&
                        e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            logger.LogWarning("No pr*.csv entry found in PR{Date}.zip. Entries: {Entries}",
                date.ToString("ddMMyy"),
                string.Join(", ", archive.Entries.Select(e => e.Name)));
            return null;
        }

        logger.LogInformation("Parsing PR CSV entry: {Name}", entry.Name);
        await using var csvStream = entry.Open();
        return await ComputePcrFromCsvAsync(csvStream, ct);
    }

    private async Task<PrPcrResult?> ComputePcrFromCsvAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream);

        var headerLine = await reader.ReadLineAsync(ct);
        if (headerLine is null) return null;

        var headers = headerLine.Split(',')
            .Select(h => h.Trim().Trim('"').ToUpperInvariant())
            .ToList();

        var symbolIdx = headers.IndexOf("SYMBOL");
        var optTypeIdx = headers.IndexOf("OPTION_TYP");
        var contractsIdx = headers.IndexOf("CONTRACTS");
        var openIntIdx = headers.IndexOf("OPEN_INT");

        if (symbolIdx < 0 || optTypeIdx < 0 || contractsIdx < 0 || openIntIdx < 0)
        {
            logger.LogWarning("PR CSV missing required columns (SYMBOL/OPTION_TYP/CONTRACTS/OPEN_INT). Found: {H}", headerLine);
            return null;
        }

        var maxIdx = Math.Max(openIntIdx, Math.Max(symbolIdx, Math.Max(optTypeIdx, contractsIdx)));

        double niftyPutOi = 0, niftyCallOi = 0;
        double niftyPutVol = 0, niftyCallVol = 0;
        double bankNiftyPutOi = 0, bankNiftyCallOi = 0;
        double bankNiftyPutVol = 0, bankNiftyCallVol = 0;

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cols = line.Split(',');
            if (cols.Length <= maxIdx) continue;

            var symbol = cols[symbolIdx].Trim().Trim('"').ToUpperInvariant();
            var optType = cols[optTypeIdx].Trim().Trim('"').ToUpperInvariant();

            bool isNifty = symbol.Equals("NIFTY", StringComparison.OrdinalIgnoreCase);
            bool isBankNifty = symbol.Equals("BANKNIFTY", StringComparison.OrdinalIgnoreCase);
            if (!isNifty && !isBankNifty) continue;

            var oiStr = cols[openIntIdx].Trim().Trim('"').Replace(",", "");
            var volStr = cols[contractsIdx].Trim().Trim('"').Replace(",", "");
            double.TryParse(oiStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var oi);
            double.TryParse(volStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var vol);

            bool isPut = optType.Equals("PE", StringComparison.OrdinalIgnoreCase);
            bool isCall = optType.Equals("CE", StringComparison.OrdinalIgnoreCase);

            if (isNifty)
            {
                if (isPut) { niftyPutOi += oi; niftyPutVol += vol; }
                else if (isCall) { niftyCallOi += oi; niftyCallVol += vol; }
            }
            else
            {
                if (isPut) { bankNiftyPutOi += oi; bankNiftyPutVol += vol; }
                else if (isCall) { bankNiftyCallOi += oi; bankNiftyCallVol += vol; }
            }
        }

        var niftyPcrOi = ComputeRatio(niftyPutOi, niftyCallOi, "NIFTY OI");
        var niftyPcrVol = ComputeRatio(niftyPutVol, niftyCallVol, "NIFTY Volume");
        var bnfPcrOi = ComputeRatio(bankNiftyPutOi, bankNiftyCallOi, "BANKNIFTY OI");
        var bnfPcrVol = ComputeRatio(bankNiftyPutVol, bankNiftyCallVol, "BANKNIFTY Volume");

        logger.LogInformation(
            "PR PCR — NIFTY OI: {NIO}, Vol: {NV} | BANKNIFTY OI: {BIO}, Vol: {BV}",
            niftyPcrOi, niftyPcrVol, bnfPcrOi, bnfPcrVol);

        return new PrPcrResult(niftyPcrOi, niftyPcrVol, bnfPcrOi, bnfPcrVol);
    }

    private double? ComputeRatio(double putValue, double callValue, string label)
    {
        if (callValue <= 0)
        {
            logger.LogWarning("PR PCR: Call value is zero for {Label}. Put={Put}", label, putValue);
            return null;
        }
        return Math.Round(putValue / callValue, 4);
    }
}