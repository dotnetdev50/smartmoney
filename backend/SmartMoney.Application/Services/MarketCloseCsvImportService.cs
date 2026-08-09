using Microsoft.EntityFrameworkCore;
using SmartMoney.Domain.Entities;
using SmartMoney.Infrastructure.Persistence;
using System.Globalization;

namespace SmartMoney.Application.Services;

public sealed class MarketCloseCsvImportService(SmartMoneyDbContext db)
{
    private const string RequiredHeader = "Date,Symbol,Close";

    public async Task<MarketCloseImportResult> ImportFromCsvFileAsync(string csvFilePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(csvFilePath))
            throw new ArgumentException("csvFilePath is required.", nameof(csvFilePath));

        if (!File.Exists(csvFilePath))
            throw new FileNotFoundException($"CSV file not found: {csvFilePath}", csvFilePath);

        await using var fs = File.OpenRead(csvFilePath);
        return await ImportFromCsvStreamAsync(fs, ct);
    }

    public async Task<MarketCloseImportResult> ImportFromCsvStreamAsync(Stream csvStream, CancellationToken ct)
    {
        var parsed = await ParseCsvAsync(csvStream, ct);
        var keys = parsed.Select(x => new { x.Date, x.Symbol }).ToList();

        var dates = keys.Select(x => x.Date).Distinct().ToList();
        var symbols = keys.Select(x => x.Symbol).Distinct().ToList();

        var existing = await db.MarketCloses
            .Where(x => dates.Contains(x.Date) && symbols.Contains(x.Symbol))
            .ToListAsync(ct);

        var existingMap = existing.ToDictionary(
            x => KeyFor(x.Date, x.Symbol),
            x => x,
            StringComparer.Ordinal);

        var inserted = 0;
        var updated = 0;
        var unchanged = 0;

        foreach (var row in parsed)
        {
            var key = KeyFor(row.Date, row.Symbol);
            if (!existingMap.TryGetValue(key, out var current))
            {
                await db.MarketCloses.AddAsync(new MarketClose
                {
                    Date = row.Date,
                    Symbol = row.Symbol,
                    Close = row.Close
                }, ct);
                inserted++;
                continue;
            }

            if (Math.Abs(current.Close - row.Close) < 1e-12)
            {
                unchanged++;
                continue;
            }

            current.Close = row.Close;
            updated++;
        }

        if (inserted > 0 || updated > 0)
            await db.SaveChangesAsync(ct);

        return new MarketCloseImportResult(parsed.Count, inserted, updated, unchanged);
    }

    public static async Task<List<MarketCloseCsvRow>> ParseCsvAsync(Stream csvStream, CancellationToken ct)
    {
        using var reader = new StreamReader(csvStream, System.Text.Encoding.UTF8, true, 1024, true);

        var header = await reader.ReadLineAsync(ct);
        if (header is null)
            throw new MarketCloseCsvValidationException("CSV is empty. Expected header: Date,Symbol,Close.");

        if (!header.Trim().Equals(RequiredHeader, StringComparison.OrdinalIgnoreCase))
            throw new MarketCloseCsvValidationException($"Invalid header. Expected: {RequiredHeader}.");

        var rows = new List<MarketCloseCsvRow>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();

        var lineNumber = 1;
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var cols = line.Split(',');
            if (cols.Length != 3)
            {
                errors.Add($"Line {lineNumber}: expected 3 columns (Date,Symbol,Close).");
                continue;
            }

            var dateRaw = cols[0].Trim();
            var symbolRaw = cols[1].Trim();
            var closeRaw = cols[2].Trim();

            if (!DateTime.TryParseExact(dateRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
            {
                errors.Add($"Line {lineNumber}: invalid Date '{dateRaw}'. Expected yyyy-MM-dd.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(symbolRaw))
            {
                errors.Add($"Line {lineNumber}: Symbol is required.");
                continue;
            }

            if (!double.TryParse(closeRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var close))
            {
                errors.Add($"Line {lineNumber}: invalid Close '{closeRaw}'.");
                continue;
            }

            if (close <= 0)
            {
                errors.Add($"Line {lineNumber}: Close must be > 0.");
                continue;
            }

            var symbol = symbolRaw.ToUpperInvariant();
            var key = KeyFor(date.Date, symbol);
            if (!keys.Add(key))
            {
                errors.Add($"Line {lineNumber}: duplicate Date+Symbol '{date:yyyy-MM-dd}|{symbol}' in input.");
                continue;
            }

            rows.Add(new MarketCloseCsvRow(date.Date, symbol, close));
        }

        if (errors.Count > 0)
            throw new MarketCloseCsvValidationException(string.Join(Environment.NewLine, errors));

        if (rows.Count == 0)
            throw new MarketCloseCsvValidationException("CSV contains no valid data rows.");

        return rows;
    }

    private static string KeyFor(DateTime date, string symbol)
        => $"{date:yyyy-MM-dd}|{symbol}";
}

public sealed record MarketCloseCsvRow(DateTime Date, string Symbol, double Close);

public sealed record MarketCloseImportResult(int InputRows, int Inserted, int Updated, int Unchanged);

public sealed class MarketCloseCsvValidationException(string message) : Exception(message);