using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartMoney.Application.Options;
using System.Globalization;
using System.IO.Compression;

namespace SmartMoney.Application.Services;

/// <summary>
/// Downloads the NSE FO Bhavcopy ZIP for a given date, parses the CSV inside,
/// and computes the Put-Call Ratio (PCR) for NIFTY index options.
/// PCR = Sum(OPEN_INT for PE) / Sum(OPEN_INT for CE)
/// </summary>
public sealed class FoBhavCopyService(
    HttpClient http,
    IOptions<NseOptions> options,
    ILogger<FoBhavCopyService> logger)
{
    private readonly NseOptions _opt = options.Value;

    private const string NiftyInstrument = "OPTIDX";
    private const string NiftySymbol = "NIFTY";

    /// <summary>
    /// Fetches PCR for the given date. Returns null if data unavailable (holiday/404/parse error).
    /// </summary>
    public async Task<double?> FetchPcrAsync(DateTime date, CancellationToken ct)
    {
        var urls = BuildBhavCopyUrlCandidates(date).ToList();
        logger.LogInformation("Fetching FO bhavcopy PCR for {Date} from {Urls}", date.ToString("yyyy-MM-dd"), string.Join(", ", urls));

        foreach (var url in urls)
        {
            try
            {
                await using var zipStream = await DownloadAsync(url, ct);
                return await ParsePcrFromZipAsync(zipStream, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                logger.LogDebug("FO bhavcopy ZIP not found at {Url}", url);
                continue;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Failed to fetch/parse FO bhavcopy for {Date} from {Url}: {Msg}", date.ToString("yyyy-MM-dd"), url, ex.Message);
                return null;
            }
        }

        logger.LogWarning("FO bhavcopy not found (404) for {Date} — likely holiday or data not yet published.", date.ToString("yyyy-MM-dd"));
        return null;
    }

    private IEnumerable<string> BuildBhavCopyUrlCandidates(DateTime date)
    {
        var dd = date.ToString("dd", CultureInfo.InvariantCulture);
        var mmm = date.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant();
        var yyyy = date.ToString("yyyy", CultureInfo.InvariantCulture);
        var yy = yyyy[^2..];

        var fileFull = $"fo{dd}{mmm}{yyyy}bhav.csv.zip"; // fo04MAR2026bhav.csv.zip
        var fileShort = $"fo{dd}{mmm}{yy}bhav.csv.zip";   // fo04MAR26bhav.csv.zip

        // Prefer HttpClient base address (configured centrally) then fall back to option
        var primaryBase = http.BaseAddress?.ToString().TrimEnd('/')
                          ?? _opt.FoBhavCopyBaseUrl?.TrimEnd('/')
                          ?? string.Empty;

        if (!string.IsNullOrEmpty(primaryBase))
        {
            // Year/month organized path (primary)
            yield return $"{primaryBase}/{yyyy}/{mmm}/{fileFull}";
            if (!fileShort.Equals(fileFull, StringComparison.OrdinalIgnoreCase))
                yield return $"{primaryBase}/{yyyy}/{mmm}/{fileShort}";

            // Top-level path
            yield return $"{primaryBase}/{fileFull}";
            if (!fileShort.Equals(fileFull, StringComparison.OrdinalIgnoreCase))
                yield return $"{primaryBase}/{fileShort}";
        }

        // Keep a small, deterministic fallback list (not scattered across files)
        yield return $"https://nsearchives.nseindia.com/content/historical/DERIVATIVES/{yyyy}/{mmm}/{fileFull}";
        yield return $"https://nsearchives.nseindia.com/content/fo/{fileFull}";
    }

    private async Task<Stream> DownloadAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
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

    private async Task<double?> ParsePcrFromZipAsync(Stream zipStream, CancellationToken ct)
    {
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var entry = archive.Entries.FirstOrDefault(e =>
            e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            logger.LogWarning("No CSV entry found in bhavcopy ZIP.");
            return null;
        }

        await using var csvStream = entry.Open();
        return await ComputePcrFromCsvAsync(csvStream, ct);
    }

    private async Task<double?> ComputePcrFromCsvAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream);

        // First line = header
        var headerLine = await reader.ReadLineAsync(ct);
        if (headerLine is null) return null;

        var headers = headerLine.Split(',')
            .Select(h => h.Trim().Trim('"').ToUpperInvariant())
            .ToList();

        var instrIdx = headers.IndexOf("INSTRUMENT");
        var symbolIdx = headers.IndexOf("SYMBOL");
        var optTypeIdx = headers.IndexOf("OPTION_TYP");
        var openIntIdx = headers.IndexOf("OPEN_INT");

        if (instrIdx < 0 || symbolIdx < 0 || optTypeIdx < 0 || openIntIdx < 0)
        {
            logger.LogWarning("Bhavcopy CSV missing required columns (INSTRUMENT/SYMBOL/OPTION_TYP/OPEN_INT). Found: {H}", headerLine);
            return null;
        }

        double putOi = 0, callOi = 0;
        var maxIdx = Math.Max(openIntIdx, Math.Max(instrIdx, Math.Max(symbolIdx, optTypeIdx)));

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cols = line.Split(',');
            if (cols.Length <= maxIdx) continue;

            var instr = cols[instrIdx].Trim().Trim('"');
            var symbol = cols[symbolIdx].Trim().Trim('"');
            var optType = cols[optTypeIdx].Trim().Trim('"');

            if (!instr.Equals(NiftyInstrument, StringComparison.OrdinalIgnoreCase)) continue;
            if (!symbol.Equals(NiftySymbol, StringComparison.OrdinalIgnoreCase)) continue;

            var oiStr = cols[openIntIdx].Trim().Trim('"').Replace(",", "");
            if (!double.TryParse(oiStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var oi)) continue;

            if (optType.Equals("PE", StringComparison.OrdinalIgnoreCase))
                putOi += oi;
            else if (optType.Equals("CE", StringComparison.OrdinalIgnoreCase))
                callOi += oi;
        }

        if (callOi <= 0)
        {
            logger.LogWarning("Call OI is zero or negative — cannot compute PCR. Put OI={PutOi}, Call OI={CallOi}", putOi, callOi);
            return null;
        }

        var pcr = Math.Round(putOi / callOi, 4);
        logger.LogInformation("PCR computed: {Pcr} (Put OI={PutOi:N0}, Call OI={CallOi:N0})", pcr, putOi, callOi);
        return pcr;
    }
}