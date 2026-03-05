using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartMoney.Application.Options;
using System.Globalization;
using System.Text.Json;

namespace SmartMoney.Application.Services;

/// <summary>
/// Fetches the India VIX closing value from NSE.
///
/// Strategy (in order):
///   1. NSE JSON API  — https://www.nseindia.com/api/historicalOR/vixhistory?from=DD-MM-YYYY&amp;to=DD-MM-YYYY
///      Requires NSE session cookies; homepage is primed first (Akamai bot-protection).
///      Source page: https://www.nseindia.com/reports-indices-historical-vix
///   2. Archive CSV   — <see cref="NseOptions.VixArchiveUrl"/> (full history, no session needed)
///      Date format in CSV: DD-MMM-YYYY (e.g. 05-Mar-2026)
///      Columns: Date, Open, High, Low, Close, Prev Close, Change
/// </summary>
public sealed class VixFetchService(
    HttpClient http,
    IOptions<NseOptions> options,
    ILogger<VixFetchService> logger)
{
    private readonly NseOptions _opt = options.Value;

    private const string NseHomeUrl = "https://www.nseindia.com/";
    private const string NseVixApiTemplate = "https://www.nseindia.com/api/historicalOR/vixhistory?from={0}&to={1}";

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

    /// <summary>
    /// Returns the India VIX closing value for <paramref name="date"/>, or null on failure.
    /// Never throws.
    /// </summary>
    public async Task<double?> FetchVixAsync(DateTime date, CancellationToken ct)
    {
        var apiVix = await FetchVixFromNseApiAsync(date, ct);
        if (apiVix.HasValue)
        {
            logger.LogInformation("India VIX for {Date} via NSE API: {Vix}", date.ToString("yyyy-MM-dd"), apiVix.Value);
            return apiVix;
        }

        logger.LogWarning("NSE API VIX fetch returned null for {Date}. Falling back to archive CSV.", date.ToString("yyyy-MM-dd"));
        return await FetchVixFromArchiveCsvAsync(date, ct);
    }

    /// <summary>
    /// Fetches VIX from the NSE historicalOR JSON API.
    /// Primes session cookies via homepage before calling the API endpoint.
    /// API date format: dd-MM-yyyy  (e.g. 05-03-2026)
    /// </summary>
    private async Task<double?> FetchVixFromNseApiAsync(DateTime date, CancellationToken ct)
    {
        try
        {
            var dateStr = date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
            var apiUrl = string.Format(CultureInfo.InvariantCulture, NseVixApiTemplate, dateStr, dateStr);

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

            AddNseHeaders(sessionClient, acceptHtml: true);

            // Step 1: prime session cookies (Akamai bot-protection).
            logger.LogInformation("Priming NSE session via homepage for VIX API call.");
            var homeResp = await sessionClient.GetAsync(NseHomeUrl, ct);
            logger.LogInformation("NSE homepage responded with HTTP {Status}.", (int)homeResp.StatusCode);

            // Step 2: call VIX history API with session cookies.
            AddNseHeaders(sessionClient, acceptHtml: false);
            var apiResp = await sessionClient.GetAsync(apiUrl, ct);

            if (!apiResp.IsSuccessStatusCode)
            {
                logger.LogWarning("NSE VIX API returned HTTP {Status} for {Date}.", (int)apiResp.StatusCode, date.ToString("yyyy-MM-dd"));
                return null;
            }

            var json = await apiResp.Content.ReadAsStringAsync(ct);
            return ParseVixFromApiJson(json, date);
        }
        catch (Exception ex)
        {
            logger.LogWarning("NSE VIX API fetch failed for {Date}: {Msg}", date.ToString("yyyy-MM-dd"), ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Fetches VIX from the full-history CSV at <see cref="NseOptions.VixArchiveUrl"/>.
    /// CSV date format: DD-MMM-YYYY (e.g. 05-Mar-2026). Columns: Date,Open,High,Low,Close,...
    /// </summary>
    private async Task<double?> FetchVixFromArchiveCsvAsync(DateTime date, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, _opt.VixArchiveUrl);
            req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            req.Headers.TryAddWithoutValidation("Accept", "text/csv,text/plain,*/*");
            req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            req.Headers.TryAddWithoutValidation("Referer", NseHomeUrl);

            var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            var content = await resp.Content.ReadAsStringAsync(ct);
            var vix = ParseVixClose(content, date);

            if (vix.HasValue)
                logger.LogInformation("India VIX for {Date} via archive CSV: {Vix}", date.ToString("yyyy-MM-dd"), vix.Value);
            else
                logger.LogWarning("India VIX not found in archive CSV for {Date}.", date.ToString("yyyy-MM-dd"));

            return vix;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Archive CSV VIX fetch failed for {Date}: {Msg}", date.ToString("yyyy-MM-dd"), ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Parses VIX close from NSE JSON API response.
    /// Expected: { "data": [ { "EOD_TIMESTAMP": "05-Mar-2026", "EOD_CLOSE_INDEX_VAL": "14.13" } ] }
    /// </summary>
    private double? ParseVixFromApiJson(string json, DateTime date)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var dataArray) ||
                dataArray.ValueKind != JsonValueKind.Array)
            {
                logger.LogWarning("NSE VIX API JSON missing 'data' array for {Date}. Raw: {Json}",
                    date.ToString("yyyy-MM-dd"), json.Length > 200 ? json[..200] : json);
                return null;
            }

            // API timestamps use "dd-MMM-yyyy" format (e.g. "05-Mar-2026").
            var targetDate = date.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);

            foreach (var item in dataArray.EnumerateArray())
            {
                if (!item.TryGetProperty("EOD_TIMESTAMP", out var ts)) continue;
                var tsStr = ts.GetString() ?? "";
                if (!tsStr.Equals(targetDate, StringComparison.OrdinalIgnoreCase)) continue;

                if (!item.TryGetProperty("EOD_CLOSE_INDEX_VAL", out var closeEl)) continue;

                var closeStr = closeEl.ValueKind == JsonValueKind.String
                    ? closeEl.GetString() ?? ""
                    : closeEl.GetRawText();

                if (double.TryParse(closeStr.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var vix))
                    return Math.Round(vix, 2);
            }

            logger.LogWarning("NSE VIX API: no entry found for {Date} in {Count} records.",
                date.ToString("yyyy-MM-dd"), dataArray.GetArrayLength());
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to parse NSE VIX API JSON for {Date}: {Msg}", date.ToString("yyyy-MM-dd"), ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Parses VIX close from full-history archive CSV.
    /// Date column format: DD-MMM-YYYY (e.g. "05-Mar-2026") or D-MMM-YYYY (e.g. "5-Mar-2026").
    /// Close value is the 5th column (index 4).
    /// </summary>
    private static double? ParseVixClose(string csv, DateTime date)
    {
        var targetTwoDigit = date.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
        var targetOneDigit = date.ToString("d-MMM-yyyy", CultureInfo.InvariantCulture);

        foreach (var rawLine in csv.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            var comma = line.IndexOf(',');
            if (comma <= 0) continue;

            var datePart = line[..comma].Trim();
            if (!datePart.Equals(targetTwoDigit, StringComparison.OrdinalIgnoreCase) &&
                !datePart.Equals(targetOneDigit, StringComparison.OrdinalIgnoreCase)) continue;

            var cols = line.Split(',');
            // Columns: Date, Open, High, Low, Close, Prev Close, Change
            if (cols.Length < 5) continue;

            var closeStr = cols[4].Trim();
            if (double.TryParse(closeStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var vix))
                return Math.Round(vix, 2);
        }

        return null;
    }

    private static void AddNseHeaders(HttpClient client, bool acceptHtml)
    {
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Accept",
            acceptHtml
                ? "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"
                : "application/json,text/plain,*/*");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", NseHomeUrl);
    }
}