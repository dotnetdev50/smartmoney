using System.Xml.Linq;
using Microsoft.Extensions.Options;
using SmartMoney.ExternalContext.Contracts;

namespace SmartMoney.ExternalContext.Providers;

public sealed class SebiNewsSourceOptions
{
    public bool Enabled { get; set; } = true;
    public string Endpoint { get; set; } = "https://www.sebi.gov.in/sebirss.xml";
    public int TimeoutSeconds { get; set; } = 30;

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);
}

public sealed class SebiNewsSourceProvider : INewsSourceProvider
{
    private readonly HttpClient _httpClient;
    private readonly SebiNewsSourceOptions _options;

    public SebiNewsSourceProvider(HttpClient httpClient, IOptions<SebiNewsSourceOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public string Name => "SEBI";
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
        var published = NewsProviderUtilities.GetElementValue(item, "pubDate");

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link)
            || !NewsProviderUtilities.TryParseFeedDate(published, out var publishedAtUtc)
            || publishedAtUtc < request.FromUtc || publishedAtUtc > request.ToUtc)
        {
            return null;
        }

        if (IsRoutineEnforcementOrAppeal(title))
        {
            return null;
        }

        return new NewsCandidate
        {
            Id = NewsProviderUtilities.BuildStableId("sebi", title, publishedAtUtc),
            Scope = NewsScope.India,
            Category = MapCategory(title),
            Headline = title,
            Summary = string.IsNullOrWhiteSpace(summary) || string.Equals(summary, title, StringComparison.OrdinalIgnoreCase) ? null : summary,
            SourceName = "SEBI",
            SourceType = NewsSourceType.Official,
            ArticleUrl = new Uri(link),
            PublishedAtUtc = publishedAtUtc,
            RetrievedAtUtc = DateTimeOffset.UtcNow,
            Country = "India",
            Tags = ["sebi", "regulation", "india"]
        };
    }

    private static bool IsRoutineEnforcementOrAppeal(string title)
    {
        var t = title.ToLowerInvariant();
         return t.Contains("settlement order") ||
             t.Contains("adjudication order") ||
               t.Contains("appeal no") ||
               t.Contains("notice of demand") ||
               t.Contains("recovery proceedings") ||
               t.Contains("release order") ||
               t.Contains("recovery certificate") ||
               t.Contains("notice of attachment") ||
               t.Contains("in the matter of") ||
               t.Contains("illiquid stock options") ||
               t.Contains("grant of certificate") ||
               t.Contains("surrender of certificate") ||
               t.Contains("in the matter of illiquid");
    }

    private static NewsCategory MapCategory(string title)
    {
        var t = title.ToLowerInvariant();
        if (t.Contains("monetary") || t.Contains("macro") || t.Contains("repo"))
        {
            return NewsCategory.MonetaryMacro;
        }
        return NewsCategory.IndiaPolicyRegulation;
    }
}
