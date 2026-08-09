using SmartMoney.Domain.Enums;

namespace SmartMoney.Application.Backtesting;

public enum BacktestHorizon
{
    T1 = 1,
    T3 = 3,
    T5 = 5
}

public enum ScoreBucket
{
    StrongNegative,
    MildNegative,
    Neutral,
    MildPositive,
    StrongPositive
}

public sealed record BacktestSignalInput(DateTime SignalDate, double FinalScore, Regime Regime);

public sealed record BacktestMarketClose(DateTime Date, double Close);

public sealed record BacktestEvaluationRow(
    DateTime SignalDate,
    double FinalScore,
    Regime Regime,
    ScoreBucket ScoreBucket,
    BacktestHorizon Horizon,
    DateTime FutureDate,
    double SignalClose,
    double FutureClose,
    double ForwardReturn,
    bool? IsDirectionallyCorrect
);

public sealed record BacktestMetrics(
    int SampleCount,
    double? DirectionalAccuracy,
    double? MeanReturn,
    double? MedianReturn,
    double? MeanReturnPositiveSignals,
    double? MeanReturnNegativeSignals,
    double? PearsonCorrelationScoreVsForwardReturn
);

public sealed record BacktestEvaluationReport(
    IReadOnlyList<BacktestEvaluationRow> Evaluations,
    IReadOnlyDictionary<BacktestHorizon, BacktestMetrics> MetricsByHorizon,
    IReadOnlyDictionary<Regime, IReadOnlyDictionary<BacktestHorizon, BacktestMetrics>> MetricsByRegime,
    IReadOnlyDictionary<ScoreBucket, IReadOnlyDictionary<BacktestHorizon, BacktestMetrics>> MetricsByScoreBucket
);

public sealed class BacktestEvaluator
{
    private static readonly BacktestHorizon[] Horizons =
    [
        BacktestHorizon.T1,
        BacktestHorizon.T3,
        BacktestHorizon.T5
    ];

    public BacktestEvaluationReport Evaluate(
        IReadOnlyList<BacktestSignalInput> signals,
        IReadOnlyList<BacktestMarketClose> closes)
    {
        ValidateCloses(closes);

        var orderedCloses = closes
            .Select(x => new BacktestMarketClose(x.Date.Date, x.Close))
            .OrderBy(x => x.Date)
            .ToList();

        var closeIndexByDate = orderedCloses
            .Select((x, i) => new { x.Date, Index = i })
            .ToDictionary(x => x.Date, x => x.Index);

        var evaluations = new List<BacktestEvaluationRow>();

        foreach (var signal in signals.OrderBy(x => x.SignalDate))
        {
            var signalDate = signal.SignalDate.Date;
            if (!closeIndexByDate.TryGetValue(signalDate, out var startIndex))
                continue;

            var signalClose = orderedCloses[startIndex].Close;
            var bucket = ClassifyBucket(signal.FinalScore);

            foreach (var horizon in Horizons)
            {
                var futureIndex = startIndex + (int)horizon;
                if (futureIndex >= orderedCloses.Count)
                    continue;

                var future = orderedCloses[futureIndex];
                var forwardReturn = (future.Close - signalClose) / signalClose;
                var directional = DirectionalCorrectness(signal.FinalScore, forwardReturn);

                evaluations.Add(new BacktestEvaluationRow(
                    SignalDate: signalDate,
                    FinalScore: signal.FinalScore,
                    Regime: signal.Regime,
                    ScoreBucket: bucket,
                    Horizon: horizon,
                    FutureDate: future.Date,
                    SignalClose: signalClose,
                    FutureClose: future.Close,
                    ForwardReturn: forwardReturn,
                    IsDirectionallyCorrect: directional));
            }
        }

        var metricsByHorizon = BuildByHorizon(evaluations);
        var metricsByRegime = BuildByRegime(evaluations);
        var metricsByBucket = BuildByBucket(evaluations);

        return new BacktestEvaluationReport(
            Evaluations: evaluations,
            MetricsByHorizon: metricsByHorizon,
            MetricsByRegime: metricsByRegime,
            MetricsByScoreBucket: metricsByBucket);
    }

    public static ScoreBucket ClassifyBucket(double score)
    {
        if (score <= -20) return ScoreBucket.StrongNegative;
        if (score < -5) return ScoreBucket.MildNegative;
        if (score <= 5) return ScoreBucket.Neutral;
        if (score < 20) return ScoreBucket.MildPositive;
        return ScoreBucket.StrongPositive;
    }

    private static bool? DirectionalCorrectness(double score, double forwardReturn)
    {
        if (score == 0) return null;
        if (score > 0) return forwardReturn > 0;
        return forwardReturn < 0;
    }

