namespace SmartMoney.ExternalContext.Providers;

using SmartMoney.ExternalContext.Contracts;

public interface INewsSourceProvider
{
    Task<IReadOnlyList<NewsCandidate>> GetNewsAsync(NewsSourceRequest request, CancellationToken cancellationToken);
}

public sealed class FixtureNewsSourceProvider : INewsSourceProvider
{
    public const string FixtureSourceName = "fixture";

    private readonly string _sourceName;

    public FixtureNewsSourceProvider(string sourceName = FixtureSourceName)
    {
        _sourceName = sourceName;
    }

    public Task<IReadOnlyList<NewsCandidate>> GetNewsAsync(NewsSourceRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTimeOffset.UtcNow;
        var candidates = new List<NewsCandidate>
        {
            new()
            {
                Id = "fixture-global-geopolitical",
                Scope = NewsScope.Global,
                Category = NewsCategory.Geopolitical,
                Headline = "Global geopolitical tensions rise as shipping routes remain volatile",
                Summary = "A geopolitical flashpoint is increasing uncertainty around regional trade and commodity flows.",
                SourceName = _sourceName,
                SourceType = NewsSourceType.Publisher,
                ArticleUrl = new Uri("https://example.com/fixture/global-geopolitical"),
                PublishedAtUtc = now.AddMinutes(-10),
                RetrievedAtUtc = now,
                Country = "Global",
                Tags = ["geopolitics", "shipping", "commodities"]
            },
            new()
            {
                Id = "fixture-india-rbi-policy",
                Scope = NewsScope.India,
                Category = NewsCategory.MonetaryMacro,
                Headline = "India policy signal suggests tightening remains on the table",
                Summary = "Policy commentary highlights persistent inflation and rate-sensitive market attention.",
                SourceName = _sourceName,
                SourceType = NewsSourceType.Official,
                ArticleUrl = new Uri("https://example.com/fixture/india-rbi-policy"),
                PublishedAtUtc = now.AddMinutes(-20),
                RetrievedAtUtc = now,
                Country = "India",
                Tags = ["rbi", "rates", "policy"]
            },
            new()
            {
                Id = "fixture-oil-supply-shock",
                Scope = NewsScope.Global,
                Category = NewsCategory.OilEnergy,
                Headline = "Oil and energy markets react to supply disruption warnings",
                Summary = "Energy disruptions have an outsized impact on inflation expectations and growth sentiment.",
                SourceName = _sourceName,
                SourceType = NewsSourceType.Aggregator,
                ArticleUrl = new Uri("https://example.com/fixture/oil-supply-shock"),
                PublishedAtUtc = now.AddMinutes(-40),
                RetrievedAtUtc = now,
                Country = "Global",
                Tags = ["oil", "energy", "inflation"]
            },
            new()
            {
                Id = "fixture-india-natural-disaster",
                Scope = NewsScope.India,
                Category = NewsCategory.NaturalDisaster,
                Headline = "Heavy rainfall disrupts logistics and power supply across regions",
                Summary = "Infrastructure disruption is expected to affect local activity and sentiment in vulnerable sectors.",
                SourceName = _sourceName,
                SourceType = NewsSourceType.Publisher,
                ArticleUrl = new Uri("https://example.com/fixture/india-natural-disaster"),
                PublishedAtUtc = now.AddMinutes(-70),
                RetrievedAtUtc = now,
                Country = "India",
                Tags = ["weather", "disaster", "logistics"]
            },
            new()
            {
                Id = "fixture-minor-event",
                Scope = NewsScope.India,
                Category = NewsCategory.Other,
                Headline = "Corporate press release extends a local operational update",
                Summary = "A minor company update is unlikely to shift broader market structure.",
                SourceName = _sourceName,
                SourceType = NewsSourceType.Other,
                ArticleUrl = new Uri("https://example.com/fixture/minor-event"),
                PublishedAtUtc = now.AddMinutes(-140),
                RetrievedAtUtc = now,
                Country = "India",
                Tags = ["company", "minor"]
            }
        };

        return Task.FromResult<IReadOnlyList<NewsCandidate>>(candidates);
    }
}
