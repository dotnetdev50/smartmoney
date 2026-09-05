using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using SmartMoney.ExternalContext.Contracts;

namespace SmartMoney.ExternalContext.Providers;

public sealed class PibNewsSourceOptions
{
    public bool Enabled { get; set; } = true;
    public string Endpoint { get; set; } = "https://pib.gov.in/RssMain.aspx?ModId=6&reg=3&lang=1";
    public int TimeoutSeconds { get; set; } = 30;

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);
}

public sealed class PibNewsSourceProvider : INewsSourceProvider
{
    private readonly HttpClient _httpClient;
    private readonly PibNewsSourceOptions _options;

    public PibNewsSourceProvider(HttpClient httpClient, IOptions<PibNewsSourceOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public string Name => "PIB";
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
        var published = NewsProviderUtilities.GetElementValue(item, "pubDate")
            ?? (item.Parent is not null ? NewsProviderUtilities.GetElementValue(item.Parent, "pubDate") : null)
            ?? (item.Parent is not null ? NewsProviderUtilities.GetElementValue(item.Parent, "lastBuildDate") : null);

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(published) || !NewsProviderUtilities.TryParseFeedDate(published, out var publishedAtUtc))
        {
            return null;
        }

        if (publishedAtUtc < request.FromUtc || publishedAtUtc > request.ToUtc)
        {
            return null;
        }

        if (!IsMarketRelevant(title, summary))
        {
            return null;
        }

        var canonicalUrl = NormalizePibUrl(link);
        var prid = ExtractPrid(link) ?? title;

        return new NewsCandidate
        {
            Id = NewsProviderUtilities.BuildStableId("pib", prid, publishedAtUtc),
            Scope = NewsScope.India,
            Category = MapCategory(title, summary),
            Headline = title,
            Summary = string.IsNullOrWhiteSpace(summary) || string.Equals(summary, title, StringComparison.OrdinalIgnoreCase) ? null : summary,
            SourceName = "PIB",
            SourceType = NewsSourceType.Official,
            ArticleUrl = new Uri(canonicalUrl),
            PublishedAtUtc = publishedAtUtc,
            RetrievedAtUtc = DateTimeOffset.UtcNow,
            Country = "India",
            Tags = ["pib", "government", "policy", "india"]
        };
    }

    private static bool IsMarketRelevant(string title, string? summary)
    {
        var text = (title + " " + (summary ?? string.Empty)).ToLowerInvariant();

        string[] exclusions = ["corporate mitra", "ccras", "festival", "greetings", "sports", "inaugurates", "wishes", "sangeet", "sahitya", "awards", "olympiad", "tribute"];
        if (exclusions.Any(e => text.Contains(e)))
        {
            return false;
        }

        string[] inclusions = [
            "ministry of finance", "department of economic affairs", "department of revenue", "department of financial services",
            "ministry of commerce", "cabinet approves", "petroleum", "energy", "gst", "tax", "taxation", "fiscal",
            "budget", "tariff", "trade policy", "disinvestment", "privatisation", "banking", "financial sector",
            "capital market", "inflation", "gdp", "infrastructure investment", "infrastructure project", "crude", "oil pricing",
            "economic reform", "free trade agreement", "customs duty"
        ];

        return inclusions.Any(i => text.Contains(i));
    }

    private static NewsCategory MapCategory(string title, string? summary)
    {
        var text = (title + " " + (summary ?? string.Empty)).ToLowerInvariant();
        if (text.Contains("monetary") || text.Contains("macro") || text.Contains("inflation") ||
            text.Contains("gdp") || text.Contains("fiscal") || text.Contains("repo"))
        {
            return NewsCategory.MonetaryMacro;
        }

        return NewsCategory.IndiaPolicyRegulation;
    }

    private static string NormalizePibUrl(string rawUrl)
    {
        return rawUrl.Replace("PressReleaseIframePage.aspx", "PressReleasePage.aspx", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractPrid(string link)
    {
        var match = Regex.Match(link, @"PRID=(\d+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }
}
