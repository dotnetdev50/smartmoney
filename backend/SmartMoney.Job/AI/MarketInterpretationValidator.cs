using System.Text.RegularExpressions;

namespace SmartMoney.Job.AI;

public sealed class MarketInterpretationValidator
{
    public const int MaximumFieldLength = 400;
    public const int MaximumCombinedLength = 1000;

    private static readonly Regex NumericClaim = new(
        @"(?<![\p{L}\p{N}_])[-+]?(?:\d+(?:[.,]\d+)?|\.\d+)%?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TradingLanguage = new(
        @"\b(?:buy|buys|buying|sell|sells|selling|hold|holds|holding)\b|\bprice\s+target\b|\bguaranteed?\s+outcome\b|\b(?:guarantee(?:d|s)?|certain)\b|\b(?:you\s+should|should|must|consider|recommend)\s+(?:buy|sell|hold|trade|enter|exit|go|buying|selling|holding|trading|entering|exiting|going)\b|\bgo\s+(?:long|short)\b|\btake\s+(?:a\s+)?position\b|\b(?:enter|exit)\s+(?:the|a)\s+(?:market|trade|position)\b|\btrade\s+(?:the|this)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex PredictionLanguage = new(
        @"\b(?:predict(?:s|ed|ion)?|forecast(?:s|ed|ing)?|expected\s+to|likely\s+to|(?:will|may|might|could)\s+(?:rise|fall|rally|decline|increase|decrease|gain|drop|move))\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ExternalFacts = new(
        @"\bnews\b|\bearnings\b|\bpolicy\s+announcements?\b|\beconomic\s+events?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Direction = new(
        @"\b(?<value>bullish|bearish|neutral)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Regime = new(
        @"\b(?<value>shock|normal)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ParticipantDriverClaim = new(
        @"(?:\b(?<name>FII|PRO|DII|Retail)\b.{0,50}\b(?:largest|main|dominant)\b)|(?:\b(?:largest|main|dominant)\b.{0,50}\b(?<name>FII|PRO|DII|Retail)\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex IndicatorDriverClaim = new(
        @"(?:\b(?<name>Futures|Puts|Calls)\b.{0,50}\b(?:largest|main|dominant)\b)|(?:\b(?:largest|main|dominant)\b.{0,50}\b(?<name>Futures|Puts|Calls)\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public MarketInterpretationValidationResult Validate(
        MarketInterpretationResult? result,
        MarketInterpretationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (result is null)
            return new(false, ["response_missing"]);

        var errors = new List<string>();
        var fields = new (string Name, string? Value, bool Required)[]
        {
            ("summary", result.summary, true),
            ("key_observation", result.key_observation, true),
            ("uncertainty", result.uncertainty, true),
            ("context", result.context, false)
        };

        foreach (var field in fields)
        {
            if (field.Required && string.IsNullOrWhiteSpace(field.Value))
                errors.Add($"{field.Name}_required");
            if (field.Value?.Length > MaximumFieldLength)
                errors.Add($"{field.Name}_too_long");
        }

        var combinedLength = fields.Sum(x => x.Value?.Length ?? 0);
        if (combinedLength > MaximumCombinedLength)
            errors.Add("combined_text_too_long");

        var prose = string.Join(" ", fields.Select(x => x.Value).Where(x => !string.IsNullOrWhiteSpace(x)));
        if (TradingLanguage.IsMatch(prose)) errors.Add("trading_recommendation");
        if (PredictionLanguage.IsMatch(prose)) errors.Add("unsupported_prediction");
        if (NumericClaim.IsMatch(prose)) errors.Add("numeric_claim");
        if (ExternalFacts.IsMatch(prose)) errors.Add("external_fact_reference");

        ValidateClassificationMentions(Direction, prose, input.displayed_direction, "direction_contradiction", errors);
        ValidateClassificationMentions(Regime, prose, input.regime, "regime_contradiction", errors);
        ValidateDriverClaims(ParticipantDriverClaim, prose, input.main_participant_driver, "participant_driver_contradiction", errors);
        ValidateDriverClaims(IndicatorDriverClaim, prose, input.main_indicator_driver, "indicator_driver_contradiction", errors);

        return errors.Count == 0
            ? MarketInterpretationValidationResult.Valid
            : new MarketInterpretationValidationResult(false, errors.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static void ValidateClassificationMentions(
        Regex regex,
        string prose,
        string expected,
        string error,
        ICollection<string> errors)
    {
        foreach (Match match in regex.Matches(prose))
        {
            if (!match.Groups["value"].Value.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(error);
                return;
            }
        }
    }

    private static void ValidateDriverClaims(
        Regex regex,
        string prose,
        string? expected,
        string error,
        ICollection<string> errors)
    {
        foreach (Match match in regex.Matches(prose))
        {
            if (expected is null || !match.Groups["name"].Value.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(error);
                return;
            }
        }
    }
}
