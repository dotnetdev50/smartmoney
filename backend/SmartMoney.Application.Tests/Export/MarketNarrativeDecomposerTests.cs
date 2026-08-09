using SmartMoney.Application.Scoring;
using SmartMoney.Domain.Entities;
using SmartMoney.Domain.Enums;
using SmartMoney.Job.Export;
using Xunit;

namespace SmartMoney.Application.Tests.Export;

public sealed class MarketNarrativeDecomposerTests
{
    private readonly MarketScoringCalculator scoring = new();

    [Fact]
    public void Decompose_SelectsLargestAbsoluteWeightedParticipantContribution()
    {
        var result = Decompose(
            Metric(ParticipantType.FII, bias: 2.0),
            Metric(ParticipantType.Pro, bias: 1.0),
            Metric(ParticipantType.DII, bias: -1.0),
            Metric(ParticipantType.Retail, bias: -1.0));

        Assert.Equal("FII", result.main_participant_driver);
        Assert.Equal(0.8, Contribution(result.participant_contributions, "FII"), 12);
        Assert.Equal(0.3, Contribution(result.participant_contributions, "PRO"), 12);
        Assert.Equal(-0.2, Contribution(result.participant_contributions, "DII"), 12);
        Assert.Equal(-0.1, Contribution(result.participant_contributions, "RETAIL"), 12);
    }

    [Fact]
    public void Decompose_WeightedLeaderCanDifferFromRawBiasLeader()
    {
        var result = Decompose(
            Metric(ParticipantType.FII, bias: 2.0),
            Metric(ParticipantType.DII, bias: 3.0));

        Assert.Equal("FII", result.main_participant_driver);
        Assert.Equal(0.8, Contribution(result.participant_contributions, "FII"), 12);
        Assert.Equal(0.6, Contribution(result.participant_contributions, "DII"), 12);
    }

    [Fact]
    public void Decompose_ComputesRegimeBlendedIndicatorContributionsAndDriver()
    {
        var result = Decompose(
            Metric(ParticipantType.FII, bias: 0, futuresShort: 2, futuresLong: 1, putShort: 1, putLong: 3, callShort: 4, callLong: 2),
            Metric(ParticipantType.Pro, bias: 0, futuresShort: -1, futuresLong: -2, putShort: 2, putLong: 1, callShort: -1, callLong: -3));

        Assert.Equal(0.005, Contribution(result.indicator_contributions, "Futures"), 12);
        Assert.Equal(0.405, Contribution(result.indicator_contributions, "Puts"), 12);
        Assert.Equal(-0.064, Contribution(result.indicator_contributions, "Calls"), 12);
        Assert.Equal("Puts", result.main_indicator_driver);
    }

    [Fact]
    public void Decompose_ComputesConcentrationCountsAndMixedFlags()
    {
        var result = Decompose(
            Metric(ParticipantType.FII, bias: 2.0, futuresShort: 1, futuresLong: 1),
            Metric(ParticipantType.DII, bias: -3.0, putShort: 1, putLong: 1));

        Assert.Equal(0.8 / 1.4, result.participant_concentration, 12);
        Assert.Equal(new SignCountsDto(1, 1, 2), result.participant_counts);
        Assert.Equal(new SignCountsDto(2, 0, 1), result.indicator_counts);
        Assert.Equal("Mixed", result.participant_alignment);
        Assert.Equal("Aligned", result.indicator_alignment);
    }

    [Fact]
    public void Decompose_ZeroInputsHaveZeroConcentrationNeutralCountsAndNoDrivers()
    {
        var result = Decompose(Metric(ParticipantType.FII, bias: 0));

        Assert.Equal(0, result.participant_concentration);
        Assert.Equal(new SignCountsDto(0, 0, 4), result.participant_counts);
        Assert.Equal(new SignCountsDto(0, 0, 3), result.indicator_counts);
        Assert.Equal("Neutral", result.participant_alignment);
        Assert.Equal("Neutral", result.indicator_alignment);
        Assert.Null(result.main_participant_driver);
        Assert.Null(result.main_indicator_driver);
    }

    [Theory]
    [InlineData(1.0, 0.5, "Agree")]
    [InlineData(1.0, -0.5, "Oppose")]
    [InlineData(0.0, -0.5, "Neutral")]
    public void Decompose_ClassifiesDiiRelationship(double fiiBias, double diiBias, string expected)
    {
        var result = Decompose(
            Metric(ParticipantType.FII, bias: fiiBias),
            Metric(ParticipantType.DII, bias: diiBias));

        Assert.Equal(expected, result.dii_smart_relationship);
    }

    [Fact]
    public void ScoreClassification_ZeroIsNeutral()
    {
        Assert.Equal("neutral", MarketNarrative.ScoreDirection(0));
        Assert.Equal(("Neutral", "Neutral"), MarketNarrative.ScoreLabel(0));
    }

    [Fact]
    public void Decomposition_DoesNotChangeScoringOutputs()
    {
        var metrics = new[]
        {
            Metric(ParticipantType.FII, 1.2, 1, 2, 3, 4, 5, 6),
            Metric(ParticipantType.Pro, -0.5, -1, -2, -3, -4, -5, -6),
            Metric(ParticipantType.DII, 0.8),
            Metric(ParticipantType.Retail, 0.1)
        };
        var biases = metrics.ToDictionary(x => x.Participant, x => x.ParticipantBias);
        var rawBefore = scoring.ComputeMarketRawScore(biases);
        var finalBefore = scoring.ComputeFinalScore(rawBefore);
        var regimeBefore = scoring.ComputeRegime(1.7);

        _ = MarketNarrativeDecomposer.Decompose(metrics, regimeBefore, scoring);

        Assert.Equal(rawBefore, scoring.ComputeMarketRawScore(biases));
        Assert.Equal(finalBefore, scoring.ComputeFinalScore(rawBefore));
        Assert.Equal(regimeBefore, scoring.ComputeRegime(1.7));
    }

    private MarketNarrativeDecomposition Decompose(params ParticipantMetric[] metrics)
        => MarketNarrativeDecomposer.Decompose(metrics, Regime.Normal, scoring);

    private static double Contribution(IReadOnlyList<ContributionDto> contributions, string name)
        => contributions.Single(x => x.name == name).contribution;

    private static ParticipantMetric Metric(
        ParticipantType participant,
        double bias,
        double futuresShort = 0,
        double futuresLong = 0,
        double putShort = 0,
        double putLong = 0,
        double callShort = 0,
        double callLong = 0)
        => new()
        {
            Participant = participant,
            ParticipantBias = bias,
            FuturesZShort = futuresShort,
            FuturesZLong = futuresLong,
            PutZShort = putShort,
            PutZLong = putLong,
            CallZShort = callShort,
            CallZLong = callLong
        };
}