    private static IReadOnlyDictionary<BacktestHorizon, BacktestMetrics> BuildByHorizon(
        IReadOnlyList<BacktestEvaluationRow> rows)
    {
        var result = new Dictionary<BacktestHorizon, BacktestMetrics>();
        foreach (var horizon in Horizons)
        {
            var horizonRows = rows.Where(x => x.Horizon == horizon).ToList();
            result[horizon] = ComputeMetrics(horizonRows);
        }

        return result;
    }

    private static IReadOnlyDictionary<Regime, IReadOnlyDictionary<BacktestHorizon, BacktestMetrics>> BuildByRegime(
        IReadOnlyList<BacktestEvaluationRow> rows)
    {
        var result = new Dictionary<Regime, IReadOnlyDictionary<BacktestHorizon, BacktestMetrics>>();
        foreach (var regime in Enum.GetValues<Regime>())
        {
            var regimeRows = rows.Where(x => x.Regime == regime).ToList();
            result[regime] = BuildByHorizon(regimeRows);
        }

        return result;
    }

    private static IReadOnlyDictionary<ScoreBucket, IReadOnlyDictionary<BacktestHorizon, BacktestMetrics>> BuildByBucket(
        IReadOnlyList<BacktestEvaluationRow> rows)
    {
        var result = new Dictionary<ScoreBucket, IReadOnlyDictionary<BacktestHorizon, BacktestMetrics>>();
        foreach (var bucket in Enum.GetValues<ScoreBucket>())
        {
            var bucketRows = rows.Where(x => x.ScoreBucket == bucket).ToList();
            result[bucket] = BuildByHorizon(bucketRows);
        }

        return result;
    }

    private static BacktestMetrics ComputeMetrics(IReadOnlyList<BacktestEvaluationRow> rows)
    {
        var scored = rows.Where(x => x.FinalScore != 0).ToList();
        if (scored.Count == 0)
        {
            return new BacktestMetrics(0, null, null, null, null, null, null);
        }

        var directionalRows = scored.Where(x => x.IsDirectionallyCorrect.HasValue).ToList();
        var directionalCorrect = directionalRows.Count(x => x.IsDirectionallyCorrect == true);

        var meanReturn = scored.Average(x => x.ForwardReturn);
        var medianReturn = Median(scored.Select(x => x.ForwardReturn));

        var posRows = scored.Where(x => x.FinalScore > 0).ToList();
        var negRows = scored.Where(x => x.FinalScore < 0).ToList();

        double? meanPositive = posRows.Count > 0 ? posRows.Average(x => x.ForwardReturn) : null;
        double? meanNegative = negRows.Count > 0 ? negRows.Average(x => x.ForwardReturn) : null;

        var corr = PearsonCorrelation(
            scored.Select(x => x.FinalScore).ToList(),
            scored.Select(x => x.ForwardReturn).ToList());

        return new BacktestMetrics(
            SampleCount: scored.Count,
            DirectionalAccuracy: directionalRows.Count > 0 ? (double)directionalCorrect / directionalRows.Count : null,
            MeanReturn: meanReturn,
            MedianReturn: medianReturn,
            MeanReturnPositiveSignals: meanPositive,
            MeanReturnNegativeSignals: meanNegative,
            PearsonCorrelationScoreVsForwardReturn: corr);
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.OrderBy(x => x).ToList();
        var count = ordered.Count;
        if (count == 0) return double.NaN;

        if (count % 2 == 1) return ordered[count / 2];

        var left = ordered[(count / 2) - 1];
        var right = ordered[count / 2];
        return (left + right) / 2.0;
    }

    private static double? PearsonCorrelation(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
    {
        if (xs.Count != ys.Count || xs.Count < 2) return null;

        var meanX = xs.Average();
        var meanY = ys.Average();

        double num = 0;
        double denX = 0;
        double denY = 0;

        for (var i = 0; i < xs.Count; i++)
        {
            var dx = xs[i] - meanX;
            var dy = ys[i] - meanY;
            num += dx * dy;
            denX += dx * dx;
            denY += dy * dy;
        }

        if (denX <= 1e-18 || denY <= 1e-18) return null;

        return num / Math.Sqrt(denX * denY);
    }

    private static void ValidateCloses(IReadOnlyList<BacktestMarketClose> closes)
    {
        var keys = new HashSet<DateTime>();
        foreach (var close in closes)
        {
            if (close.Close <= 0)
                throw new ArgumentException("All close values must be > 0.");

            var date = close.Date.Date;
            if (!keys.Add(date))
                throw new ArgumentException($"Duplicate close date detected: {date:yyyy-MM-dd}");
        }
    }
}