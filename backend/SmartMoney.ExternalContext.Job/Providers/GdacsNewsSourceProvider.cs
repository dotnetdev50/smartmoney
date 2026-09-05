using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SmartMoney.ExternalContext.Contracts;

namespace SmartMoney.ExternalContext.Providers;

public sealed class GdacsNewsSourceOptions
{
    public bool Enabled { get; set; } = true;
    public string Endpoint { get; set; } = "https://www.gdacs.org/gdacsapi/api/Events/geteventlist/search";
    public int TimeoutSeconds { get; set; } = 30;

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);
}

public sealed class GdacsNewsSourceProvider : INewsSourceProvider
{
    // GDACS event families of plausible market/economic relevance.
    private const string EventTypes = "EQ;TC;FL;VO;WF";
    private const string AlertLevels = "red;orange";

    private readonly HttpClient _httpClient;
    private readonly GdacsNewsSourceOptions _options;

    public GdacsNewsSourceProvider(HttpClient httpClient, IOptions<GdacsNewsSourceOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public string Name => "GDACS";
    public bool Enabled => _options.Enabled;

    public async Task<IReadOnlyList<NewsCandidate>> GetNewsAsync(NewsSourceRequest request, CancellationToken cancellationToken)
    {
        var endpoint = $"{_options.Endpoint}?eventlist={EventTypes}&fromdate={request.FromUtc:yyyy-MM-dd}&todate={request.ToUtc:yyyy-MM-dd}&alertlevel={AlertLevels}";
        var json = await NewsProviderUtilities.GetResponseTextAsync(_httpClient, endpoint, _options.Timeout, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<NewsCandidate>();
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<NewsCandidate>();
        }

        var candidates = new List<NewsCandidate>();
        foreach (var feature in features.EnumerateArray())
        {
            if (MapItem(feature, request) is { } candidate)
            {
                candidates.Add(candidate);
            }
        }

        return candidates.OrderByDescending(candidate => candidate.PublishedAtUtc).Take(25).ToList();
    }

    public static NewsCandidate? MapItem(JsonElement feature, NewsSourceRequest request)
    {
        if (!feature.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var eventType = GetString(properties, "eventtype");
        var eventId = GetString(properties, "eventid");
        var title = GetString(properties, "name");
        var summary = GetString(properties, "description");
        var alertLevel = GetString(properties, "alertlevel");
        var country = GetString(properties, "country");
        var fromDate = GetString(properties, "fromdate");
        var toDate = GetString(properties, "todate");
        var reportUrl = GetNestedString(properties, "url", "report");

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(reportUrl)
            || (alertLevel?.Equals("Orange", StringComparison.OrdinalIgnoreCase) != true
                && alertLevel?.Equals("Red", StringComparison.OrdinalIgnoreCase) != true))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(fromDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var publishedAtUtc))
        {
            return null;
        }

        // GDACS returns events whose active window overlaps the query range (not just events that
        // started within it), so an ongoing event's end date must also be checked for overlap.
        var eventEndUtc = publishedAtUtc;
        if (!string.IsNullOrWhiteSpace(toDate)
            && DateTimeOffset.TryParse(toDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedToDate))
        {
            eventEndUtc = parsedToDate;
        }

        if (eventEndUtc < request.FromUtc || publishedAtUtc > request.ToUtc)
        {
            return null;
        }

        var scope = country?.Contains("India", StringComparison.OrdinalIgnoreCase) == true ? NewsScope.India : NewsScope.Global;
        var idSource = $"{eventType}-{eventId ?? title}";

        return new NewsCandidate
        {
            Id = NewsProviderUtilities.BuildStableId("gdacs", idSource, publishedAtUtc),
            Scope = scope,
            Category = NewsCategory.NaturalDisaster,
            Headline = title,
            Summary = string.IsNullOrWhiteSpace(summary) ? null : summary,
            SourceName = "GDACS",
            SourceType = NewsSourceType.Official,
            ArticleUrl = new Uri(reportUrl),
            PublishedAtUtc = publishedAtUtc,
            RetrievedAtUtc = DateTimeOffset.UtcNow,
            Country = country ?? "Global",
            Tags = [alertLevel!, "natural-disaster", scope == NewsScope.India ? "india" : "global"],
            ExternalId = eventId
        };
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString().Trim()
            : null;
    }

    private static string? GetNestedString(JsonElement element, string outerProperty, string innerProperty)
    {
        return element.TryGetProperty(outerProperty, out var outer) && outer.ValueKind == JsonValueKind.Object
            ? GetString(outer, innerProperty)
            : null;
    }
}
