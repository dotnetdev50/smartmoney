using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SmartMoney.Job.AI;

public static class MarketInterpretationFingerprint
{
    public const string InputSchemaVersion = "market-interpretation-input-v1";
    public const string OutputSchemaVersion = "market-interpretation-output-v1";

    public static string Compute(
        MarketInterpretationInput input,
        MarketInterpretationFingerprintContext context)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("input_schema_version", InputSchemaVersion);
            WriteInput(writer, input);
            writer.WriteString("prompt_version", context.prompt_version);
            writer.WriteString("prompt_content_hash", Hash(context.prompt_content));
            writer.WriteString("provider", context.provider);
            writer.WriteString("model", context.model);
            writer.WriteString("output_schema_version", OutputSchemaVersion);
            writer.WritePropertyName("generation_settings");
            writer.WriteStartObject();
            foreach (var setting in (context.generation_settings ?? new Dictionary<string, string>())
                         .OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                writer.WriteString(setting.Key, setting.Value);
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return $"sha256:{Hash(stream.ToArray())}";
    }

    private static void WriteInput(Utf8JsonWriter writer, MarketInterpretationInput input)
    {
        writer.WritePropertyName("input");
        writer.WriteStartObject();
        writer.WriteString("signal_date", input.signal_date);
        writer.WriteNumber("final_score", input.final_score);
        writer.WriteString("displayed_direction", input.displayed_direction);
        writer.WriteString("strength", input.strength);
        writer.WriteString("regime", input.regime);
        writer.WriteNumber("shock_score", input.shock_score);
        WriteContributions(writer, "participant_contributions", input.participant_contributions);
        WriteNullableString(writer, "main_participant_driver", input.main_participant_driver);
        WriteContributions(writer, "indicator_contributions", input.indicator_contributions);
        WriteNullableString(writer, "main_indicator_driver", input.main_indicator_driver);
        writer.WriteNumber("smart_bias", input.smart_bias);
        writer.WriteNumber("retail_bias", input.retail_bias);
        writer.WriteNumber("dii_bias", input.dii_bias);
        writer.WriteNumber("smart_retail_divergence", input.smart_retail_divergence);
        writer.WriteNumber("smart_dii_divergence", input.smart_dii_divergence);
        writer.WriteString("smart_retail_state", input.smart_retail_state);
        writer.WriteNumber("participant_concentration", input.participant_concentration);
        writer.WriteString("participant_alignment", input.participant_alignment);
        writer.WriteString("indicator_alignment", input.indicator_alignment);
        writer.WriteString("dii_smart_relationship", input.dii_smart_relationship);
        writer.WriteString("deterministic_explanation", input.deterministic_explanation);
        writer.WriteEndObject();
    }

    private static void WriteContributions(
        Utf8JsonWriter writer,
        string propertyName,
        IReadOnlyList<MarketInterpretationContribution> contributions)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();
        foreach (var contribution in contributions)
        {
            writer.WriteStartObject();
            writer.WriteString("name", contribution.name);
            writer.WriteNumber("contribution", contribution.contribution);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is null)
            writer.WriteNull(propertyName);
        else
            writer.WriteString(propertyName, value);
    }

    private static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
