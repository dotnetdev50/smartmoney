using SmartMoney.Application.Scoring;
using SmartMoney.Domain.Enums;
using SmartMoney.Job.Export;
using Xunit;

namespace SmartMoney.Application.Tests.Export;

public sealed class MarketNarrativeTests
{
    [Theory]
    [InlineData(22.5, "neutral", "mild strength", "+22.5")]
    [InlineData(-22.5, "neutral", "mild strength", "-22.5")]
    [InlineData(40.0, "bullish", "moderate strength", "+40.0")]
    [InlineData(-40.0, "bearish", "moderate strength", "-40.0")]
    [InlineData(0.0, "neutral", "neutral strength", "0.0")]
    public void Explanation_ReportsCanonicalJobClassificationStrengthAndValue(
        double score,
        string biasLabel,
        string strength,
        string formattedScore)
    {
        var text = Explain(score: score);

        Assert.Contains($"Composite bias is {biasLabel}", text);
        Assert.Contains(strength, text);
        Assert.Contains($"({formattedScore})", text);
    }

    [Theory]
    [InlineData("NORMAL", 1.3, "Normal regime with ShockScore 1.3")]
    [InlineData("SHOCK", 2.7, "Shock regime with ShockScore 2.7")]
    public void Explanation_ReportsRegimeAsModelClassification(string regime, double shockScore, string expected)
    {
        var text = Explain(regime: regime, shockScore: shockScore);

        Assert.Contains("model classification", text);
        Assert.Contains(expected, text);
        Assert.DoesNotContain("detected", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explanation_ReportsWeightedParticipantAndIndicatorDrivers()
    {
        var text = Explain(model: Model(participantDriver: "FII", indicatorDriver: "Futures"));

        Assert.Contains("FII is the largest weighted participant driver", text);
        Assert.Contains("futures contributing most", text);
    }

    [Theory]
    [InlineData("SmartBullRetailBear", "Oppose", "Mixed", "opposite directions", "DII opposes", "contributions are mixed")]
    [InlineData("BothBull", "Agree", "Aligned", "are aligned", "DII agrees", "contributions are aligned")]
    [InlineData("MixedNeutral", "Neutral", "Neutral", "mixed or neutral", "relationship to Smart positioning is neutral", "contributions are neutral")]
    public void Explanation_ReportsStructureDiiAndParticipantAlignment(
        string state,
        string diiRelationship,
        string alignment,
        string expectedState,
        string expectedDii,
        string expectedAlignment)
    {
        var text = Explain(model: Model(
            smartRetailState: state,
            diiRelationship: diiRelationship,
            participantAlignment: alignment,
            divergence: 0.82));

        Assert.Contains(expectedState, text);
        Assert.Contains("divergence 0.82", text);
        Assert.Contains(expectedDii, text);
        Assert.Contains(expectedAlignment, text);
    }

    [Fact]
    public void Explanation_AllZeroDiagnosticsHaveNoDriversAndNeutralStructure()
    {
        var text = Explain(
            score: 0,
            model: Model(
                participantDriver: null,
                indicatorDriver: null,
                smartRetailState: "MixedNeutral",
                diiRelationship: "Neutral",
                participantAlignment: "Neutral",
                divergence: 0));

        Assert.Contains("No weighted participant or indicator driver is present", text);
        Assert.Contains("divergence 0.00", text);
        Assert.Contains("contributions are neutral", text);
    }

    [Fact]
    public void Explanation_MissingOneDriverIsHandledDeterministically()
    {
        var noParticipant = Explain(model: Model(participantDriver: null, indicatorDriver: "Puts"));
        var noIndicator = Explain(model: Model(participantDriver: "PRO", indicatorDriver: null));

        Assert.Contains("No weighted participant driver is present; puts contributes most", noParticipant);
        Assert.Contains("PRO is the largest weighted participant driver; no indicator driver is present", noIndicator);
    }

    [Fact]
    public void Explanation_WithIdenticalInputsIsDeterministic()
    {
        var model = Model(divergence: -1.25);

        Assert.Equal(Explain(-31, "SHOCK", 2.3, model), Explain(-31, "SHOCK", 2.3, model));
    }

    [Fact]
    public void NarrativeCallDoesNotChangeScoringOutputs()
    {
        var scoring = new MarketScoringCalculator();
        var biases = new Dictionary<ParticipantType, double>
        {
            [ParticipantType.FII] = 1.2,
            [ParticipantType.Pro] = -0.5,
            [ParticipantType.DII] = 0.8,
            [ParticipantType.Retail] = 0.0
        };
        var rawBefore = scoring.ComputeMarketRawScore(biases);
        var finalBefore = scoring.ComputeFinalScore(rawBefore);
        var regimeBefore = scoring.ComputeRegime(1.7);

        _ = Explain(finalBefore, "SHOCK", 1.7, Model());

        Assert.Equal(rawBefore, scoring.ComputeMarketRawScore(biases));
        Assert.Equal(finalBefore, scoring.ComputeFinalScore(rawBefore));
        Assert.Equal(regimeBefore, scoring.ComputeRegime(1.7));
    }

    private static string Explain(
        double score = 22.5,
        string regime = "NORMAL",
        double shockScore = 1.2,
        MarketNarrativeDecomposition? model = null)
        => MarketNarrative.Explanation(regime, shockScore, score, model ?? Model());

    private static MarketNarrativeDecomposition Model(
        string? participantDriver = "FII",
        string? indicatorDriver = "Futures",
        string smartRetailState = "SmartBullRetailBear",
        string diiRelationship = "Oppose",
        string participantAlignment = "Mixed",
        double divergence = 0.82)
        => new(
            participant_contributions: [],
            main_participant_driver: participantDriver,
            indicator_contributions: [],
            main_indicator_driver: indicatorDriver,
            participant_counts: new SignCountsDto(1, 1, 2),
            indicator_counts: new SignCountsDto(1, 1, 1),
            participant_concentration: 0.5,
            participant_alignment: participantAlignment,
            indicator_alignment: "Mixed",
            dii_smart_relationship: diiRelationship,
            smart_bias: 0.7,
            retail_bias: -0.12,
            dii_bias: -0.2,
            smart_retail_divergence: divergence,
            smart_dii_divergence: 0.9,
            smart_retail_state: smartRetailState);
}
