using SmartMoney.Domain.Enums;

namespace SmartMoney.Application.Scoring;

public sealed class MarketScoringCalculator
{
    public const int ShortWindow = 5;
    public const int LongWindow = 20;
    public const double ShockThreshold = 1.5;

    private const double Epsilon = 1e-8;

    public double Z(IReadOnlyList<double> values)
    {
        var mean = values.Average();
        var variance = values.Sum(v => (v - mean) * (v - mean)) / values.Count;
        var std = Math.Sqrt(variance);
        if (Math.Abs(std) < Epsilon) return 0;
        var latestValue = values[^1];
        return (latestValue - mean) / std;
    }

    public (double Short, double Long) ComputeZShortLong(IReadOnlyList<double> values)
    {
        var shortVals = values.TakeLast(ShortWindow).ToList();
        var longVals = values.TakeLast(LongWindow).ToList();
        return (Z(shortVals), Z(longVals));
    }

    public double Blend(double shortZ, double longZ, Regime regime)
        => regime == Regime.Shock
            ? 0.7 * shortZ + 0.3 * longZ
            : 0.7 * longZ + 0.3 * shortZ;

    public double ComputeParticipantBias(double futuresEff, double putEff, double callEff)
        =>
            0.5 * futuresEff +
            0.3 * putEff -
            0.2 * callEff;

    public double GetParticipantWeight(ParticipantType participant)
        => participant switch
        {
            ParticipantType.FII => 0.4,
            ParticipantType.Pro => 0.3,
            ParticipantType.DII => 0.2,
            ParticipantType.Retail => 0.1,
            _ => 0.0
        };

    public double ComputeMarketRawScore(IReadOnlyDictionary<ParticipantType, double> participantBias)
        => participantBias.Sum(kvp => GetParticipantWeight(kvp.Key) * kvp.Value);

    public Regime ComputeRegime(double shockScore)
        => shockScore > ShockThreshold ? Regime.Shock : Regime.Normal;

    public double ComputeFinalScore(double rawBias)
        => Math.Tanh(rawBias / 2.0) * 100.0;

    public double ComputeSmartBiasWeighted(IReadOnlyDictionary<ParticipantType, double> participantBias)
        =>
            GetParticipantBias(participantBias, ParticipantType.FII) * GetParticipantWeight(ParticipantType.FII) +
            GetParticipantBias(participantBias, ParticipantType.Pro) * GetParticipantWeight(ParticipantType.Pro);

    public double ComputeSmartBiasNormalized(IReadOnlyDictionary<ParticipantType, double> participantBias)
        => ComputeSmartBiasWeighted(participantBias) / 0.7;

    public double ComputeRetailBias(IReadOnlyDictionary<ParticipantType, double> participantBias)
        => GetParticipantBias(participantBias, ParticipantType.Retail);

    public double ComputeDiiBias(IReadOnlyDictionary<ParticipantType, double> participantBias)
        => GetParticipantBias(participantBias, ParticipantType.DII);

    public double ComputeSmartRetailDivergence(IReadOnlyDictionary<ParticipantType, double> participantBias)
        => ComputeSmartBiasNormalized(participantBias) - ComputeRetailBias(participantBias);

    public double ComputeSmartDiiDivergence(IReadOnlyDictionary<ParticipantType, double> participantBias)
        => ComputeSmartBiasNormalized(participantBias) - ComputeDiiBias(participantBias);

    public SmartRetailState ComputeSmartRetailState(IReadOnlyDictionary<ParticipantType, double> participantBias)
    {
        var smart = ComputeSmartBiasNormalized(participantBias);
        var retail = ComputeRetailBias(participantBias);

        if (smart > 0 && retail < 0) return SmartRetailState.SmartBullRetailBear;
        if (smart < 0 && retail > 0) return SmartRetailState.SmartBearRetailBull;
        if (smart > 0 && retail > 0) return SmartRetailState.BothBull;
        if (smart < 0 && retail < 0) return SmartRetailState.BothBear;

        return SmartRetailState.MixedNeutral;
    }

    private static double GetParticipantBias(IReadOnlyDictionary<ParticipantType, double> participantBias, ParticipantType participant)
        => participantBias.TryGetValue(participant, out var bias) ? bias : 0.0;
}

public enum SmartRetailState
{
    SmartBullRetailBear,
    SmartBearRetailBull,
    BothBull,
    BothBear,
    MixedNeutral
}