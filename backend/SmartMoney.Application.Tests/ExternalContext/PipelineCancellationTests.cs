using SmartMoney.ExternalContext.Configuration;
using SmartMoney.ExternalContext.Contracts;
using SmartMoney.ExternalContext.Export;
using SmartMoney.ExternalContext.Pipeline;
using SmartMoney.ExternalContext.Providers;
using Xunit;

namespace SmartMoney.Application.Tests.ExternalContext;

public sealed class PipelineCancellationTests
{
    [Fact]
    public async Task ProviderLocalTimeout_IsIsolated_AndOtherProvidersContinue()
    {
        var document = await RunAsync(
            [
                new SucceedingProvider("ProviderA"),
                new ProviderLocalTimeoutProvider("ProviderB"),
                new SucceedingProvider("ProviderC")
            ],
            CancellationToken.None);

        Assert.Equal(["ProviderA", "ProviderC"], document.Items.Select(item => item.Source).OrderBy(value => value));
    }

    [Fact]
    public async Task CallerCancellation_IsPropagated_AndDoesNotReturnPartialResults()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var providers = new INewsSourceProvider[]
        {
            new SucceedingProvider("ProviderA"),
            new CancellationObservingProvider("ProviderB"),
            new SucceedingProvider("ProviderC")
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() => RunAsync(providers, cts.Token));
    }

    private static async Task<MarketNewsDocument> RunAsync(IEnumerable<INewsSourceProvider> providers, CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"smartmoney-cancellation-test-{Guid.NewGuid():N}.json");
        try
        {
            var pipeline = new MarketNewsPipeline(
                providers,
                new DefaultNewsNormalizer(),
                new DefaultNewsDeduplicator(),
                new SimpleNewsRanker(),
                new JsonMarketNewsExporter(outputPath));

            return await pipeline.RunAsync(new ExternalContextOptions { MaxOutputItems = 10 }, cancellationToken);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    private sealed class SucceedingProvider : INewsSourceProvider
    {
        public SucceedingProvider(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public Task<IReadOnlyList<NewsCandidate>> GetNewsAsync(NewsSourceRequest request, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult<IReadOnlyList<NewsCandidate>>([new NewsCandidate
            {
                Id = Name,
                Scope = NewsScope.Global,
                Category = NewsCategory.Other,
                Headline = Name,
                SourceName = Name,
                SourceType = NewsSourceType.Other,
                ArticleUrl = new Uri($"https://example.com/{Name}"),
                PublishedAtUtc = now,
                RetrievedAtUtc = now
            }]);
        }
    }

    private sealed class ProviderLocalTimeoutProvider : INewsSourceProvider
    {
        public ProviderLocalTimeoutProvider(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public Task<IReadOnlyList<NewsCandidate>> GetNewsAsync(NewsSourceRequest request, CancellationToken cancellationToken)
        {
            // Simulates a provider-local timeout that is unrelated to the caller's token.
            using var localTimeout = new CancellationTokenSource();
            localTimeout.Cancel();
            localTimeout.Token.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<NewsCandidate>>([]);
        }
    }

    private sealed class CancellationObservingProvider : INewsSourceProvider
    {
        public CancellationObservingProvider(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public Task<IReadOnlyList<NewsCandidate>> GetNewsAsync(NewsSourceRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<NewsCandidate>>([]);
        }
    }
}
