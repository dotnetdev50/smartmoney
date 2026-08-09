using SmartMoney.Application.Scoring;
using SmartMoney.Domain.Entities;
using SmartMoney.Domain.Enums;

namespace SmartMoney.Job.Export;

public sealed record ContributionDto(string name, double contribution);

public sealed record SignCountsDto(int positive, int negative, int zero);

public sealed record MarketNarrativeDecomposition(
    IReadOnlyList<ContributionDto> participant_contributions,
    string? main_participant_driver,
    IReadOnlyList<ContributionDto> indicator_contributions,
    string? main_indicator_driver,
    SignCountsDto participant_counts,
    SignCountsDto indicator_counts,
    double participant_concentration,
    string participant_alignment,
    string indicator_alignment,
    string dii_smart_relationship,
    double smart_bias,
    double retail_bias,
    double dii_bias,
    double smart_retail_divergence,
    double smart_dii_divergence,
    string smart_retail_state);

public static class MarketNarrativeDecomposer
{
    public static MarketNarrativeDecomposition Decompose(
        IReadOnlyList<ParticipantMetric> metrics,
        Regime regime,
        MarketScoringCalculator scoring)
    {
        var orderedParticipants = new[]
        {
            ParticipantType.FII,
            ParticipantType.Pro,
            ParticipantType.DII,
            ParticipantType.Retail
        };

        var metricMap = metrics
            .GroupBy(m => m.Participant)
            .ToDictionary(g => g.Key, g => g.Last());
        var biasMap = metricMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ParticipantBias);

        var participantContributions = orderedParticipants
            .Select(participant => new ContributionDto(
                participant.ToString().ToUpperInvariant(),
                scoring.GetParticipantWeight(participant) * GetBias(biasMap, participant)))
            .ToList();

        double futuresContribution = 0;
        double putContribution = 0;
        double callContribution = 0;

        foreach (var participant in orderedParticipants)
        {
            if (!metricMap.TryGetValue(participant, out var metric)) continue;

            var participantWeight = scoring.GetParticipantWeight(participant);
            futuresContribution += participantWeight * 0.5 * scoring.Blend(metric.FuturesZShort, metric.FuturesZLong, regime);
            putContribution += participantWeight * 0.3 * scoring.Blend(metric.PutZShort, metric.PutZLong, regime);
            callContribution += participantWeight * -0.2 * scoring.Blend(metric.CallZShort, metric.CallZLong, regime);
        }

        var indicatorContributions = new List<ContributionDto>
        {
            new("Futures", futuresContribution),
            new("Puts", putContribution),
            new("Calls", callContribution)
        };

        var smartBias = scoring.ComputeSmartBiasNormalized(biasMap);
        var retailBias = scoring.ComputeRetailBias(biasMap);
        var diiBias = scoring.ComputeDiiBias(biasMap);
        var absoluteParticipantTotal = participantContributions.Sum(x => Math.Abs(x.contribution));
        var maximumParticipantContribution = participantContributions.Max(x => Math.Abs(x.contribution));

        return new MarketNarrativeDecomposition(
            participantContributions,
            MainDriver(participantContributions),
            indicatorContributions,
            MainDriver(indicatorContributions),
            CountSigns(participantContributions.Select(x => x.contribution)),
            CountSigns(indicatorContributions.Select(x => x.contribution)),
            absoluteParticipantTotal == 0 ? 0 : maximumParticipantContribution / absoluteParticipantTotal,
            Alignment(participantContributions.Select(x => x.contribution)),
            Alignment(indicatorContributions.Select(x => x.contribution)),
            Relationship(smartBias, diiBias),
            smartBias,
            retailBias,
            diiBias,
            scoring.ComputeSmartRetailDivergence(biasMap),
            scoring.ComputeSmartDiiDivergence(biasMap),
            scoring.ComputeSmartRetailState(biasMap).ToString());
    }

    private static double GetBias(IReadOnlyDictionary<ParticipantType, double> biases, ParticipantType participant)
        => biases.TryGetValue(participant, out var bias) ? bias : 0;

    private static string? MainDriver(IReadOnlyList<ContributionDto> contributions)
    {
        var driver = contributions.OrderByDescending(x => Math.Abs(x.contribution)).First();
        return driver.contribution == 0 ? null : driver.name;
    }

    private static SignCountsDto CountSigns(IEnumerable<double> values)
    {
        var signs = values.Select(Math.Sign).ToList();
        return new SignCountsDto(signs.Count(x => x > 0), signs.Count(x => x < 0), signs.Count(x => x == 0));
    }

    private static string Alignment(IEnumerable<double> values)
    {
        var signs = values.Select(Math.Sign).Where(x => x != 0).Distinct().ToList();
        if (signs.Count == 0) return "Neutral";
        return signs.Count == 1 ? "Aligned" : "Mixed";
    }

    private static string Relationship(double smartBias, double diiBias)
    {
        var smartSign = Math.Sign(smartBias);
        var diiSign = Math.Sign(diiBias);
        if (smartSign == 0 || diiSign == 0) return "Neutral";
        return smartSign == diiSign ? "Agree" : "Oppose";
    }
}
