using System.Text.Json.Serialization;

namespace SmartMoney.ExternalContext.Contracts;

public enum NewsScope
{
    India,
    Global
}

public enum NewsCategory
{
    Geopolitical,
    OilEnergy,
    MonetaryMacro,
    IndiaPolicyRegulation,
    FinancialSystem,
    NaturalDisaster,
    Other
}

public enum NewsImpact
{
    High,
    Medium,
    Low
}

public enum NewsSentiment
{
    Positive,
    Negative,
    Mixed,
    Neutral
}

public enum NewsSourceType
{
    Official,
    Aggregator,
    Publisher,
    Other
}

public sealed class NewsSourceRequest
{
    public DateTimeOffset FromUtc { get; set; }
    public DateTimeOffset ToUtc { get; set; }
}

public sealed class NewsCandidate
{
    public string Id { get; set; } = string.Empty;
    public NewsScope Scope { get; set; }
    public NewsCategory Category { get; set; }
    public string Headline { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public NewsSourceType SourceType { get; set; }
    public Uri ArticleUrl { get; set; } = new("https://example.com");
    public DateTimeOffset PublishedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset RetrievedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? ExternalId { get; set; }
    public string? Country { get; set; }
    public IReadOnlyCollection<string>? Tags { get; set; }
}

public sealed class MarketNewsDocument
{
    [JsonPropertyName("generated_at_utc")]
    public DateTimeOffset GeneratedAtUtc { get; set; }

    [JsonPropertyName("lookback_hours")]
    public int LookbackHours { get; set; }

    [JsonPropertyName("items")]
    public IReadOnlyList<MarketNewsItem> Items { get; set; } = Array.Empty<MarketNewsItem>();
}

public sealed class MarketNewsItem
{
    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    [JsonPropertyName("scope")]
    public NewsScope Scope { get; set; }

    [JsonPropertyName("category")]
    public NewsCategory Category { get; set; }

    [JsonPropertyName("impact")]
    public NewsImpact Impact { get; set; }

    [JsonPropertyName("sentiment")]
    public NewsSentiment Sentiment { get; set; }

    [JsonPropertyName("headline")]
    public string Headline { get; set; } = string.Empty;

    [JsonPropertyName("why_it_matters")]
    public string WhyItMatters { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("published_at_utc")]
    public DateTimeOffset PublishedAtUtc { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}
