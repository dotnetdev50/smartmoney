using SmartMoney.Application.Backtesting;
using SmartMoney.Domain.Enums;
using Xunit;

namespace SmartMoney.Application.Tests.Backtesting;

public sealed class BacktestEvaluatorTests
{
    [Fact]
    public void AlignsTradingObservations_ForT1T3T5()
    {
        var sut = new BacktestEvaluator();

        var closes = new List<BacktestMarketClose>
        {
            new(new DateTime(2026, 1, 1), 100),
            new(new DateTime(2026, 1, 2), 101),
            new(new DateTime(2026, 1, 5), 102),
            new(new DateTime(2026, 1, 6), 103),
            new(new DateTime(2026, 1, 8), 104),
            new(new DateTime(2026, 1, 9), 105),
            new(new DateTime(2026, 1, 12), 106)
        };

        var signals = new List<BacktestSignalInput>
        {
            new(new DateTime(2026, 1, 2), 10, Regime.Normal)
        };

        var report = sut.Evaluate(signals, closes);

        var t1 = report.Evaluations.Single(x => x.Horizon == BacktestHorizon.T1);
        var t3 = report.Evaluations.Single(x => x.Horizon == BacktestHorizon.T3);
        var t5 = report.Evaluations.Single(x => x.Horizon == BacktestHorizon.T5);

        Assert.Equal(new DateTime(2026, 1, 5), t1.FutureDate);
        Assert.Equal(new DateTime(2026, 1, 8), t3.FutureDate);
        Assert.Equal(new DateTime(2026, 1, 12), t5.FutureDate);
    }

    [Fact]
    public void HandlesWeekendAndHolidayGaps_ByObservationIndex()
    {
        var sut = new BacktestEvaluator();

        var closes = new List<BacktestMarketClose>
        {
            new(new DateTime(2026, 2, 13), 200),
            new(new DateTime(2026, 2, 16), 202),
            new(new DateTime(2026, 2, 17), 201),
            new(new DateTime(2026, 2, 19), 203),
            new(new DateTime(2026, 2, 20), 204),
            new(new DateTime(2026, 2, 23), 205)
        };

        var signals = new List<BacktestSignalInput>
        {
            new(new DateTime(2026, 2, 16), 8, Regime.Shock)
        };

        var report = sut.Evaluate(signals, closes);

        Assert.Equal(new DateTime(2026, 2, 17), report.Evaluations.Single(x => x.Horizon == BacktestHorizon.T1).FutureDate);
        Assert.Equal(new DateTime(2026, 2, 20), report.Evaluations.Single(x => x.Horizon == BacktestHorizon.T3).FutureDate);
        Assert.Equal(2, report.Evaluations.Count);
    }

    [Fact]
    public void ComputesForwardReturn_Correctly()
    {
        var sut = new BacktestEvaluator();

        var closes = new List<BacktestMarketClose>
        {
            new(new DateTime(2026, 1, 1), 100),
            new(new DateTime(2026, 1, 2), 110),
            new(new DateTime(2026, 1, 3), 100),
            new(new DateTime(2026, 1, 4), 90),
            new(new DateTime(2026, 1, 5), 95),
            new(new DateTime(2026, 1, 6), 96)
        };

        var signals = new List<BacktestSignalInput>
        {
            new(new DateTime(2026, 1, 1), 10, Regime.Normal)
        };

        var report = sut.Evaluate(signals, closes);
        var t1 = report.Evaluations.Single(x => x.Horizon == BacktestHorizon.T1);

        Assert.InRange(t1.ForwardReturn, 0.1 - 1e-12, 0.1 + 1e-12);
    }

    [Fact]
    public void EvaluatesPositiveAndNegativeDirectionalCorrectness()
    {
        var sut = new BacktestEvaluator();

        var closes = new List<BacktestMarketClose>
        {
            new(new DateTime(2026, 1, 1), 100),
            new(new DateTime(2026, 1, 2), 110),
            new(new DateTime(2026, 1, 3), 90),
            new(new DateTime(2026, 1, 4), 89),
            new(new DateTime(2026, 1, 5), 88),
            new(new DateTime(2026, 1, 6), 87)
        };

        var signals = new List<BacktestSignalInput>
        {
            new(new DateTime(2026, 1, 1), 10, Regime.Normal),
            new(new DateTime(2026, 1, 2), -10, Regime.Shock)
        };

        var report = sut.Evaluate(signals, closes);
        var t1Rows = report.Evaluations.Where(x => x.Horizon == BacktestHorizon.T1).OrderBy(x => x.SignalDate).ToList();

        Assert.Equal(true, t1Rows[0].IsDirectionallyCorrect);
        Assert.Equal(true, t1Rows[1].IsDirectionallyCorrect);
    }

