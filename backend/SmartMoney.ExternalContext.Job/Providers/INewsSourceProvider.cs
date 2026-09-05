using SmartMoney.ExternalContext.Contracts;

namespace SmartMoney.ExternalContext.Providers;

public interface INewsSourceProvider
{
    string Name => GetType().Name;
    bool Enabled => true;

    Task<IReadOnlyList<NewsCandidate>> GetNewsAsync(NewsSourceRequest request, CancellationToken cancellationToken);

    async Task<NewsProviderResult> GetNewsResultAsync(NewsSourceRequest request, CancellationToken cancellationToken)
    {
        var retrievedAtUtc = DateTimeOffset.UtcNow;
        if (!Enabled)
        {
            return new NewsProviderResult
            {
                ProviderName = Name,
                Status = NewsProviderRunStatus.Disabled,
                RetrievedAtUtc = retrievedAtUtc
            };
        }

        try
        {
            var candidates = await GetNewsAsync(request, cancellationToken);
            return new NewsProviderResult
            {
                ProviderName = Name,
                Status = NewsProviderRunStatus.Success,
                Candidates = candidates,
                RetrievedAtUtc = retrievedAtUtc
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new NewsProviderResult
            {
                ProviderName = Name,
                Status = NewsProviderRunStatus.Failed,
                RetrievedAtUtc = retrievedAtUtc,
                DiagnosticCode = "HTTP_REQUEST_FAILED"
            };
        }
        catch
        {
            return new NewsProviderResult
            {
                ProviderName = Name,
                Status = NewsProviderRunStatus.Failed,
                RetrievedAtUtc = retrievedAtUtc,
                DiagnosticCode = "PROVIDER_EXECUTION_FAILED"
            };
        }
    }
}
