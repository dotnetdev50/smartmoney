using Microsoft.Extensions.Options;
using SmartMoney.ExternalContext.Contracts;
using System.Xml.Linq;

namespace SmartMoney.ExternalContext.Providers;

public sealed class NseNewsSourceOptions
{
    public bool Enabled { get; set; } = true;
    public string Endpoint { get; set; } = "https://feeds.feedburner.com/nseindia/circulars";
    public int TimeoutSeconds { get; set; } = 30;

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);
}

public sealed class NseNewsSourceProvider : INewsSourceProvider
{
    private readonly HttpClient _httpClient;
    private readonly NseNewsSourceOptions _options;

    public NseNewsSourceProvider(HttpClient httpClient, IOptions<NseNewsSourceOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public string Name => "NSE";
    public bool Enabled => _options.Enabled;

    public async Task<IReadOnlyList<NewsCandidate>> GetNewsAsync(NewsSourceRequest request, CancellationToken cancellationToken)
    {
        return (await GetNewsResultAsync(request, cancellationToken)).Candidates;
    }

    public async Task<NewsProviderResult> GetNewsResultAsync(NewsSourceRequest request, CancellationToken cancellationToken)
    {
        var retrievedAtUtc = DateTimeOffset.UtcNow;
        string xml;
        try
        {
            xml = await NewsProviderUtilities.GetResponseTextAsync(_httpClient, _options.Endpoint, _options.Timeout, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return CreateResult(NewsProviderRunStatus.Failed, retrievedAtUtc, "HTTP_REQUEST_FAILED");
        }

        if (string.IsNullOrWhiteSpace(xml))
        {
            return CreateResult(NewsProviderRunStatus.Degraded, retrievedAtUtc, "EMPTY_FEED_RESPONSE");
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.None);
        }
        catch (System.Xml.XmlException)
        {
            return CreateResult(NewsProviderRunStatus.Degraded, retrievedAtUtc, "INVALID_FEED_XML");
        }

        if (document.Root?.Name.LocalName is not ("rss" or "feed"))
        {
            return CreateResult(NewsProviderRunStatus.Degraded, retrievedAtUtc, "NON_FEED_RESPONSE");
        }

        var feedItems = document
            .Descendants()
            .Where(element => element.Name.LocalName is "item" or "entry")
            .ToList();
        var candidates = feedItems
            .Select(item => MapItem(item, request))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .OrderByDescending(candidate => candidate.PublishedAtUtc)
            .Take(25)
            .ToList();

        return new NewsProviderResult
        {
            ProviderName = Name,
            Status = NewsProviderRunStatus.Success,
            Candidates = candidates,
            RetrievedAtUtc = retrievedAtUtc,
            FetchedItemCount = feedItems.Count
        };
    }

    private NewsProviderResult CreateResult(NewsProviderRunStatus status, DateTimeOffset retrievedAtUtc, string diagnosticCode) => new()
    {
        ProviderName = Name,
        Status = status,
        RetrievedAtUtc = retrievedAtUtc,
        DiagnosticCode = diagnosticCode
    };

    public static NewsCandidate? MapItem(XElement item, NewsSourceRequest request)
    {
        var title = NewsProviderUtilities.GetElementValue(item, "title");
        var link = GetCanonicalLink(item);
        var published = NewsProviderUtilities.GetElementValue(item, "pubDate")
            ?? NewsProviderUtilities.GetElementValue(item, "published")
            ?? NewsProviderUtilities.GetElementValue(item, "updated");
        var guid = NewsProviderUtilities.GetElementValue(item, "guid")
            ?? NewsProviderUtilities.GetElementValue(item, "id");
        var summary = NewsProviderUtilities.GetElementValue(item, "description")
            ?? NewsProviderUtilities.GetElementValue(item, "summary");

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link)
            || !NewsProviderUtilities.TryParseFeedDate(published, out var publishedAtUtc)
            || publishedAtUtc < request.FromUtc || publishedAtUtc > request.ToUtc)
        {
            return null;
        }

        if (!IsMarketWideAnnouncement(title, summary))
        {
            return null;
        }

        var idSource = !string.IsNullOrWhiteSpace(guid) ? guid : link;

        return new NewsCandidate
        {
            Id = NewsProviderUtilities.BuildStableId("nse", idSource, publishedAtUtc),
            Scope = NewsScope.India,
            Category = MapCategory(title, summary),
            Headline = title,
            Summary = string.IsNullOrWhiteSpace(summary) ? null : summary,
            SourceName = "NSE",
            SourceType = NewsSourceType.Official,
            ArticleUrl = new Uri(link),
            PublishedAtUtc = publishedAtUtc,
            RetrievedAtUtc = DateTimeOffset.UtcNow,
            Country = "India",
            Tags = ["nse", "exchange", "circular", "india"],
            ExternalId = guid
        };
    }

    private static string? GetCanonicalLink(XElement item)
    {
        var atomLink = item.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("link", StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace((string?)element.Attribute("rel"))
                || string.Equals((string?)element.Attribute("rel"), "alternate", StringComparison.OrdinalIgnoreCase)))
            ?.Attribute("href")?.Value;
        return atomLink ?? NewsProviderUtilities.GetElementValue(item, "link");
    }

    private static bool IsMarketWideAnnouncement(string title, string? summary)
    {
        var text = $"{title} {summary}".ToLowerInvariant();

        if (text.Contains("mf invest") || text.Contains("suspension of trading in units") || text.Contains("on account of redemption"))
        {
            return false;
        }

        string[] keyTerms = [
            "surveillance", "asm", "gsm", "margin", "derivative", "futures", "options", "settlement",
            "trading hours", "holiday", "circuit", "position limit", "mwpl", "clearing", "capital adequacy",
            "risk management", "sebi order"
        ];

        return keyTerms.Any(text.Contains);
    }

    private static NewsCategory MapCategory(string title, string? summary)
    {
        var text = $"{title} {summary}".ToLowerInvariant();

        if (text.Contains("clearing") || text.Contains("risk") || text.Contains("margin") || text.Contains("mwpl"))
        {
            return NewsCategory.FinancialSystem;
        }

        return NewsCategory.IndiaPolicyRegulation;
    }
}
