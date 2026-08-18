using SmartMoney.Job.Export;

namespace SmartMoney.Job.AI;

public static class MarketInterpretationInputFactory
{
    public static MarketInterpretationInput Create(
        string signalDate,
        double finalScore,
        string displayedDirection,
        string strength,
        string regime,
        double shockScore,
        MarketNarrativeDecomposition decomposition,
        string deterministicExplanation)
    {
        ArgumentNullException.ThrowIfNull(decomposition);

        return new MarketInterpretationInput(
            signal_date: signalDate,
            final_score: finalScore,
            displayed_direction: displayedDirection,
            strength: strength,
            regime: regime,
            shock_score: shockScore,
            participant_contributions: decomposition.participant_contributions
                .Select(x => new MarketInterpretationContribution(x.name, x.contribution))
                .ToArray(),
            main_participant_driver: decomposition.main_participant_driver,
            indicator_contributions: decomposition.indicator_contributions
                .Select(x => new MarketInterpretationContribution(x.name, x.contribution))
                .ToArray(),
            main_indicator_driver: decomposition.main_indicator_driver,
            smart_bias: decomposition.smart_bias,
            retail_bias: decomposition.retail_bias,
            dii_bias: decomposition.dii_bias,
            smart_retail_divergence: decomposition.smart_retail_divergence,
            smart_dii_divergence: decomposition.smart_dii_divergence,
            smart_retail_state: decomposition.smart_retail_state,
            participant_concentration: decomposition.participant_concentration,
            participant_alignment: decomposition.participant_alignment,
            indicator_alignment: decomposition.indicator_alignment,
            dii_smart_relationship: decomposition.dii_smart_relationship,
            deterministic_explanation: deterministicExplanation);
    }
}
