using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartMoney.Application.Services;
using SmartMoney.Infrastructure.Persistence;
using System.Text;
using Xunit;

namespace SmartMoney.Application.Tests.Services;

public sealed class MarketCloseCsvImportServiceTests
{
    [Fact]
    public async Task ParseCsvAsync_ParsesValidRows()
    {
        using var stream = ToCsvStream(
            "Date,Symbol,Close\n" +
            "2026-01-30,NIFTY50,22500.10\n" +
            "2026-01-31,NIFTY50,22510.25\n");

        var rows = await MarketCloseCsvImportService.ParseCsvAsync(stream, CancellationToken.None);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new DateTime(2026, 1, 30), rows[0].Date);
        Assert.Equal("NIFTY50", rows[0].Symbol);
        Assert.Equal(22500.10, rows[0].Close, 10);
    }

    [Fact]
    public async Task ParseCsvAsync_RejectsInvalidClose()
    {
        using var stream = ToCsvStream(
            "Date,Symbol,Close\n" +
            "2026-01-30,NIFTY50,0\n");

        var ex = await Assert.ThrowsAsync<MarketCloseCsvValidationException>(
            async () => await MarketCloseCsvImportService.ParseCsvAsync(stream, CancellationToken.None));

        Assert.Contains("Close must be > 0", ex.Message);
    }

    [Fact]
    public async Task ParseCsvAsync_RejectsDuplicateDateSymbolInInput()
    {
        using var stream = ToCsvStream(
            "Date,Symbol,Close\n" +
            "2026-01-30,NIFTY50,22500\n" +
            "2026-01-30,NIFTY50,22501\n");

        var ex = await Assert.ThrowsAsync<MarketCloseCsvValidationException>(
            async () => await MarketCloseCsvImportService.ParseCsvAsync(stream, CancellationToken.None));

        Assert.Contains("duplicate Date+Symbol", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportFromCsvStreamAsync_IsIdempotentOnReimport()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var options = new DbContextOptionsBuilder<SmartMoneyDbContext>()
            .UseSqlite(conn)
            .Options;

        await using (var setup = new SmartMoneyDbContext(options))
            await setup.Database.EnsureCreatedAsync();

        await using var db = new SmartMoneyDbContext(options);
        var sut = new MarketCloseCsvImportService(db);

        var csv =
            "Date,Symbol,Close\n" +
            "2026-01-30,NIFTY50,22500.10\n" +
            "2026-01-31,NIFTY50,22510.25\n";

        await using var stream1 = ToCsvStream(csv);
        var first = await sut.ImportFromCsvStreamAsync(stream1, CancellationToken.None);

        await using var stream2 = ToCsvStream(csv);
        var second = await sut.ImportFromCsvStreamAsync(stream2, CancellationToken.None);

        Assert.Equal(2, first.InputRows);
        Assert.Equal(2, first.Inserted);
        Assert.Equal(0, first.Updated);

        Assert.Equal(2, second.InputRows);
        Assert.Equal(0, second.Inserted);
        Assert.Equal(0, second.Updated);
        Assert.Equal(2, second.Unchanged);

        var count = await db.MarketCloses.CountAsync();
        Assert.Equal(2, count);
    }

    private static MemoryStream ToCsvStream(string csv)
        => new(Encoding.UTF8.GetBytes(csv));
}