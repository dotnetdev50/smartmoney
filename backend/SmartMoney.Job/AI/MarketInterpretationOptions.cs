namespace SmartMoney.Job.AI;

public sealed class MarketInterpretationOptions
{
    public bool Enabled { get; set; } = false;
    public string Provider { get; set; } = "disabled";
    public string Model { get; set; } = "none";
    public string? Endpoint { get; set; }
    public string? ApiCredentialEnvironmentVariable { get; set; }
    public int TimeoutSeconds { get; set; } = 15;
    public string PromptVersion { get; set; } = "market-daily-v1";
    public Dictionary<string, string> GenerationSettings { get; set; } = new(StringComparer.Ordinal);
}