    [Fact]
    public void ExcludesNeutralSignals_FromMetricsSampleCount()
    {
        var sut = new BacktestEvaluator();

        var closes = new List<BacktestMarketClose>
        {
            new(new DateTime(2026, 1, 1), 100),
            new(new DateTime(2026, 1, 2), 101),
            new(new DateTime(2026, 1, 3), 102),
            new(new DateTime(2026, 1, 4), 103),
            new(new DateTime(2026, 1, 5), 104),
            new(new DateTime(2026, 1, 6), 105)
        };

        var signals = new List<BacktestSignalInput>
        {
            new(new DateTime(2026, 1, 1), 0, Regime.Normal),
            new(new DateTime(2026, 1, 2), 5, Regime.Normal)
        };

        var report = sut.Evaluate(signals, closes);
        var t1 = report.MetricsByHorizon[BacktestHorizon.T1];

        Assert.Equal(1, t1.SampleCount);
    }

    [Theory]
    [InlineData(-20, ScoreBucket.StrongNegative)]
    [InlineData(-19.999, ScoreBucket.MildNegative)]
    [InlineData(-5, ScoreBucket.Neutral)]
    [InlineData(0, ScoreBucket.Neutral)]
    [InlineData(5, ScoreBucket.Neutral)]
    [InlineData(5.001, ScoreBucket.MildPositive)]
    [InlineData(20, ScoreBucket.StrongPositive)]
    public void ClassifiesBuckets_WithExpectedBoundaries(double score, ScoreBucket expected)
    {
        var bucket = BacktestEvaluator.ClassifyBucket(score);
        Assert.Equal(expected, bucket);
    }

    [Fact]
    public void ComputesPearsonCorrelation_BasicSanity()
    {
        var sut = new BacktestEvaluator();

        var closes = new List<BacktestMarketClose>
        {
            new(new DateTime(2026, 1, 1), 100),
            new(new DateTime(2026, 1, 2), 110),
            new(new DateTime(2026, 1, 3), 99),
            new(new DateTime(2026, 1, 4), 100),
            new(new DateTime(2026, 1, 5), 100),
            new(new DateTime(2026, 1, 6), 100)
        };

        var signals = new List<BacktestSignalInput>
        {
            new(new DateTime(2026, 1, 1), 1, Regime.Normal),
            new(new DateTime(2026, 1, 2), -1, Regime.Normal)
        };

        var report = sut.Evaluate(signals, closes);
        var corr = report.MetricsByHorizon[BacktestHorizon.T1].PearsonCorrelationScoreVsForwardReturn;

        Assert.NotNull(corr);
        Assert.InRange(corr!.Value, 0.999999, 1.000001);
    }

    [Fact]
    public void ExcludesRows_WhenFutureHorizonMissing()
    {
        var sut = new BacktestEvaluator();

        var closes = new List<BacktestMarketClose>
        {
            new(new DateTime(2026, 1, 1), 100),
            new(new DateTime(2026, 1, 2), 101),
            new(new DateTime(2026, 1, 3), 102),
            new(new DateTime(2026, 1, 4), 103),
            new(new DateTime(2026, 1, 5), 104),
            new(new DateTime(2026, 1, 6), 105)
        };

        var signals = new List<BacktestSignalInput>
        {
            new(new DateTime(2026, 1, 1), 10, Regime.Normal),
            new(new DateTime(2026, 1, 4), 10, Regime.Normal)
        };

        var report = sut.Evaluate(signals, closes);

        Assert.Equal(1, report.MetricsByHorizon[BacktestHorizon.T5].SampleCount);
        Assert.Equal(2, report.MetricsByHorizon[BacktestHorizon.T1].SampleCount);
    }
}