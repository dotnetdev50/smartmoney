namespace SmartMoney.ExternalContext.Configuration;

public sealed class ExternalContextOptions
{
    public bool Enabled { get; set; } = false;
    public int LookbackHours { get; set; } = 24;
    public int MaxCandidates { get; set; } = 100;
    public int MaxOutputItems { get; set; } = 5;
    public int ProviderTimeoutSeconds { get; set; } = 30;
    public string? OutputPath { get; set; }

    public void Validate()
    {
        if (LookbackHours <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(LookbackHours));
        }

        if (MaxCandidates <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCandidates));
        }

        if (MaxOutputItems <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxOutputItems));
        }

        if (ProviderTimeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ProviderTimeoutSeconds));
        }
    }
}
