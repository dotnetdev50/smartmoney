using SmartMoney.Application.Scoring;
using SmartMoney.Domain.Enums;
using Xunit;

namespace SmartMoney.Application.Tests.Scoring;

public sealed class MarketScoringCalculatorTests
{
    private const double Tolerance = 1e-8;

    [Fact]
    public void Z_ReturnsZero_ForConstantValues()
    {
        var sut = new MarketScoringCalculator();

        var result = sut.Z([5, 5, 5, 5, 5]);

        Assert.Equal(0.0, result, 12);
    }

    [Fact]
    public void Z_ReturnsPositiveValue_ForIncreasingAroundMean()
    {
        var sut = new MarketScoringCalculator();

        var result = sut.Z([90, 95, 100, 105, 110]);

        Assert.InRange(result, 1.41421356 - Tolerance, 1.41421356 + Tolerance);
    }

    [Fact]
    public void Z_ReturnsNegativeValue_ForDecreasingAroundMean()
    {
        var sut = new MarketScoringCalculator();

        var result = sut.Z([110, 105, 100, 95, 90]);

        Assert.InRange(result, -1.41421356 - Tolerance, -1.41421356 + Tolerance);
    }

    [Fact]
    public void Z_ReturnsPositiveValue_ForIncreasingOneToTen()
    {
        var sut = new MarketScoringCalculator();

        var result = sut.Z([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);

        Assert.InRange(result, 1.56669890 - Tolerance, 1.56669890 + Tolerance);
    }

    [Fact]
    public void Z_ReturnsNegativeValue_ForDecreasingTenToOne()
    {
        var sut = new MarketScoringCalculator();

        var result = sut.Z([10, 9, 8, 7, 6, 5, 4, 3, 2, 1]);

        Assert.InRange(result, -1.56669890 - Tolerance, -1.56669890 + Tolerance);
    }

    [Fact]
    public void Z_ReturnsZero_WhenStdDevIsNearZero()
    {
        var sut = new MarketScoringCalculator();

        var result = sut.Z([1.0, 1.0, 1.0, 1.0, 1.0 + 1e-12]);

        Assert.Equal(0.0, result, 12);
    }

    [Fact]
    public void Z_WithFiniteInputs_ReturnsFiniteNumber()
    {
        var sut = new MarketScoringCalculator();

        var result = sut.Z([90.0, 95.0, 100.0, 105.0, 110.0]);

        Assert.False(double.IsNaN(result));
        Assert.False(double.IsInfinity(result));
    }

    [Fact]
    public void ComputeZShortLong_UsesOnlyLastFiveValues_ForShortScore()
    {
        var sut = new MarketScoringCalculator();

        var values = Enumerable.Repeat(1000.0, 15)
            .Concat([1.0, 2.0, 3.0, 4.0, 5.0])
            .ToList();

        var (shortZ, _) = sut.ComputeZShortLong(values);

        var expectedShort = ExpectedZ([1.0, 2.0, 3.0, 4.0, 5.0]);

        Assert.InRange(shortZ, expectedShort - Tolerance, expectedShort + Tolerance);
    }

    [Fact]
    public void ComputeZShortLong_UsesOnlyLastTwentyValues_ForLongScore()
    {
        var sut = new MarketScoringCalculator();

        var values = Enumerable.Repeat(-999.0, 7)
            .Concat(Enumerable.Range(1, 20).Select(i => (double)i))
            .ToList();

        var (_, longZ) = sut.ComputeZShortLong(values);

        var expectedLong = ExpectedZ(Enumerable.Range(1, 20).Select(i => (double)i).ToList());

        Assert.InRange(longZ, expectedLong - Tolerance, expectedLong + Tolerance);
    }

    [Fact]
    public void ComputeZShortLong_IgnoresValuesOutsideLastTwenty_WhenInputExceedsTwenty()
    {
        var sut = new MarketScoringCalculator();

        var baseValues = Enumerable.Range(1, 25).Select(i => (double)i).ToList();
        var changedOutsideTwenty = baseValues.ToList();
        changedOutsideTwenty[0] = -5000;
        changedOutsideTwenty[1] = 9000;
        changedOutsideTwenty[2] = -7000;
        changedOutsideTwenty[3] = 8000;
        changedOutsideTwenty[4] = -6000;

        var (shortBase, longBase) = sut.ComputeZShortLong(baseValues);
        var (shortChanged, longChanged) = sut.ComputeZShortLong(changedOutsideTwenty);

        Assert.InRange(shortChanged, shortBase - Tolerance, shortBase + Tolerance);
        Assert.InRange(longChanged, longBase - Tolerance, longBase + Tolerance);
    }

    [Fact]
    public void ComputeZShortLong_ChangingValueInsideLastFive_CanAffectShortAndLong()
    {
        var sut = new MarketScoringCalculator();

        var original = Enumerable.Range(1, 25).Select(i => (double)i).ToList();
        var changed = original.ToList();
        changed[^3] += 25.0;

        var (shortOriginal, longOriginal) = sut.ComputeZShortLong(original);
        var (shortChanged, longChanged) = sut.ComputeZShortLong(changed);

        Assert.True(Math.Abs(shortChanged - shortOriginal) > Tolerance);
        Assert.True(Math.Abs(longChanged - longOriginal) > Tolerance);
    }

    [Fact]
    public void ComputeZShortLong_ChangingValueInsideLastTwentyButOutsideLastFive_AffectsLongNotShort()
    {
        var sut = new MarketScoringCalculator();

        var original = Enumerable.Range(1, 25).Select(i => (double)i).ToList();
        var changed = original.ToList();

        // For length 25: last20 starts at index 5; last5 starts at index 20.
        // Index 10 is inside last20 but outside last5.
        changed[10] += 100.0;

        var (shortOriginal, longOriginal) = sut.ComputeZShortLong(original);
        var (shortChanged, longChanged) = sut.ComputeZShortLong(changed);

        Assert.InRange(shortChanged, shortOriginal - Tolerance, shortOriginal + Tolerance);
        Assert.True(Math.Abs(longChanged - longOriginal) > Tolerance);
    }

    [Fact]
    public void ComputeZShortLong_UsesLatestObservationAsX_ForBothWindows()
    {
        var sut = new MarketScoringCalculator();

        var values = Enumerable.Range(1, 25).Select(i => (double)i).ToList();
        var shortWindow = values.TakeLast(MarketScoringCalculator.ShortWindow).ToList();
        var longWindow = values.TakeLast(MarketScoringCalculator.LongWindow).ToList();

        var expectedShort = ExpectedZ(shortWindow);
        var expectedLong = ExpectedZ(longWindow);

        var (shortZ, longZ) = sut.ComputeZShortLong(values);

        Assert.InRange(shortZ, expectedShort - Tolerance, expectedShort + Tolerance);
        Assert.InRange(longZ, expectedLong - Tolerance, expectedLong + Tolerance);
    }

    [Fact]
    public void ComputeZShortLong_ShortAndLongCanHaveDifferentSigns()
    {
        var sut = new MarketScoringCalculator();

        var values = Enumerable.Repeat(0.0, 15)
            .Concat([100.0, 100.0, 100.0, 100.0, 90.0])
            .ToList();

        var (shortZ, longZ) = sut.ComputeZShortLong(values);

        Assert.True(shortZ < 0);
        Assert.True(longZ > 0);
    }

    [Fact]
    public void ComputeZShortLong_WithExactlyFiveValues_UsesSameWindowForShortAndLong()
    {
        var sut = new MarketScoringCalculator();

        var values = new List<double> { 2, 4, 6, 8, 10 };

        var (shortZ, longZ) = sut.ComputeZShortLong(values);
        var expected = ExpectedZ(values);

        Assert.InRange(shortZ, expected - Tolerance, expected + Tolerance);
        Assert.InRange(longZ, expected - Tolerance, expected + Tolerance);
        Assert.InRange(shortZ, longZ - Tolerance, longZ + Tolerance);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(19)]
    public void ComputeZShortLong_WithSixToNineteenValues_UsesLastFiveForShortAndAllValuesForLong(int length)
    {
        var sut = new MarketScoringCalculator();

        var values = Enumerable.Range(1, length).Select(i => (double)i).ToList();

        var shortWindow = values.TakeLast(5).ToList();
        var expectedShort = ExpectedZ(shortWindow);
        var expectedLong = ExpectedZ(values);

        var (shortZ, longZ) = sut.ComputeZShortLong(values);

        Assert.InRange(shortZ, expectedShort - Tolerance, expectedShort + Tolerance);
        Assert.InRange(longZ, expectedLong - Tolerance, expectedLong + Tolerance);
    }

    [Fact]
    public void ComputeZShortLong_WithExactlyTwentyValues_UsesLastFiveForShortAndAllTwentyForLong()
    {
        var sut = new MarketScoringCalculator();

        var values = Enumerable.Range(1, 20).Select(i => (double)i).ToList();

        var expectedShort = ExpectedZ(values.TakeLast(5).ToList());
        var expectedLong = ExpectedZ(values);

        var (shortZ, longZ) = sut.ComputeZShortLong(values);

        Assert.InRange(shortZ, expectedShort - Tolerance, expectedShort + Tolerance);
        Assert.InRange(longZ, expectedLong - Tolerance, expectedLong + Tolerance);
    }

    [Fact]
    public void ComputeZShortLong_WithMoreThanTwentyValues_UsesLastFiveForShortAndLastTwentyForLong()
    {
        var sut = new MarketScoringCalculator();

        var values = Enumerable.Range(1, 30).Select(i => (double)i).ToList();

        var expectedShort = ExpectedZ(values.TakeLast(5).ToList());
        var expectedLong = ExpectedZ(values.TakeLast(20).ToList());

        var (shortZ, longZ) = sut.ComputeZShortLong(values);

        Assert.InRange(shortZ, expectedShort - Tolerance, expectedShort + Tolerance);
        Assert.InRange(longZ, expectedLong - Tolerance, expectedLong + Tolerance);
    }

    [Fact]
    public void ComputeRegime_WithZeroShockScore_ReturnsNormal()
    {
        var sut = new MarketScoringCalculator();

        var regime = sut.ComputeRegime(0.0);

        Assert.Equal(Regime.Normal, regime);
    }

    [Fact]
    public void ComputeRegime_WithShockScoreOnePointFourNine_ReturnsNormal()
    {
        var sut = new MarketScoringCalculator();

        var regime = sut.ComputeRegime(1.49);

        Assert.Equal(Regime.Normal, regime);
    }

    [Fact]
    public void ComputeRegime_WithShockScoreExactlyOnePointFive_ReturnsNormal()
    {
        var sut = new MarketScoringCalculator();

        var regime = sut.ComputeRegime(1.5);

        Assert.Equal(Regime.Normal, regime);
    }

    [Fact]
    public void ComputeRegime_WithShockScoreOnePointFiveZeroZeroZeroZeroOne_ReturnsShock()
    {
        var sut = new MarketScoringCalculator();

        var regime = sut.ComputeRegime(1.5000001);

        Assert.Equal(Regime.Shock, regime);
    }

    [Fact]
    public void ComputeRegime_WithShockScoreTwoPointZero_ReturnsShock()
    {
        var sut = new MarketScoringCalculator();

        var regime = sut.ComputeRegime(2.0);

        Assert.Equal(Regime.Shock, regime);
    }

    [Fact]
    public void Blend_UsesExpectedWeights_ForShortTenLongTwo()
    {
        var sut = new MarketScoringCalculator();

        var shortZ = 10.0;
        var longZ = 2.0;

        var normal = sut.Blend(shortZ, longZ, Regime.Normal);
        var shock = sut.Blend(shortZ, longZ, Regime.Shock);

        Assert.InRange(normal, 4.4 - Tolerance, 4.4 + Tolerance);
        Assert.InRange(shock, 7.6 - Tolerance, 7.6 + Tolerance);
    }

    [Fact]
    public void Blend_WhenShortEqualsLong_ReturnsSameValueForBothRegimes()
    {
        var sut = new MarketScoringCalculator();

        var shortZ = 3.25;
        var longZ = 3.25;

        var normal = sut.Blend(shortZ, longZ, Regime.Normal);
        var shock = sut.Blend(shortZ, longZ, Regime.Shock);

        Assert.InRange(normal, 3.25 - Tolerance, 3.25 + Tolerance);
        Assert.InRange(shock, 3.25 - Tolerance, 3.25 + Tolerance);
        Assert.InRange(normal, shock - Tolerance, shock + Tolerance);
    }

    [Fact]
    public void Blend_HandlesNegativeValues()
    {
        var sut = new MarketScoringCalculator();

        var shortZ = -8.0;
        var longZ = -2.0;

        var normal = sut.Blend(shortZ, longZ, Regime.Normal);
        var shock = sut.Blend(shortZ, longZ, Regime.Shock);

        Assert.InRange(normal, -3.8 - Tolerance, -3.8 + Tolerance);
        Assert.InRange(shock, -6.2 - Tolerance, -6.2 + Tolerance);
    }

    [Fact]
    public void Blend_HandlesMixedSignValues()
    {
        var sut = new MarketScoringCalculator();

        var shortZ = 4.0;
        var longZ = -2.0;

        var normal = sut.Blend(shortZ, longZ, Regime.Normal);
        var shock = sut.Blend(shortZ, longZ, Regime.Shock);

        Assert.InRange(normal, -0.2 - Tolerance, -0.2 + Tolerance);
        Assert.InRange(shock, 2.2 - Tolerance, 2.2 + Tolerance);
    }

    [Fact]
    public void Blend_WithZeroValues_ReturnsZeroForBothRegimes()
    {
        var sut = new MarketScoringCalculator();

        var normal = sut.Blend(0.0, 0.0, Regime.Normal);
        var shock = sut.Blend(0.0, 0.0, Regime.Shock);

        Assert.Equal(0.0, normal, 12);
        Assert.Equal(0.0, shock, 12);
    }

    [Fact]
    public void Blend_RegimeSwitchLeansTowardDifferentWindowMagnitudes()
    {
        var sut = new MarketScoringCalculator();

        var shortZ = 9.0;
        var longZ = 2.0;

        var normal = sut.Blend(shortZ, longZ, Regime.Normal);
        var shock = sut.Blend(shortZ, longZ, Regime.Shock);

        Assert.True(shock > normal);
        Assert.True(Math.Abs(shock - shortZ) < Math.Abs(normal - shortZ));
        Assert.True(Math.Abs(normal - longZ) < Math.Abs(shock - longZ));
    }

    [Fact]
    public void ComputeParticipantBias_WithAllZeroInputs_ReturnsZero()
    {
        var sut = new MarketScoringCalculator();

        var bias = sut.ComputeParticipantBias(0.0, 0.0, 0.0);

        Assert.Equal(0.0, bias, 12);
    }

    [Fact]
    public void ComputeParticipantBias_WithPositiveInputs_MatchesExpectedValue()
    {
        var sut = new MarketScoringCalculator();

        var bias = sut.ComputeParticipantBias(2.0, 1.0, 1.0);

        Assert.InRange(bias, 1.1 - Tolerance, 1.1 + Tolerance);
    }

    [Fact]
    public void ComputeParticipantBias_WithCallsOnlyPositiveInput_ReturnsNegativeValue()
    {
        var sut = new MarketScoringCalculator();

        var bias = sut.ComputeParticipantBias(0.0, 0.0, 2.0);

        Assert.InRange(bias, -0.4 - Tolerance, -0.4 + Tolerance);
    }

    [Fact]
    public void ComputeParticipantBias_WithAllNegativeInputs_MatchesExpectedValue()
    {
        var sut = new MarketScoringCalculator();

        var bias = sut.ComputeParticipantBias(-2.0, -1.0, -1.0);

        Assert.InRange(bias, -1.1 - Tolerance, -1.1 + Tolerance);
    }

    [Fact]
    public void ComputeParticipantBias_WithMixedSignInputs_MatchesExpectedValue()
    {
        var sut = new MarketScoringCalculator();

        var bias = sut.ComputeParticipantBias(1.5, -2.0, 3.0);

        Assert.InRange(bias, -0.45 - Tolerance, -0.45 + Tolerance);
    }

    [Fact]
    public void ComputeParticipantBias_WithSameMagnitudeInputs_ReflectsUnequalCoefficients()
    {
        var sut = new MarketScoringCalculator();

        var bias = sut.ComputeParticipantBias(2.0, 2.0, 2.0);

        Assert.InRange(bias, 1.2 - Tolerance, 1.2 + Tolerance);
    }

    [Fact]
    public void GetParticipantWeight_MapsFiiToPointFour()
    {
        var sut = new MarketScoringCalculator();

        var weight = sut.GetParticipantWeight(ParticipantType.FII);

        Assert.InRange(weight, 0.4 - Tolerance, 0.4 + Tolerance);
    }

    [Fact]
    public void GetParticipantWeight_MapsProToPointThree()
    {
        var sut = new MarketScoringCalculator();

        var weight = sut.GetParticipantWeight(ParticipantType.Pro);

        Assert.InRange(weight, 0.3 - Tolerance, 0.3 + Tolerance);
    }

    [Fact]
    public void GetParticipantWeight_MapsDiiToPointTwo()
    {
        var sut = new MarketScoringCalculator();

        var weight = sut.GetParticipantWeight(ParticipantType.DII);

        Assert.InRange(weight, 0.2 - Tolerance, 0.2 + Tolerance);
    }

    [Fact]
    public void GetParticipantWeight_MapsRetailToPointOne()
    {
        var sut = new MarketScoringCalculator();

        var weight = sut.GetParticipantWeight(ParticipantType.Retail);

        Assert.InRange(weight, 0.1 - Tolerance, 0.1 + Tolerance);
    }

    [Fact]
    public void GetParticipantWeight_WithUnknownParticipant_ReturnsZero()
    {
        var sut = new MarketScoringCalculator();

        var unknownParticipant = (ParticipantType)999;
        var weight = sut.GetParticipantWeight(unknownParticipant);

        Assert.Equal(0.0, weight, 12);
    }

    [Fact]
    public void ParticipantWeights_SumToOnePointZero()
    {
        var sut = new MarketScoringCalculator();

        var total =
            sut.GetParticipantWeight(ParticipantType.FII) +
            sut.GetParticipantWeight(ParticipantType.Pro) +
            sut.GetParticipantWeight(ParticipantType.DII) +
            sut.GetParticipantWeight(ParticipantType.Retail);

        Assert.InRange(total, 1.0 - Tolerance, 1.0 + Tolerance);
    }

    [Fact]
    public void ComputeMarketRawScore_WithAllZeroParticipantBias_ReturnsZero()
    {
        var sut = new MarketScoringCalculator();

        var participantBias = new Dictionary<ParticipantType, double>
        {
            [ParticipantType.FII] = 0.0,
            [ParticipantType.Pro] = 0.0,
            [ParticipantType.DII] = 0.0,
            [ParticipantType.Retail] = 0.0
        };

        var raw = sut.ComputeMarketRawScore(participantBias);

        Assert.Equal(0.0, raw, 12);
    }

    [Fact]
    public void ComputeMarketRawScore_WithAllParticipantBiasOne_ReturnsOnePointZero()
    {
        var sut = new MarketScoringCalculator();

        var participantBias = new Dictionary<ParticipantType, double>
        {
            [ParticipantType.FII] = 1.0,
            [ParticipantType.Pro] = 1.0,
            [ParticipantType.DII] = 1.0,
            [ParticipantType.Retail] = 1.0
        };

        var raw = sut.ComputeMarketRawScore(participantBias);

        Assert.InRange(raw, 1.0 - Tolerance, 1.0 + Tolerance);
    }

    [Fact]
    public void ComputeMarketRawScore_WithFiiOnlyBiasOne_ReturnsPointFour()
    {
        var sut = new MarketScoringCalculator();

        var participantBias = new Dictionary<ParticipantType, double>
        {
            [ParticipantType.FII] = 1.0,
            [ParticipantType.Pro] = 0.0,
            [ParticipantType.DII] = 0.0,
            [ParticipantType.Retail] = 0.0
        };

        var raw = sut.ComputeMarketRawScore(participantBias);

        Assert.InRange(raw, 0.4 - Tolerance, 0.4 + Tolerance);
    }

    [Fact]
    public void ComputeMarketRawScore_WithRetailOnlyBiasOne_ReturnsPointOne()
    {
        var sut = new MarketScoringCalculator();

        var participantBias = new Dictionary<ParticipantType, double>
        {
            [ParticipantType.FII] = 0.0,
            [ParticipantType.Pro] = 0.0,
            [ParticipantType.DII] = 0.0,
            [ParticipantType.Retail] = 1.0
        };

        var raw = sut.ComputeMarketRawScore(participantBias);

        Assert.InRange(raw, 0.1 - Tolerance, 0.1 + Tolerance);
    }

    [Fact]
    public void ComputeMarketRawScore_WithMixedParticipantBias_MatchesExpectedValue()
    {
        var sut = new MarketScoringCalculator();

        var participantBias = new Dictionary<ParticipantType, double>
        {
            [ParticipantType.FII] = 2.0,
            [ParticipantType.Pro] = -1.0,
            [ParticipantType.DII] = 0.5,
            [ParticipantType.Retail] = 3.0
        };

        var raw = sut.ComputeMarketRawScore(participantBias);

        Assert.InRange(raw, 0.9 - Tolerance, 0.9 + Tolerance);
    }

    [Fact]
    public void ComputeMarketRawScore_WithNegativeParticipantBias_MatchesExpectedValue()
    {
        var sut = new MarketScoringCalculator();

        var participantBias = new Dictionary<ParticipantType, double>
        {
            [ParticipantType.FII] = -1.0,
            [ParticipantType.Pro] = -2.0,
            [ParticipantType.DII] = -3.0,
            [ParticipantType.Retail] = -4.0
        };

        var raw = sut.ComputeMarketRawScore(participantBias);

        Assert.InRange(raw, -2.0 - Tolerance, -2.0 + Tolerance);
    }

    [Fact]
    public void ComputeMarketRawScore_WithUnknownParticipantBias_IgnoresUnknownWeightAsZero()
    {
        var sut = new MarketScoringCalculator();

        var participantBias = new Dictionary<ParticipantType, double>
        {
            [ParticipantType.FII] = 1.0,
            [(ParticipantType)999] = 10.0
        };

        var raw = sut.ComputeMarketRawScore(participantBias);

        Assert.InRange(raw, 0.4 - Tolerance, 0.4 + Tolerance);
    }

    [Fact]
    public void ComputeFinalScore_WithZeroInput_ReturnsZero()
    {
        var sut = new MarketScoringCalculator();

        var finalScore = sut.ComputeFinalScore(0.0);

        Assert.Equal(0.0, finalScore, 12);
    }

    [Fact]
    public void ComputeFinalScore_WithPositiveOne_MatchesExpectedValue()
    {
        var sut = new MarketScoringCalculator();

        var finalScore = sut.ComputeFinalScore(1.0);
        var expected = 46.2117157260;

        Assert.InRange(finalScore, expected - 1e-7, expected + 1e-7);
    }

    [Fact]
    public void ComputeFinalScore_WithNegativeOne_MatchesExpectedValue()
    {
        var sut = new MarketScoringCalculator();

        var finalScore = sut.ComputeFinalScore(-1.0);
        var expected = -46.2117157260;

        Assert.InRange(finalScore, expected - 1e-7, expected + 1e-7);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void ComputeFinalScore_IsOddSymmetric(double x)
    {
        var sut = new MarketScoringCalculator();

        var positive = sut.ComputeFinalScore(x);
        var negative = sut.ComputeFinalScore(-x);

        Assert.InRange(positive, -negative - 1e-7, -negative + 1e-7);
    }

    [Theory]
    [InlineData(0.5, 24.4918662404)]
    [InlineData(1.0, 46.2117157260)]
    [InlineData(2.0, 76.1594155956)]
    [InlineData(-0.5, -24.4918662404)]
    [InlineData(-1.0, -46.2117157260)]
    [InlineData(-2.0, -76.1594155956)]
    public void ComputeFinalScore_MatchesKnownReferenceValues(double rawBias, double expected)
    {
        var sut = new MarketScoringCalculator();

        var finalScore = sut.ComputeFinalScore(rawBias);

        Assert.InRange(finalScore, expected - 1e-7, expected + 1e-7);
    }

    [Fact]
    public void ComputeFinalScore_IsBoundedAndApproachesLimits_ForLargeMagnitudes()
    {
        var sut = new MarketScoringCalculator();

        var plus10 = sut.ComputeFinalScore(10.0);
        var plus100 = sut.ComputeFinalScore(100.0);
        var minus10 = sut.ComputeFinalScore(-10.0);
        var minus100 = sut.ComputeFinalScore(-100.0);

        Assert.True(plus10 < 100.0);
        Assert.True(plus10 > 99.0);
        Assert.True(plus100 <= 100.0);
        Assert.True(plus100 > 99.0);

        Assert.True(minus10 > -100.0);
        Assert.True(minus10 < -99.0);
        Assert.True(minus100 >= -100.0);
        Assert.True(minus100 < -99.0);
    }

    [Fact]
    public void ComputeFinalScore_IsMonotonicIncreasing()
    {
        var sut = new MarketScoringCalculator();

        var inputs = new[] { -3.0, -2.0, -1.0, 0.0, 1.0, 2.0, 3.0 };

        for (var i = 0; i < inputs.Length - 1; i++)
        {
            var left = sut.ComputeFinalScore(inputs[i]);
            var right = sut.ComputeFinalScore(inputs[i + 1]);
            Assert.True(left < right);
        }
    }

    [Fact]
    public void ComputeFinalScore_CompressesLargeMagnitudeChanges()
    {
        var sut = new MarketScoringCalculator();

        var deltaZeroToOne = sut.ComputeFinalScore(1.0) - sut.ComputeFinalScore(0.0);
        var deltaFourToFive = sut.ComputeFinalScore(5.0) - sut.ComputeFinalScore(4.0);

        Assert.True(deltaFourToFive < deltaZeroToOne);
    }

    [Fact]
    public void ComputeFinalScore_WithRepresentativeFiniteRawValues_ReturnsFiniteNumbers()
    {
        var sut = new MarketScoringCalculator();

        var rawValues = new[] { -100.0, -10.0, -2.0, -1.0, -0.5, 0.0, 0.5, 1.0, 2.0, 10.0, 100.0 };

        foreach (var raw in rawValues)
        {
            var score = sut.ComputeFinalScore(raw);
            Assert.False(double.IsNaN(score));
            Assert.False(double.IsInfinity(score));
        }
    }

    [Fact]
    public void EndToEndPureCalculator_ParticipantBiasesToFinalScore_MatchesExpected()
    {
        var sut = new MarketScoringCalculator();

        var participantBias = new Dictionary<ParticipantType, double>
        {
            [ParticipantType.FII] = 1.2,
            [ParticipantType.Pro] = -0.5,
            [ParticipantType.DII] = 0.8,
            [ParticipantType.Retail] = 0.0
        };

        var raw = sut.ComputeMarketRawScore(participantBias);
        var finalScore = sut.ComputeFinalScore(raw);

        var expectedRaw = 0.49;
        var expectedFinal = 24.0212864686;

        Assert.InRange(raw, expectedRaw - Tolerance, expectedRaw + Tolerance);
        Assert.InRange(finalScore, expectedFinal - 1e-7, expectedFinal + 1e-7);
    }

    private static double ExpectedZ(IReadOnlyList<double> values)
    {
        var mean = values.Sum() / values.Count;

        var sumSquares = 0.0;
        foreach (var value in values)
        {
            var diff = value - mean;
            sumSquares += diff * diff;
        }

        var variance = sumSquares / values.Count;
        var std = Math.Sqrt(variance);
        if (Math.Abs(std) < 1e-8) return 0;

        var latest = values[values.Count - 1];
        return (latest - mean) / std;
    }
}
