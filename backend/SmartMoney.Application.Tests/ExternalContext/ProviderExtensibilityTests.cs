using SmartMoney.ExternalContext.Configuration;
using SmartMoney.ExternalContext.Contracts;
using SmartMoney.ExternalContext.Export;
using SmartMoney.ExternalContext.Pipeline;
using SmartMoney.ExternalContext.Providers;
using Xunit;

namespace SmartMoney.Application.Tests.ExternalContext;

public sealed class ProviderExtensibilityTests
{
    [Fact]
    public async Task Pipeline_WorksWithOneProvider()
    {
        var document = await RunAsync([new TestProvider("ProviderA")]);

        Assert.Equal(["ProviderA"], document.Items.Select(item => item.Source));
    }

    [Fact]
    public async Task Pipeline_WorksWithThreeProviders()
    {
        var document = await RunAsync([
            new TestProvider("ProviderA"),
            new TestProvider("ProviderB"),
            new TestProvider("ProviderC")
        ]);

        Assert.Equal(["ProviderA", "ProviderB", "ProviderC"], document.Items.Select(item => item.Source).OrderBy(value => value));
    }

    [Fact]
    public async Task RemovingProvider_RequiresNoPipelineChange()
    {
        var document = await RunAsync([
            new TestProvider("ProviderA"),
            new TestProvider("ProviderC")
        ]);

        Assert.Equal(["ProviderA", "ProviderC"], document.Items.Select(item => item.Source).OrderBy(value => value));
    }

    [Fact]
    public async Task FailingProvider_DoesNotBlockOtherProviders()
    {
        var document = await RunAsync([
            new TestProvider("ProviderA"),
            new FailingProvider("ProviderB"),
            new TestProvider("ProviderC")
        ]);

        Assert.Equal(["ProviderA", "ProviderC"], document.Items.Select(item => item.Source).OrderBy(value => value));
    }

    [Fact]
    public async Task DisabledProvider_IsSkipped()
    {
        var disabled = new TestProvider("ProviderB") { Enabled = false };
        var document = await RunAsync([
            new TestProvider("ProviderA"),
            disabled,
            new TestProvider("ProviderC")
        ]);

        Assert.Equal(["ProviderA", "ProviderC"], document.Items.Select(item => item.Source).OrderBy(value => value));
        Assert.False(disabled.Called);
    }

    [Fact]
    public async Task ProviderOrder_DoesNotAffectCollectedSources()
    {
        var first = await RunAsync([
            new TestProvider("ProviderA"),
            new TestProvider("ProviderB"),
            new TestProvider("ProviderC")
        ]);
        var second = await RunAsync([
            new TestProvider("ProviderC"),
            new TestProvider("ProviderA"),
            new TestProvider("ProviderB")
        ]);

        Assert.Equal(
            first.Items.Select(item => item.Source).OrderBy(value => value),
            second.Items.Select(item => item.Source).OrderBy(value => value));
    }

    [Fact]
    public void PipelineConstructor_ExposesOnlyProviderInterface()
    {
        var constructor = typeof(MarketNewsPipeline).GetConstructors().Single();
        var providersParameter = constructor.GetParameters().Single(parameter => parameter.Name == "providers");

        Assert.Equal(typeof(IEnumerable<INewsSourceProvider>), providersParameter.ParameterType);
    }

    private static async Task<MarketNewsDocument> RunAsync(IEnumerable<INewsSourceProvider> providers)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"smartmoney-provider-test-{Guid.NewGuid():N}.json");
        try
        {
            var pipeline = new MarketNewsPipeline(
                providers,
                new DefaultNewsNormalizer(),
                new DefaultNewsDeduplicator(),
                new SimpleNewsRanker(),
                new JsonMarketNewsExporter(outputPath));

            return await pipeline.RunAsync(new ExternalContextOptions { MaxOutputItems = 10 }, CancellationToken.None);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    private sealed class TestProvider : INewsSourceProvider
    {
        public TestProvider(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public bool Enabled { get; set; } = true;
        public bool Called { get; private set; }

        public Task<IReadOnlyList<NewsCandidate>> GetNewsAsync(NewsSourceRequest request, CancellationToken cancellationToken)
        {
            Called = true;
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

    private sealed class FailingProvider : INewsSourceProvider
    {
        public FailingProvider(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public Task<IReadOnlyList<NewsCandidate>> GetNewsAsync(NewsSourceRequest request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Synthetic provider failure.");
        }
    }
}
