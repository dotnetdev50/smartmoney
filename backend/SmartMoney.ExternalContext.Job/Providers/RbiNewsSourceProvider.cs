using System.Xml.Linq;
using SmartMoney.ExternalContext.Contracts;
using Microsoft.Extensions.Options;

namespace SmartMoney.ExternalContext.Providers;

public sealed class RbiNewsSourceOptions
{
    public bool Enabled { get; set; } = true;
    public string Endpoint { get; set; } = "https://www.rbi.org.in/Scripts/rss.aspx";
    public int TimeoutSeconds { get; set; } = 30;

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);
}

public sealed class RbiNewsSourceProvider : INewsSourceProvider
{
    private readonly HttpClient _httpClient;
    private readonly RbiNewsSourceOptions _options;

    public RbiNewsSourceProvider(HttpClient httpClient, IOptions<RbiNewsSourceOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public string Name => "RBI";
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

    private static NewsCandidate? MapItem(XElement item, NewsSourceRequest request)
    {
        var title = NewsProviderUtilities.GetElementValue(item, "title");
        var link = NewsProviderUtilities.GetElementValue(item, "link");
        var summary = NewsProviderUtilities.GetElementValue(item, "description");
        var published = NewsProviderUtilities.GetElementValue(item, "pubDate");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link)
            || !NewsProviderUtilities.TryParseFeedDate(published, out var publishedAtUtc)
            || publishedAtUtc < request.FromUtc || publishedAtUtc > request.ToUtc)
        {
            return null;
        }

        return new NewsCandidate
        {
            Id = NewsProviderUtilities.BuildStableId("rbi", title, publishedAtUtc),
            Scope = NewsScope.India,
            Category = NewsCategory.MonetaryMacro,
            Headline = title,
            Summary = string.IsNullOrWhiteSpace(summary) ? null : summary,
            SourceName = "RBI",
            SourceType = NewsSourceType.Official,
            ArticleUrl = new Uri(link),
            PublishedAtUtc = publishedAtUtc,
            RetrievedAtUtc = DateTimeOffset.UtcNow,
            Country = "India",
            Tags = ["rbi", "policy", "india"]
        };
    }
}
