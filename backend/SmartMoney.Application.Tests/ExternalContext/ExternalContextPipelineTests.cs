using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using SmartMoney.ExternalContext.Configuration;
using SmartMoney.ExternalContext.Contracts;
using SmartMoney.ExternalContext.Export;
using SmartMoney.ExternalContext.Pipeline;
using SmartMoney.ExternalContext.Providers;
using SmartMoney.ExternalContext.Ranking;
using Xunit;

namespace SmartMoney.Application.Tests.ExternalContext;

public sealed class ExternalContextPipelineTests
{
    [Fact]
    public async Task FixtureProvider_ReturnsDeterministicCandidates()
    {
        var provider = new FixtureNewsSourceProvider();

        var candidates = await provider.GetNewsAsync(new NewsSourceRequest { FromUtc = DateTimeOffset.UtcNow.AddHours(-24), ToUtc = DateTimeOffset.UtcNow }, CancellationToken.None);

        Assert.NotEmpty(candidates);
        Assert.Equal(5, candidates.Count);
        Assert.All(candidates, c => Assert.False(string.IsNullOrWhiteSpace(c.Headline)));
        Assert.Equal(FixtureNewsSourceProvider.FixtureSourceName, candidates.First().SourceName.ToLowerInvariant());
    }

    [Fact]
    public async Task Pipeline_AggregatesCandidates_FromMultipleProviders()
    {
        var providers = new INewsSourceProvider[]
        {
            new FixtureNewsSourceProvider(),
            new FixtureNewsSourceProvider("Secondary fixture")
        };

        var pipeline = new MarketNewsPipeline(
            providers,
            new DefaultNewsNormalizer(),
            new DefaultNewsDeduplicator(),
            new SimpleNewsRanker(),
            new JsonMarketNewsExporter());

        var doc = await pipeline.RunAsync(new ExternalContextOptions(), CancellationToken.None);

        Assert.NotNull(doc);
        Assert.NotEmpty(doc.Items);
        Assert.True(doc.Items.Count <= 5);
    }

    [Fact]
    public async Task ExactDuplicateIdsAndUrls_AreRemoved()
    {
        var provider = new DuplicateCandidateProvider();
        var pipeline = new MarketNewsPipeline([provider], new DefaultNewsNormalizer(), new DefaultNewsDeduplicator(), new SimpleNewsRanker(), new JsonMarketNewsExporter());

        var doc = await pipeline.RunAsync(new ExternalContextOptions { MaxOutputItems = 10 }, CancellationToken.None);

        Assert.Equal(2, doc.Items.Count);
    }

    [Fact]
    public async Task MaxOutputItems_IsRespected()
    {
        var pipeline = new MarketNewsPipeline(
            [new FixtureNewsSourceProvider()],
            new DefaultNewsNormalizer(),
            new DefaultNewsDeduplicator(),
            new SimpleNewsRanker(),
            new JsonMarketNewsExporter());

        var doc = await pipeline.RunAsync(new ExternalContextOptions { MaxOutputItems = 2 }, CancellationToken.None);

        Assert.Equal(2, doc.Items.Count);
    }

    [Fact]
    public async Task OutputRanks_StartAtOne_AndAreSequential()
    {
        var pipeline = new MarketNewsPipeline(
            [new FixtureNewsSourceProvider()],
            new DefaultNewsNormalizer(),
            new DefaultNewsDeduplicator(),
            new SimpleNewsRanker(),
            new JsonMarketNewsExporter());

        var doc = await pipeline.RunAsync(new ExternalContextOptions { MaxOutputItems = 5 }, CancellationToken.None);

        Assert.Equal(1, doc.Items.First().Rank);
        Assert.Equal(Enumerable.Range(1, doc.Items.Count), doc.Items.Select(x => x.Rank));
    }

