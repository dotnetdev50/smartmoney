using System.Text.RegularExpressions;
using System.Xml.Linq;
using SmartMoney.ExternalContext.Contracts;
using Microsoft.Extensions.Options;

namespace SmartMoney.ExternalContext.Providers;

public sealed class RbiNewsSourceOptions
{
    public bool Enabled { get; set; } = true;
    public string Endpoint { get; set; } = "https://www.rbi.org.in/pressreleases_rss.xml";
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

    public static NewsCandidate? MapItem(XElement item, NewsSourceRequest request)
    {
        var title = NewsProviderUtilities.GetElementValue(item, "title");
        var link = NewsProviderUtilities.GetElementValue(item, "link");
        var summary = NewsProviderUtilities.GetElementValue(item, "description");
        var published = NewsProviderUtilities.GetElementValue(item, "pubDate");
        var guid = NewsProviderUtilities.GetElementValue(item, "guid");

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link)
            || !TryParsePublicationDate(published, out var publishedAtUtc)
            || publishedAtUtc < request.FromUtc || publishedAtUtc > request.ToUtc)
        {
            return null;
        }

        if (IsRoutineOrLowRelevance(title))
        {
            return null;
        }

        // Prefer guid, then the canonical press-release URL, for a stable identity.
        var idSource = !string.IsNullOrWhiteSpace(guid) ? guid : link;
        var prid = ExtractPrid(link);

        return new NewsCandidate
        {
            Id = NewsProviderUtilities.BuildStableId("rbi", idSource, publishedAtUtc),
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
            Tags = ["rbi", "policy", "india"],
            ExternalId = guid ?? prid
        };
    }

    private static readonly string[] RoutineTitlePatterns =
    [
        "monetary penalty",
        "auction of state government securities",
        "treasury bill",
        "weekly statistical supplement",
        "premature redemption",
        "variable rate reverse repo",
        "variable rate repo"
    ];

    private static bool IsRoutineOrLowRelevance(string title)
    {
        var t = title.ToLowerInvariant();
        return RoutineTitlePatterns.Any(t.Contains);
    }

    private static bool TryParsePublicationDate(string? rawValue, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        if (ContainsExplicitTimeZone(rawValue))
        {
            return NewsProviderUtilities.TryParseFeedDate(rawValue, out result);
        }

        if (!DateTime.TryParse(rawValue, System.Globalization.CultureInfo.GetCultureInfo("en-US"), System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var localTime))
        {
            return false;
        }

        result = new DateTimeOffset(localTime, TimeSpan.FromHours(5.5)).ToUniversalTime();
        return true;
    }

    private static bool ContainsExplicitTimeZone(string rawValue) =>
        Regex.IsMatch(rawValue, @"(?:Z|GMT|UTC|[+-]\d{2}:?\d{2})\s*$", RegexOptions.IgnoreCase);

    private static string? ExtractPrid(string link)
    {
        var match = Regex.Match(link, @"prid=(\d+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }
}
