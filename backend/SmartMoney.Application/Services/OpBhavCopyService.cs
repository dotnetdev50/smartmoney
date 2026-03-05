using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartMoney.Application.Options;
using SmartMoney.Domain.Entities;
using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace SmartMoney.Application.Services;

/// <summary>
/// Downloads the NSE F&amp;O daily bhavcopy ZIP and computes Put-Call Ratios for
/// NIFTY and BANKNIFTY across all expiries (primary PCR source).
///
/// Source page : https://www.nseindia.com/all-reports-derivatives
/// ZIP pattern : {FoBhavZipBaseUrl}/fo{DDMMYYYY}.zip  (e.g. fo05032026.zip — 8-digit year)
/// CSV inside  : op{DDMMYY}.csv                       (e.g. op040326.csv  — 6-digit year)
///
/// Note: The op*.csv inside is dated to the *previous* trading day's data;
///       always use a fuzzy fallback (any op*.csv) so the correct entry is found.
///
/// Columns: CONTRACT_D, OI_NO_CON, TRADED_QUA
/// CONTRACT_D format: OPTIDX{SYMBOL}{EXPIRY}{TYPE}{STRIKE}
///   e.g. OPTIDXNIFTY30-MAR-2026CE22000
///
/// PCR (OI)     = Sum(OI_NO_CON  for PE) / Sum(OI_NO_CON  for CE)
/// PCR (Volume) = Sum(TRADED_QUA for PE) / Sum(TRADED_QUA for CE)
/// Aggregated across ALL expiries.
/// </summary>
public sealed partial class OpBhavCopyService(
    HttpClient http,
    IOptions<NseOptions> options,
    ILogger<OpBhavCopyService> logger)
{
    private readonly NseOptions _opt = options.Value;

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

    private const string NseHomeUrl = "https://www.nseindia.com/";

    // Matches "CE" or "PE" immediately followed by one or more digits at end of string.
    [GeneratedRegex(@"(CE|PE)\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex OptionTypeRegex();

    // BANKNIFTY prefix must be checked before NIFTY so the longer prefix wins.
    private const string NiftyPrefix = "OPTIDXNIFTY";

    private const string BankNiftyPrefix = "OPTIDXBANKNIFTY";

    /// <summary>
    /// Fetches PCR values for NIFTY and BANKNIFTY for the given date.
    /// Returns null if data is unavailable (holiday / 404 / parse error). Never throws.
    /// </summary>
    public async Task<PrPcrResult?> FetchPcrAsync(DateTime date, CancellationToken ct)
    {
        var urls = BuildZipUrlCandidates(date).ToList();
        logger.LogInformation("Fetching NSE op-bhavcopy PCR for {Date} from candidates: {Urls}",
            date.ToString("yyyy-MM-dd"), string.Join(", ", urls));

        foreach (var url in urls)
        {
            try
            {
                await using var zipStream = await DownloadWithSessionAsync(url, ct);
                var result = await ParsePcrFromZipAsync(zipStream, date, ct);
                if (result is not null)
                    return result;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                logger.LogDebug("op-bhavcopy ZIP not found at {Url}", url);
                continue;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Failed to fetch/parse NSE op-bhavcopy for {Date} from {Url}: {Msg}",
                    date.ToString("yyyy-MM-dd"), url, ex.Message);
                return null;
            }
        }

        logger.LogWarning("NSE op-bhavcopy ZIP not found for {Date} — likely holiday or data not yet published.",
            date.ToString("yyyy-MM-dd"));
        return null;
    }

    /// <summary>
    /// Builds URL candidates for fo{DDMMYYYY}.zip.
    /// ZIP filename uses 8-digit year; tries primary base then known fallback hosts.
    /// </summary>
    private IEnumerable<string> BuildZipUrlCandidates(DateTime date)
    {
        var dd = date.ToString("dd", CultureInfo.InvariantCulture);
        var mm = date.ToString("MM", CultureInfo.InvariantCulture);
        var yyyy = date.ToString("yyyy", CultureInfo.InvariantCulture);

        // ZIP filename: fo{DDMMYYYY}.zip  e.g. fo05032026.zip
        var file = $"fo{dd}{mm}{yyyy}.zip";

        var bases = new List<string>();
        var configBase = _opt.FoBhavZipBaseUrl?.TrimEnd('/');
        if (!string.IsNullOrEmpty(configBase)) bases.Add(configBase);
        bases.Add("https://nsearchives.nseindia.com/content/fo");
        bases.Add("https://archives.nseindia.com/content/fo");

        foreach (var b in bases.Distinct(StringComparer.OrdinalIgnoreCase))
            yield return $"{b}/{file}";
    }

    /// <summary>
    /// Downloads the ZIP after priming NSE session cookies via the homepage.
    /// NSE uses Akamai bot-protection; without a valid session the download returns 401/403.
    /// </summary>
    private async Task<Stream> DownloadWithSessionAsync(string url, CancellationToken ct)
    {
        var cookieContainer = new System.Net.CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            UseCookies = true,
            AllowAutoRedirect = true,
        };

        using var sessionClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(_opt.RequestTimeoutSeconds > 0 ? _opt.RequestTimeoutSeconds : 30)
        };

        sessionClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        sessionClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        sessionClient.DefaultRequestHeaders.TryAddWithoutValidation("Referer", NseHomeUrl);
        sessionClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

        // Step 1: prime session cookies via NSE homepage (required for Akamai bot-check).
        logger.LogInformation("Priming NSE session via homepage for op-bhavcopy download.");
        var homeResp = await sessionClient.GetAsync(NseHomeUrl, ct);
        logger.LogInformation("NSE homepage responded with HTTP {Status}.", (int)homeResp.StatusCode);

        // Step 2: download the ZIP with the session cookies now set.
        sessionClient.DefaultRequestHeaders.Remove("Accept");
        sessionClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "Accept", "application/zip,application/octet-stream,*/*");

        var resp = await sessionClient.GetAsync(url, ct);
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

        // CSV inside uses 6-digit year: op{DDMMYY}.csv  e.g. op040326.csv
        var dd = date.ToString("dd", CultureInfo.InvariantCulture);
        var mm = date.ToString("MM", CultureInfo.InvariantCulture);
        var yy = date.ToString("yy", CultureInfo.InvariantCulture);
        var expectedName = $"op{dd}{mm}{yy}.csv";

        // Prefer exact match; fall back to any op*.csv entry present in the archive.
        var entry = archive.Entries.FirstOrDefault(e =>
                        e.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
                    ?? archive.Entries.FirstOrDefault(e =>
                        e.Name.StartsWith("op", StringComparison.OrdinalIgnoreCase) &&
                        e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            logger.LogWarning("No op*.csv entry found in fo{Date}.zip. Entries: {Entries}",
                date.ToString("ddMMyyyy"),
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

        var headerLine = await reader.ReadLineAsync(ct);
        if (headerLine is null) return null;

        var headers = headerLine.Split(',')
            .Select(h => h.Trim().Trim('"').ToUpperInvariant())
            .ToList();

        var contractIdx = headers.IndexOf("CONTRACT_D");
        var oiIdx = headers.IndexOf("OI_NO_CON");
        var volIdx = headers.IndexOf("TRADED_QUA");

        if (contractIdx < 0 || oiIdx < 0 || volIdx < 0)
        {
            logger.LogWarning("op-bhavcopy CSV missing required columns (CONTRACT_D/OI_NO_CON/TRADED_QUA). Found: {H}", headerLine);
            return null;
        }

        var maxIdx = Math.Max(contractIdx, Math.Max(oiIdx, volIdx));

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

            // BANKNIFTY must be tested before NIFTY (longer prefix wins).
            bool isBankNifty = contract.StartsWith(BankNiftyPrefix, StringComparison.Ordinal);
            bool isNifty = !isBankNifty && contract.StartsWith(NiftyPrefix, StringComparison.Ordinal);
            if (!isNifty && !isBankNifty) continue;

            var match = OptionTypeRegex().Match(contract);
            if (!match.Success) continue;
            var optType = match.Groups[1].Value;

            var oiStr = cols[oiIdx].Trim().Trim('"').Replace(",", "");
            var volStr = cols[volIdx].Trim().Trim('"').Replace(",", "");
            double.TryParse(oiStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var oi);
            double.TryParse(volStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var vol);

            bool isPut = optType == "PE";
            bool isCall = optType == "CE";

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
            "op-bhavcopy PCR — NIFTY OI: {NIO}, Vol: {NV} | BANKNIFTY OI: {BIO}, Vol: {BV}",
            niftyPcrOi, niftyPcrVol, bnfPcrOi, bnfPcrVol);

        return new PrPcrResult(niftyPcrOi, niftyPcrVol, bnfPcrOi, bnfPcrVol);
    }

    private double? ComputeRatio(double putValue, double callValue, string label)
    {
        if (callValue <= 0)
        {
            logger.LogWarning("op-bhavcopy PCR: Call value is zero for {Label}. Put={Put}", label, putValue);
            return null;
        }
        return Math.Round(putValue / callValue, 4);
    }
}