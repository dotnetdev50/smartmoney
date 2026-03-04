using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartMoney.Application.Options;
using SmartMoney.Domain.Entities;
using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace SmartMoney.Application.Services;

/// <summary>
/// Downloads the NSE F&amp;O daily bhavcopy ZIP (fo{DDMMYYYY}.zip) from NSE archives,
/// extracts the options file (op{DDMMYYYY}.csv), and computes Put-Call Ratios for
/// NIFTY and BANKNIFTY across <b>all expiries</b>.
///
/// Source URL pattern:
///   {FoBhavZipBaseUrl}/fo{DDMMYYYY}.zip  (e.g. https://nsearchives.nseindia.com/content/fo/fo04032026.zip)
///
/// The ZIP contains a file named op{DDMMYYYY}.csv with columns:
///   CONTRACT_D, PREVIOUS_S, OPEN_PRICE, HIGH_PRICE, LOW_PRICE, CLOSE_PRIC,
///   SETTLEMENT, NET_CHANGE, OI_NO_CON, TRADED_QUA, TRD_NO_CON, UNDRLNG_ST,
///   NOTIONAL_V, PREMIUM_TR
///
/// CONTRACT_D format: OPTIDX{SYMBOL}{EXPIRY}{TYPE}{STRIKE}
///   e.g. OPTIDXNIFTY30-MAR-2026CE22000
///        OPTIDXBANKNIFTY30-MAR-2026PE45000
///
/// Formulae (aggregated across all expiries):
///   PCR (OI)     = Sum(OI_NO_CON  for PE) / Sum(OI_NO_CON  for CE)
///   PCR (Volume) = Sum(TRADED_QUA for PE) / Sum(TRADED_QUA for CE)
/// </summary>
public sealed partial class OpBhavCopyService(
    HttpClient http,
    IOptions<NseOptions> options,
    ILogger<OpBhavCopyService> logger)
{
    private readonly NseOptions _opt = options.Value;

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

    // Matches "CE" or "PE" immediately followed by one or more digits at end of string.
    // Used to extract the option type from the full contract descriptor (CONTRACT_D).
    [GeneratedRegex(@"(CE|PE)\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex OptionTypeRegex();

    private const string NiftyPrefix = "OPTIDXNIFTY";

    // BANKNIFTY prefix must be checked before NIFTY so the longer prefix wins.
    private const string BankNiftyPrefix = "OPTIDXBANKNIFTY";

    /// <summary>
    /// Fetches PCR values for NIFTY and BANKNIFTY for the given date.
    /// Returns null if data is unavailable (holiday / 404 / parse error). Never throws.
    /// </summary>
    public async Task<PrPcrResult?> FetchPcrAsync(DateTime date, CancellationToken ct)
    {
        var url = BuildZipUrl(date);
        logger.LogInformation("Fetching NSE op-bhavcopy PCR for {Date} from {Url}", date.ToString("yyyy-MM-dd"), url);

        try
        {
            await using var zipStream = await DownloadAsync(url, ct);
            return await ParsePcrFromZipAsync(zipStream, date, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogWarning("NSE FO bhavcopy ZIP not found (404) for {Date} — likely holiday or data not yet published.", date.ToString("yyyy-MM-dd"));
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to fetch/parse NSE op-bhavcopy for {Date}: {Msg}", date.ToString("yyyy-MM-dd"), ex.Message);
            return null;
        }
    }

    private string BuildZipUrl(DateTime date)
    {
        // Filename format: fo{DDMMYYYY}.zip  e.g. fo04032026.zip
        var dd   = date.ToString("dd",   CultureInfo.InvariantCulture);
        var mm   = date.ToString("MM",   CultureInfo.InvariantCulture);
        var yyyy = date.ToString("yyyy", CultureInfo.InvariantCulture);
        var file = $"fo{dd}{mm}{yyyy}.zip";
        return $"{_opt.FoBhavZipBaseUrl.TrimEnd('/')}/{file}";
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

        // Primary: op{DDMMYYYY}.csv  (e.g. op04032026.csv)
        var dd   = date.ToString("dd",   CultureInfo.InvariantCulture);
        var mm   = date.ToString("MM",   CultureInfo.InvariantCulture);
        var yyyy = date.ToString("yyyy", CultureInfo.InvariantCulture);
        var expectedName = $"op{dd}{mm}{yyyy}.csv";

        var entry = archive.Entries.FirstOrDefault(e =>
                        e.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
                    ?? archive.Entries.FirstOrDefault(e =>
                        e.Name.StartsWith("op", StringComparison.OrdinalIgnoreCase) &&
                        e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            logger.LogWarning("No op*.csv entry found in NSE FO bhavcopy ZIP for {Date}. Entries: {Entries}",
                date.ToString("yyyy-MM-dd"),
                string.Join(", ", archive.Entries.Select(e => e.Name)));
            return null;
        }

        logger.LogInformation("Parsing op-bhavcopy CSV entry: {Name}", entry.Name);
        await using var csvStream = entry.Open();
        return await ComputePcrFromCsvAsync(csvStream, ct);
    }

    private async Task<PrPcrResult?> ComputePcrFromCsvAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream);

        // First line = header
        var headerLine = await reader.ReadLineAsync(ct);
        if (headerLine is null) return null;

        var headers = headerLine.Split(',')
            .Select(h => h.Trim().Trim('"').ToUpperInvariant())
            .ToList();

        var contractIdx = headers.IndexOf("CONTRACT_D");
        var oiIdx       = headers.IndexOf("OI_NO_CON");
        var volIdx      = headers.IndexOf("TRADED_QUA");

        if (contractIdx < 0 || oiIdx < 0 || volIdx < 0)
        {
            logger.LogWarning("op-bhavcopy CSV missing required columns (CONTRACT_D/OI_NO_CON/TRADED_QUA). Found: {H}", headerLine);
            return null;
        }

        var maxIdx = Math.Max(contractIdx, Math.Max(oiIdx, volIdx));

        // Accumulators per symbol × option type
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

            var contract = cols[contractIdx].Trim().Trim('"').ToUpperInvariant();

            // BANKNIFTY prefix must be tested first because it starts with "OPTIDXNIFTY" too.
            bool isBankNifty = contract.StartsWith(BankNiftyPrefix, StringComparison.Ordinal);
            bool isNifty     = !isBankNifty && contract.StartsWith(NiftyPrefix, StringComparison.Ordinal);

            if (!isNifty && !isBankNifty) continue;

            // Extract CE or PE from contract name: e.g. OPTIDXNIFTY30-MAR-2026CE22000 → CE
            var match = OptionTypeRegex().Match(contract);
            if (!match.Success) continue;

            var optType = match.Groups[1].Value; // already upper-case due to ToUpperInvariant above

            var oiStr  = cols[oiIdx].Trim().Trim('"').Replace(",", "");
            var volStr = cols[volIdx].Trim().Trim('"').Replace(",", "");

            double.TryParse(oiStr,  NumberStyles.Any, CultureInfo.InvariantCulture, out var oi);
            double.TryParse(volStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var vol);

            bool isPut  = optType == "PE";
            bool isCall = optType == "CE";

            if (isNifty)
            {
                if (isPut)       { niftyPutOi  += oi; niftyPutVol  += vol; }
                else if (isCall) { niftyCallOi += oi; niftyCallVol += vol; }
            }
            else // BANKNIFTY
            {
                if (isPut)       { bankNiftyPutOi  += oi; bankNiftyPutVol  += vol; }
                else if (isCall) { bankNiftyCallOi += oi; bankNiftyCallVol += vol; }
            }
        }

        var niftyPcrOi  = ComputeRatio(niftyPutOi,      niftyCallOi,      "NIFTY OI");
        var niftyPcrVol = ComputeRatio(niftyPutVol,     niftyCallVol,     "NIFTY Volume");
        var bnfPcrOi    = ComputeRatio(bankNiftyPutOi,  bankNiftyCallOi,  "BANKNIFTY OI");
        var bnfPcrVol   = ComputeRatio(bankNiftyPutVol, bankNiftyCallVol, "BANKNIFTY Volume");

        logger.LogInformation(
            "op-bhavcopy PCR — NIFTY OI: {NIO}, Vol: {NV} | BANKNIFTY OI: {BIO}, Vol: {BV}",
            niftyPcrOi, niftyPcrVol, bnfPcrOi, bnfPcrVol);

        return new PrPcrResult(niftyPcrOi, niftyPcrVol, bnfPcrOi, bnfPcrVol);
    }

    private double? ComputeRatio(double putValue, double callValue, string label)
    {
        if (callValue <= 0)
        {
            logger.LogWarning("op-bhavcopy PCR: Call value is zero for {Label} — skipping. Put={Put}", label, putValue);
            return null;
        }
        return Math.Round(putValue / callValue, 4);
    }
}
