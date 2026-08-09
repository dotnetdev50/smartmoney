using Microsoft.Data.SqlClient;
using SmartMoney.Application.Backtesting;
using SmartMoney.Domain.Enums;
using System.Globalization;
using System.Text;
using System.Text.Json;

const string connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=SmartMoney_Dev;Trusted_Connection=True;TrustServerCertificate=True;";
var fromDate = new DateTime(2026, 1, 30);
var toDate = new DateTime(2026, 2, 27);
const string symbol = "NIFTY50";

var signals = await LoadSignalsAsync(connectionString, fromDate, toDate);
var closes = await LoadClosesAsync(connectionString, symbol);

var evaluator = new BacktestEvaluator();
var report = evaluator.Evaluate(signals, closes);

var repoRoot = FindRepoRoot();
var outDir = Path.Combine(repoRoot, "artifacts", "phase3", "backtest-v1");
Directory.CreateDirectory(outDir);

var evalPath = Path.Combine(outDir, "backtest_evaluations.csv");
var summaryPath = Path.Combine(outDir, "backtest_summary.json");
var readmePath = Path.Combine(outDir, "README.txt");

await WriteEvaluationsCsvAsync(evalPath, report.Evaluations);
var summary = BuildSummary(report, signals.Count, fromDate, toDate, symbol);
await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions
{
    WriteIndented = true
}));

await File.WriteAllLinesAsync(readmePath,
[
    "SmartMoney Phase 3 Step 3.5 - Backtest V1 Export",
    "Inputs:",
    "  - Signals: market_bias (2026-01-30..2026-02-27)",
    "  - Closes: market_close Symbol=NIFTY50",
    "  - Horizons: T+1, T+3, T+5 (trading observations)",
    "Outputs:",
    "  - backtest_evaluations.csv",
    "  - backtest_summary.json",
    "",
    "This run is preliminary due to small sample size (21 signals).",
    "No scoring model tuning was performed."
]);

Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"artifacts_dir={outDir}");

static async Task<List<BacktestSignalInput>> LoadSignalsAsync(string cs, DateTime from, DateTime to)
{
    var list = new List<BacktestSignalInput>();
    await using var conn = new SqlConnection(cs);
    await conn.OpenAsync();

    const string sql = @"
SELECT [Date], FinalScore, Regime
FROM market_bias
WHERE [Date] BETWEEN @from AND @to
ORDER BY [Date];";

    await using var cmd = new SqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@from", from);
    cmd.Parameters.AddWithValue("@to", to);

    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var date = reader.GetDateTime(0).Date;
        var score = reader.GetDouble(1);
        var regimeText = reader.GetString(2);
        var regime = Enum.Parse<Regime>(regimeText, ignoreCase: true);
        list.Add(new BacktestSignalInput(date, score, regime));
    }

    return list;
}

static async Task<List<BacktestMarketClose>> LoadClosesAsync(string cs, string symbol)
{
    var list = new List<BacktestMarketClose>();
    await using var conn = new SqlConnection(cs);
    await conn.OpenAsync();

    const string sql = @"
SELECT [Date], [Close]
FROM market_close
WHERE Symbol = @symbol
ORDER BY [Date];";

    await using var cmd = new SqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@symbol", symbol);

    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var date = reader.GetDateTime(0).Date;
        var close = reader.GetDouble(1);
        list.Add(new BacktestMarketClose(date, close));
    }

    return list;
}

