namespace SmartMoney.Job.AI;

public interface IMarketInterpretationProvider
{
    Task<MarketInterpretationResult?> GenerateAsync(
        MarketInterpretationInput input,
        MarketInterpretationPrompt prompt,
        CancellationToken ct);
}

public interface IMarketInterpretationService
{
    Task<MarketInterpretationAttempt?> InterpretAsync(
        MarketInterpretationInput input,
        MarketInterpretationAttempt? previous,
        CancellationToken ct);
}

public sealed class DisabledMarketInterpretationProvider : IMarketInterpretationProvider
{
    public Task<MarketInterpretationResult?> GenerateAsync(
        MarketInterpretationInput input,
        MarketInterpretationPrompt prompt,
        CancellationToken ct) => throw new MarketInterpretationProviderUnavailableException();
}

public sealed class MarketInterpretationProviderUnavailableException : Exception
{
    public MarketInterpretationProviderUnavailableException()
        : base("No market interpretation provider is configured.")
    {
    }
}

public sealed class MarketInterpretationService : IMarketInterpretationService
{
    private readonly IMarketInterpretationProvider provider;
    private readonly MarketInterpretationValidator validator;
    private readonly MarketInterpretationOptions options;
    private readonly MarketInterpretationPrompt prompt;
    private readonly TimeProvider timeProvider;

    public MarketInterpretationService(
        IMarketInterpretationProvider provider,
        MarketInterpretationValidator validator,
        MarketInterpretationOptions options,
        MarketInterpretationPrompt prompt,
        TimeProvider timeProvider)
    {
        this.provider = provider;
        this.validator = validator;
        this.options = options;
        this.prompt = prompt;
        this.timeProvider = timeProvider;
    }

    public async Task<MarketInterpretationAttempt?> InterpretAsync(
        MarketInterpretationInput input,
        MarketInterpretationAttempt? previous,
        CancellationToken ct)
    {
        if (!options.Enabled)
            return null;

        var fingerprint = MarketInterpretationFingerprint.Compute(
            input,
            new MarketInterpretationFingerprintContext(
                prompt.version,
                prompt.content,
                options.Provider,
                options.Model,
                options.GenerationSettings));

        if (string.IsNullOrWhiteSpace(prompt.content))
            return Unavailable(fingerprint, "prompt_unavailable");

        if (CanReuse(previous, fingerprint, input))
        {
            return previous! with
            {
                status = MarketInterpretationStatus.Reused,
                prompt_version = prompt.version,
                input_fingerprint = fingerprint,
                failure_category = null
            };
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds)));
            var result = await provider.GenerateAsync(input, prompt, timeout.Token);
            var validation = validator.Validate(result, input);
            if (!validation.is_valid)
            {
                return new MarketInterpretationAttempt(
                    MarketInterpretationStatus.Invalid,
                    prompt.version,
                    fingerprint,
                    failure_category: string.Join(",", validation.errors));
            }

            return new MarketInterpretationAttempt(
                MarketInterpretationStatus.Generated,
                prompt.version,
                fingerprint,
                timeProvider.GetUtcNow(),
                result);
        }
        catch (OperationCanceledException)
        {
            return Unavailable(fingerprint, ct.IsCancellationRequested ? "cancelled" : "timeout");
        }
        catch (MarketInterpretationProviderUnavailableException)
        {
            return Unavailable(fingerprint, "provider_unavailable");
        }
        catch (Exception)
        {
            return Unavailable(fingerprint, "provider_failure");
        }
    }

    private bool CanReuse(
        MarketInterpretationAttempt? previous,
        string fingerprint,
        MarketInterpretationInput input)
    {
        if (previous is null
            || previous.status is not (MarketInterpretationStatus.Generated or MarketInterpretationStatus.Reused)
            || !string.Equals(previous.input_fingerprint, fingerprint, StringComparison.Ordinal)
            || !string.Equals(previous.prompt_version, prompt.version, StringComparison.Ordinal)
            || previous.interpretation is null)
        {
            return false;
        }

        return validator.Validate(previous.interpretation, input).is_valid;
    }

    private MarketInterpretationAttempt Unavailable(string fingerprint, string category) => new(
        MarketInterpretationStatus.Unavailable,
        prompt.version,
        fingerprint,
        failure_category: category);
}
