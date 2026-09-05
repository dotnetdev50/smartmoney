using System.Text.Json;
using Microsoft.Extensions.Options;
using SmartMoney.ExternalContext.Contracts;

namespace SmartMoney.ExternalContext.Providers;

public sealed class GdacsNewsSourceOptions
{
    public bool Enabled { get; set; } = true;
    public string Endpoint { get; set; } = "https://www.gdacs.org/gdacsapi/api/events";
    public int TimeoutSeconds { get; set; } = 30;

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);
}

public sealed class GdacsNewsSourceProvider : INewsSourceProvider
{
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
        var endpoint = new UriBuilder(_options.Endpoint)
        {
            Query = $"fromDate={request.FromUtc:yyyy-MM-dd}&toDate={request.ToUtc:yyyy-MM-dd}"
        };
        var json = await NewsProviderUtilities.GetResponseTextAsync(_httpClient, endpoint.Uri.ToString(), _options.Timeout, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<NewsCandidate>();
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<NewsCandidate>();
        }

        var candidates = new List<NewsCandidate>();
        foreach (var item in events.EnumerateArray())
        {
            if (MapItem(item, request) is { } candidate)
            {
                candidates.Add(candidate);
            }
        }

        return candidates.OrderByDescending(candidate => candidate.PublishedAtUtc).Take(25).ToList();
    }

    private static NewsCandidate? MapItem(JsonElement item, NewsSourceRequest request)
    {
        var eventId = GetString(item, "eventid");
        var title = GetString(item, "title");
        var summary = GetString(item, "description");
        var alertLevel = GetString(item, "alertlevel");
        var url = GetString(item, "url");
        var published = GetString(item, "fromDate");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url)
            || (alertLevel?.Equals("Orange", StringComparison.OrdinalIgnoreCase) != true
                && alertLevel?.Equals("Red", StringComparison.OrdinalIgnoreCase) != true)
            || !DateTimeOffset.TryParse(published, out var publishedAtUtc)
            || publishedAtUtc < request.FromUtc || publishedAtUtc > request.ToUtc)
        {
            return null;
        }

        return new NewsCandidate
        {
            Id = NewsProviderUtilities.BuildStableId("gdacs", eventId ?? title, publishedAtUtc),
            Scope = NewsScope.Global,
            Category = NewsCategory.NaturalDisaster,
            Headline = title,
            Summary = string.IsNullOrWhiteSpace(summary) ? null : summary,
            SourceName = "GDACS",
            SourceType = NewsSourceType.Official,
            ArticleUrl = new Uri(url),
            PublishedAtUtc = publishedAtUtc,
            RetrievedAtUtc = DateTimeOffset.UtcNow,
            Country = GetString(item, "country") ?? "Global",
            Tags = [alertLevel!, "natural-disaster", "global"],
            ExternalId = eventId
        };
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString().Trim()
            : null;
    }
}
