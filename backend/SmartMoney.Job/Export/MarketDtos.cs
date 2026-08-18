using System.Text.Json.Serialization;

namespace SmartMoney.Job.Export;

public sealed record ParticipantDto(
    string name,
    double bias,
    string? label = null
);

public sealed record ParticipantActivityRowDto(
    string participant,
    string instrument,
    double net_oi_change,
    double? vs_yesterday_pct = null
);

public sealed record AiInterpretationDto(
    string status,
    string prompt_version,
    string? input_fingerprint = null,
    DateTimeOffset? generated_at = null,
    string? summary = null,
    string? key_observation = null,
    string? uncertainty = null,
    string? context = null
);

public sealed record MarketTodayDto(
    string index,
    string date,
    double final_score,
    string regime,
    double shock_score,
    IReadOnlyList<ParticipantDto> participants,
    string? bias_Label = null,
    string? strength = null,
    string? explanation = null,
    double? pcr = null,
    double? vix = null,
    double? pcr_volume = null,
    double? banknifty_pcr = null,
    double? banknifty_pcr_volume = null,
    IReadOnlyList<ParticipantActivityRowDto>? participant_activity = null,
    double? smart_bias = null,
    double? retail_bias = null,
    double? dii_bias = null,
    double? smart_retail_divergence = null,
    double? smart_dii_divergence = null,
    string? smart_retail_state = null,
    MarketNarrativeDecomposition? decomposition = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    AiInterpretationDto? ai_interpretation = null
);

public sealed record MarketHistoryPointDto(
    string date,
    double final_score,
    string regime
);