static async Task WriteEvaluationsCsvAsync(string path, IReadOnlyList<BacktestEvaluationRow> rows)
{
    var sb = new StringBuilder();
    sb.AppendLine("SignalDate,FinalScore,Regime,Horizon,SignalClose,FutureDate,FutureClose,ForwardReturn,Correctness");

    foreach (var row in rows.OrderBy(x => x.SignalDate).ThenBy(x => x.Horizon))
    {
        var correctness = row.IsDirectionallyCorrect switch
        {
            true => "Correct",
            false => "Incorrect",
            null => "Neutral"
        };

        sb.Append(row.SignalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
          .Append(row.FinalScore.ToString("G17", CultureInfo.InvariantCulture)).Append(',')
          .Append(row.Regime).Append(',')
          .Append(row.Horizon).Append(',')
          .Append(row.SignalClose.ToString("G17", CultureInfo.InvariantCulture)).Append(',')
          .Append(row.FutureDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
          .Append(row.FutureClose.ToString("G17", CultureInfo.InvariantCulture)).Append(',')
          .Append(row.ForwardReturn.ToString("G17", CultureInfo.InvariantCulture)).Append(',')
          .AppendLine(correctness);
    }

    await File.WriteAllTextAsync(path, sb.ToString());
}

static object BuildSummary(BacktestEvaluationReport report, int signalRows, DateTime from, DateTime to, string symbol)
{
    var overall = Enum.GetValues<BacktestHorizon>()
        .ToDictionary(h => h.ToString(), h => ToMetricsObject(report.MetricsByHorizon[h]));

    var byRegime = Enum.GetValues<Regime>()
        .ToDictionary(
            regime => regime.ToString(),
            regime => Enum.GetValues<BacktestHorizon>()
                .ToDictionary(h => h.ToString(), h => ToMetricsObject(report.MetricsByRegime[regime][h])));

    var byBucket = Enum.GetValues<ScoreBucket>()
        .ToDictionary(
            bucket => bucket.ToString(),
            bucket => Enum.GetValues<BacktestHorizon>()
                .ToDictionary(h => h.ToString(), h => ToMetricsObject(report.MetricsByScoreBucket[bucket][h])));

    var horizonRank = Enum.GetValues<BacktestHorizon>()
        .Select(h => new
        {
            Horizon = h.ToString(),
            Accuracy = report.MetricsByHorizon[h].DirectionalAccuracy,
            Correlation = report.MetricsByHorizon[h].PearsonCorrelationScoreVsForwardReturn,
            Sample = report.MetricsByHorizon[h].SampleCount
        })
        .ToList();

    var best = horizonRank.OrderByDescending(x => x.Accuracy).First();
    var weakest = horizonRank.OrderBy(x => x.Accuracy).First();

    return new
    {
        metadata = new
        {
            score_date_range = $"{from:yyyy-MM-dd}..{to:yyyy-MM-dd}",
            close_symbol = symbol,
            evaluation_rows = report.Evaluations.Count,
            signal_rows = signalRows,
            generated_utc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            note = "Preliminary due to small sample size (21 signals). No model tuning performed."
        },
        overall_by_horizon = overall,
        by_regime = byRegime,
        by_score_bucket = byBucket,
        interpretation = new
        {
            best_horizon_by_directional_accuracy = best.Horizon,
            weakest_horizon_by_directional_accuracy = weakest.Horizon,
            above_50pct_accuracy = new
            {
                T1 = report.MetricsByHorizon[BacktestHorizon.T1].DirectionalAccuracy > 0.5,
                T3 = report.MetricsByHorizon[BacktestHorizon.T3].DirectionalAccuracy > 0.5,
                T5 = report.MetricsByHorizon[BacktestHorizon.T5].DirectionalAccuracy > 0.5
            }
        }
    };
}

static object ToMetricsObject(BacktestMetrics m) => new
{
    sample_count = m.SampleCount,
    directional_accuracy = m.DirectionalAccuracy,
    mean_return = m.MeanReturn,
    median_return = m.MedianReturn,
    mean_return_positive_signals = m.MeanReturnPositiveSignals,
    mean_return_negative_signals = m.MeanReturnNegativeSignals,
    pearson_correlation_score_vs_forward_return = m.PearsonCorrelationScoreVsForwardReturn
};

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        var hasFrontend = Directory.Exists(Path.Combine(dir.FullName, "frontend"));
        var hasBackend = Directory.Exists(Path.Combine(dir.FullName, "backend"));
        if (hasFrontend && hasBackend) return dir.FullName;
        dir = dir.Parent;
    }

    throw new InvalidOperationException("Repo root not found (expected /frontend and /backend).");
}
