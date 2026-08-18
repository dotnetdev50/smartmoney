namespace SmartMoney.Job.AI;

public sealed record MarketInterpretationContribution(
    string name,
    double contribution);

public sealed record MarketInterpretationInput(
    string signal_date,
    double final_score,
    string displayed_direction,
    string strength,
    string regime,
    double shock_score,
    IReadOnlyList<MarketInterpretationContribution> participant_contributions,
    string? main_participant_driver,
    IReadOnlyList<MarketInterpretationContribution> indicator_contributions,
    string? main_indicator_driver,
    double smart_bias,
    double retail_bias,
    double dii_bias,
    double smart_retail_divergence,
    double smart_dii_divergence,
    string smart_retail_state,
    double participant_concentration,
    string participant_alignment,
    string indicator_alignment,
    string dii_smart_relationship,
    string deterministic_explanation);

public sealed record MarketInterpretationResult(
    string summary,
    string key_observation,
    string uncertainty,
    string? context = null);

public enum MarketInterpretationStatus
{
    Generated,
    Reused,
    Unavailable,
    Invalid
}

public sealed record MarketInterpretationAttempt(
    MarketInterpretationStatus status,
    string prompt_version,
    string input_fingerprint,
    DateTimeOffset? generated_at = null,
    MarketInterpretationResult? interpretation = null,
    string? failure_category = null);

public sealed record MarketInterpretationValidationResult(
    bool is_valid,
    IReadOnlyList<string> errors)
{
    public static MarketInterpretationValidationResult Valid { get; } = new(true, []);
}

public sealed record MarketInterpretationFingerprintContext(
    string prompt_version,
    string prompt_content,
    string provider,
    string model,
    IReadOnlyDictionary<string, string>? generation_settings = null);

public sealed record MarketInterpretationPrompt(
    string version,
    string content);
