using System.Xml.Linq;
using Microsoft.Extensions.Options;
using SmartMoney.ExternalContext.Contracts;

namespace SmartMoney.ExternalContext.Providers;

public sealed class FederalReserveNewsSourceOptions
{
    public bool Enabled { get; set; } = true;
    public string Endpoint { get; set; } = "https://www.federalreserve.gov/feeds/press_monetary.xml";
    public int TimeoutSeconds { get; set; } = 30;

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);
}

public sealed class FederalReserveNewsSourceProvider : INewsSourceProvider
{
    private readonly HttpClient _httpClient;
    private readonly FederalReserveNewsSourceOptions _options;

    public FederalReserveNewsSourceProvider(HttpClient httpClient, IOptions<FederalReserveNewsSourceOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public string Name => "Federal Reserve";
    public bool Enabled => _options.Enabled;

    public async Task<IReadOnlyList<NewsCandidate>> GetNewsAsync(NewsSourceRequest request, CancellationToken cancellationToken)
    {
        var xml = await NewsProviderUtilities.GetResponseTextAsync(_httpClient, _options.Endpoint, _options.Timeout, cancellationToken);
        if (string.IsNullOrWhiteSpace(xml))
        {
            return Array.Empty<NewsCandidate>();
        }

        return XDocument.Parse(xml).Descendants("item")
            .Select(item => MapItem(item, request))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .OrderByDescending(candidate => candidate.PublishedAtUtc)
            .Take(25)
            .ToList();
    }

    public static NewsCandidate? MapItem(XElement item, NewsSourceRequest request)
    {
        var title = NewsProviderUtilities.GetElementValue(item, "title");
        var link = NewsProviderUtilities.GetElementValue(item, "link");
        var summary = NewsProviderUtilities.GetElementValue(item, "description");
        var category = NewsProviderUtilities.GetElementValue(item, "category");
        var published = NewsProviderUtilities.GetElementValue(item, "pubDate");
        var guid = NewsProviderUtilities.GetElementValue(item, "guid");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link)
            || !string.Equals(category, "Monetary Policy", StringComparison.OrdinalIgnoreCase)
            || !NewsProviderUtilities.TryParseFeedDate(published, out var publishedAtUtc)
            || publishedAtUtc < request.FromUtc || publishedAtUtc > request.ToUtc)
        {
            return null;
        }

        return new NewsCandidate
        {
            Id = NewsProviderUtilities.BuildStableId("fed", string.IsNullOrWhiteSpace(guid) ? link : guid, publishedAtUtc),
            Scope = NewsScope.Global,
            Category = NewsCategory.MonetaryMacro,
            Headline = title,
            Summary = string.IsNullOrWhiteSpace(summary) ? null : summary,
            SourceName = "Federal Reserve",
            SourceType = NewsSourceType.Official,
            ArticleUrl = new Uri(link),
            PublishedAtUtc = publishedAtUtc,
            RetrievedAtUtc = DateTimeOffset.UtcNow,
            Country = "United States",
            Tags = ["fed", "monetary-policy"],
            ExternalId = guid
        };
    }
}
