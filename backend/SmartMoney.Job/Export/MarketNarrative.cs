namespace SmartMoney.Job.Export;

public static class MarketNarrative
{
    public static (string biasLabel, string strength) ScoreLabel(double score)
    {
        var abs = Math.Abs(score);

        var strength =
            abs >= 70 ? "Strong" :
            abs >= 40 ? "Moderate" :
            abs >= 20 ? "Mild" : "Neutral";

        var biasLabel =
            score >= 40 ? "Bullish" :
            score <= -40 ? "Bearish" : "Neutral";

        return (biasLabel, strength);
    }

    public static string ScoreDirection(double score)
        => score > 0 ? "bullish" : score < 0 ? "bearish" : "neutral";

    public static string ParticipantLabel(double bias)
    {
        var abs = Math.Abs(bias);

        if (abs >= 2.0) return bias > 0 ? "Strong Bullish" : "Strong Bearish";
        if (abs >= 1.0) return bias > 0 ? "Bullish" : "Bearish";
        if (abs >= 0.4) return bias > 0 ? "Mild Bullish" : "Mild Bearish";
        return "Neutral";
    }

    public static string Explanation(
        string regime,
        double shockScore,
        double finalScore,
        MarketNarrativeDecomposition decomposition)
    {
        var (biasLabel, strength) = ScoreLabel(finalScore);
        var composite = $"Composite bias is {biasLabel.ToLowerInvariant()}, with {strength.ToLowerInvariant()} strength ({finalScore:+0.0;-0.0;0.0}).";
        var regimeLine = $"The model classification is {NormalizeRegime(regime)} regime with ShockScore {shockScore:0.0}.";
        var drivers = BuildDriverLine(decomposition);
        var structure = BuildStructureLine(decomposition);
        return string.Join(" ", composite, regimeLine, drivers, structure);
    }

    private static string BuildDriverLine(MarketNarrativeDecomposition decomposition)
    {
        if (decomposition.main_participant_driver is null && decomposition.main_indicator_driver is null)
            return "No weighted participant or indicator driver is present.";
        if (decomposition.main_participant_driver is null)
            return $"No weighted participant driver is present; {decomposition.main_indicator_driver!.ToLowerInvariant()} contributes most to the aggregate score.";
        if (decomposition.main_indicator_driver is null)
            return $"{decomposition.main_participant_driver} is the largest weighted participant driver; no indicator driver is present.";

        return $"{decomposition.main_participant_driver} is the largest weighted participant driver, with {decomposition.main_indicator_driver.ToLowerInvariant()} contributing most to the aggregate score.";
    }

    private static string BuildStructureLine(MarketNarrativeDecomposition decomposition)
    {
        var smartRetail = decomposition.smart_retail_state switch
        {
            "SmartBullRetailBear" or "SmartBearRetailBull" => "Smart participants and Retail are positioned in opposite directions",
            "BothBull" or "BothBear" => "Smart participants and Retail are aligned",
            _ => "Smart/Retail positioning is mixed or neutral"
        };

        var dii = decomposition.dii_smart_relationship switch
        {
            "Agree" => "DII agrees with Smart positioning",
            "Oppose" => "DII opposes Smart positioning",
            _ => "the DII relationship to Smart positioning is neutral"
        };
        var alignment = decomposition.participant_alignment.ToLowerInvariant();
        return $"{smartRetail} (divergence {decomposition.smart_retail_divergence:0.00}); {dii}, and participant contributions are {alignment}.";
    }

    private static string NormalizeRegime(string regime)
        => regime.Equals("SHOCK", StringComparison.OrdinalIgnoreCase) ? "Shock" : "Normal";
}