    [Fact]
    public void JsonSerialization_ProducesExpectedPublicContract()
    {
        var document = new MarketNewsDocument
        {
            GeneratedAtUtc = new DateTimeOffset(2026, 9, 3, 15, 30, 0, TimeSpan.Zero),
            LookbackHours = 24,
            Items =
            [
                new MarketNewsItem
                {
                    Rank = 1,
                    Scope = NewsScope.Global,
                    Category = NewsCategory.Geopolitical,
                    Impact = NewsImpact.High,
                    Sentiment = NewsSentiment.Negative,
                    Headline = "Market-moving event",
                    WhyItMatters = "Potential impact on oil prices and Indian market sentiment.",
                    Source = "Example",
                    PublishedAtUtc = new DateTimeOffset(2026, 9, 3, 14, 0, 0, TimeSpan.Zero),
                    Url = "https://example.com/article"
                }
            ]
        };

        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        });

        Assert.Contains("\"scope\":\"Global\"", json);
        Assert.Contains("\"category\":\"Geopolitical\"", json);
        Assert.Contains("\"impact\":\"High\"", json);
        Assert.Contains("\"sentiment\":\"Negative\"", json);
        Assert.Contains("\"generated_at_utc\"", json);
        Assert.Contains("\"lookback_hours\"", json);
        Assert.Contains("\"why_it_matters\"", json);
        Assert.Contains("\"published_at_utc\"", json);
        Assert.Contains("\"headline\"", json);

        var roundTrip = JsonSerializer.Deserialize<MarketNewsDocument>(json, new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        });

        Assert.NotNull(roundTrip);
        Assert.Equal(NewsScope.Global, roundTrip.Items[0].Scope);
        Assert.Equal(NewsCategory.Geopolitical, roundTrip.Items[0].Category);
        Assert.Equal(NewsImpact.High, roundTrip.Items[0].Impact);
        Assert.Equal(NewsSentiment.Negative, roundTrip.Items[0].Sentiment);
    }

    [Fact]
    public async Task AtomicExporter_WritesValidDocument()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"smartmoney-ec-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var outputPath = Path.Combine(tempDir, "market_news.json");

        try
        {
            var exporter = new JsonMarketNewsExporter(outputPath);
            var document = new MarketNewsDocument
            {
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                LookbackHours = 24,
                Items = []
            };

            await exporter.ExportAsync(document, CancellationToken.None);

            Assert.True(File.Exists(outputPath));
            var content = await File.ReadAllTextAsync(outputPath);
            Assert.Contains("\"items\"", content);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ExternalContextOptions_Defaults_AreCorrect()
    {
        var options = new ExternalContextOptions();

        Assert.False(options.Enabled);
        Assert.Equal(24, options.LookbackHours);
        Assert.Equal(100, options.MaxCandidates);
        Assert.Equal(5, options.MaxOutputItems);
        Assert.True(options.ProviderTimeoutSeconds > 0);

        var invalid = new ExternalContextOptions { MaxOutputItems = 0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => invalid.Validate());
    }

    [Fact]
    public async Task CancellationToken_IsPropagated_ThroughProviderCalls()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var provider = new CancellableProvider();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.GetNewsAsync(new NewsSourceRequest { FromUtc = DateTimeOffset.UtcNow.AddHours(-1), ToUtc = DateTimeOffset.UtcNow }, cts.Token));
    }

        [Fact]
        public async Task RbiProvider_ParsesOfficialPressReleases()
        {
                const string xml = """
                        <rss version="2.0">
                            <channel>
                                <item>
                                    <title>RBI announces policy rate decisions</title>
                                    <link>https://www.rbi.org.in/Scripts/BS_PressReleaseDisplay.aspx?prid=5723</link>
                                    <description>RBI announces policy rate decisions to support growth and price stability.</description>
                                    <pubDate>Thu, 29 Aug 2026 12:30:00 GMT</pubDate>
                                </item>
                            </channel>
                        </rss>
                        """;

                var provider = new RbiNewsSourceProvider(
                    new HttpClient(new StubHttpMessageHandler(xml)) { BaseAddress = new Uri("https://www.rbi.org.in/") },
                    Microsoft.Extensions.Options.Options.Create(new RbiNewsSourceOptions { Endpoint = "https://www.rbi.org.in/" }));

                var candidates = await provider.GetNewsAsync(new NewsSourceRequest
                {
                        FromUtc = new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero),
                        ToUtc = new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero)
                }, CancellationToken.None);

                var item = Assert.Single(candidates);
                Assert.Equal("RBI", item.SourceName);
                Assert.Equal(NewsScope.India, item.Scope);
                Assert.Equal(NewsCategory.MonetaryMacro, item.Category);
                Assert.Equal(NewsSourceType.Official, item.SourceType);
                Assert.Contains("policy", item.Headline, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task FederalReserveProvider_ParsesOfficialPressReleases()
        {
                const string xml = """
                        <rss version="2.0">
                            <channel>
                                <item>
                                    <title>Federal Reserve issues FOMC statement</title>
                                    <link>https://www.federalreserve.gov/newsevents/pressreleases/monetary20260729a.htm</link>
                                    <description>Federal Reserve issues FOMC statement after policy meeting.</description>
                                    <category>Monetary Policy</category>
                                    <pubDate>Wed, 29 Jul 2026 18:00:00 GMT</pubDate>
                                </item>
                            </channel>
                        </rss>
                        """;

                var provider = new FederalReserveNewsSourceProvider(
                    new HttpClient(new StubHttpMessageHandler(xml)) { BaseAddress = new Uri("https://www.federalreserve.gov/") },
                    Microsoft.Extensions.Options.Options.Create(new FederalReserveNewsSourceOptions { Endpoint = "https://www.federalreserve.gov/" }));

                var candidates = await provider.GetNewsAsync(new NewsSourceRequest
                {
                        FromUtc = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                        ToUtc = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)
                }, CancellationToken.None);

                var item = Assert.Single(candidates);
                Assert.Equal("Federal Reserve", item.SourceName);
                Assert.Equal(NewsScope.Global, item.Scope);
                Assert.Equal(NewsCategory.MonetaryMacro, item.Category);
                Assert.Equal(NewsSourceType.Official, item.SourceType);
                Assert.Contains("FOMC", item.Headline, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GdacsProvider_FiltersToHighSeverityEvents()
        {
                const string json = """
                        {
                            "events": [
                                {
                                    "eventid": 12345,
                                    "title": "Severe storm threat",
                                    "description": "Heavy storms and flooding expected across the region.",
                                    "alertlevel": "Orange",
                                    "eventtype": "TC",
                                    "fromDate": "2026-09-02T06:00:00Z",
                                    "url": "https://www.gdacs.org/report.aspx?eventid=12345&episodeid=12345&eventtype=TC"
                                },
                                {
                                    "eventid": 54321,
                                    "title": "Low severity advisory",
                                    "description": "Minor pressure system with limited disruption.",
                                    "alertlevel": "Green",
                                    "eventtype": "TC",
                                    "fromDate": "2026-09-02T08:00:00Z",
                                    "url": "https://www.gdacs.org/report.aspx?eventid=54321"
                                }
                            ]
                        }
                        """;

                var provider = new GdacsNewsSourceProvider(
                    new HttpClient(new StubHttpMessageHandler(json)) { BaseAddress = new Uri("https://www.gdacs.org/") },
                    Microsoft.Extensions.Options.Options.Create(new GdacsNewsSourceOptions { Endpoint = "https://www.gdacs.org/gdacsapi/api/events" }));

                var candidates = await provider.GetNewsAsync(new NewsSourceRequest
                {
                        FromUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                        ToUtc = new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero)
                }, CancellationToken.None);

                var item = Assert.Single(candidates);
                Assert.Equal("GDACS", item.SourceName);
                Assert.Equal(NewsScope.Global, item.Scope);
                Assert.Equal(NewsCategory.NaturalDisaster, item.Category);
                Assert.Equal(NewsSourceType.Official, item.SourceType);
                Assert.Equal("Orange", item.Tags?.FirstOrDefault());
        }

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
                private readonly string _responseBody;

                public StubHttpMessageHandler(string responseBody)
                {
                        _responseBody = responseBody;
                }

                protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                {
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                        {
                                Content = new StringContent(_responseBody, System.Text.Encoding.UTF8, "application/json")
                        });
                }
        }

    private sealed class DuplicateCandidateProvider : INewsSourceProvider
    {
        public Task<IReadOnlyList<NewsCandidate>> GetNewsAsync(NewsSourceRequest request, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            var a = new NewsCandidate
            {
                Id = "id-1",
                Scope = NewsScope.Global,
                Category = NewsCategory.Geopolitical,
                Headline = "Global event",
                SourceName = "Test",
                SourceType = NewsSourceType.Publisher,
                ArticleUrl = new Uri("https://example.com/one"),
                PublishedAtUtc = now,
                RetrievedAtUtc = now
            };
            var b = new NewsCandidate
            {
                Id = "id-1",
                Scope = NewsScope.Global,
                Category = NewsCategory.Geopolitical,
                Headline = "Global event",
                SourceName = "Test",
                SourceType = NewsSourceType.Publisher,
                ArticleUrl = new Uri("https://example.com/one"),
                PublishedAtUtc = now,
                RetrievedAtUtc = now
            };
            var c = new NewsCandidate
            {
                Id = "id-2",
                Scope = NewsScope.India,
                Category = NewsCategory.IndiaPolicyRegulation,
                Headline = "India event",
                SourceName = "Test",
                SourceType = NewsSourceType.Official,
                ArticleUrl = new Uri("https://example.com/two"),
                PublishedAtUtc = now,
                RetrievedAtUtc = now
            };

            return Task.FromResult<IReadOnlyList<NewsCandidate>>([a, b, c]);
        }
    }

    private sealed class CancellableProvider : INewsSourceProvider
    {
        public Task<IReadOnlyList<NewsCandidate>> GetNewsAsync(NewsSourceRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<NewsCandidate>>([]);
        }
    }
}
