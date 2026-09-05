namespace SmartMoney.ExternalContext.Pipeline;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartMoney.ExternalContext.Configuration;
using SmartMoney.ExternalContext.Contracts;
using SmartMoney.ExternalContext.Providers;

public interface INewsNormalizer
{
    IReadOnlyList<NewsCandidate> Normalize(IReadOnlyList<NewsCandidate> candidates);
}

public interface INewsDeduplicator
{
    IReadOnlyList<NewsCandidate> Deduplicate(IReadOnlyList<NewsCandidate> candidates);
}

public interface INewsRanker
{
    IReadOnlyList<RankedNewsCandidate> Rank(IReadOnlyList<NewsCandidate> candidates);
}

public interface IMarketNewsExporter
{
    Task ExportAsync(MarketNewsDocument document, CancellationToken cancellationToken);
}

public sealed record RankedNewsCandidate(
    NewsCandidate NormalizedCandidate,
    int MarketRelevanceScore,
    NewsImpact Impact,
    NewsSentiment Sentiment,
    string WhyItMatters);

public sealed class DefaultNewsNormalizer : INewsNormalizer
{
    public IReadOnlyList<NewsCandidate> Normalize(IReadOnlyList<NewsCandidate> candidates)
    {
        var normalized = new List<NewsCandidate>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var trimmed = new NewsCandidate
            {
                Id = string.IsNullOrWhiteSpace(candidate.Id) ? candidate.ArticleUrl.ToString() : candidate.Id.Trim(),
                Scope = candidate.Scope,
                Category = candidate.Category,
                Headline = candidate.Headline.Trim(),
                Summary = string.IsNullOrWhiteSpace(candidate.Summary) ? null : candidate.Summary.Trim(),
                SourceName = string.IsNullOrWhiteSpace(candidate.SourceName) ? "Unknown" : candidate.SourceName.Trim(),
                SourceType = candidate.SourceType,
                ArticleUrl = candidate.ArticleUrl,
                PublishedAtUtc = candidate.PublishedAtUtc,
                RetrievedAtUtc = candidate.RetrievedAtUtc,
                ExternalId = candidate.ExternalId,
                Country = candidate.Country,
                Tags = candidate.Tags
            };

            normalized.Add(trimmed);
        }

        return normalized;
    }
}

public sealed class DefaultNewsDeduplicator : INewsDeduplicator
{
    public IReadOnlyList<NewsCandidate> Deduplicate(IReadOnlyList<NewsCandidate> candidates)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduplicated = new List<NewsCandidate>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var keys = new[]
            {
                candidate.Id,
                candidate.ArticleUrl.ToString(),
                candidate.ExternalId ?? string.Empty
            };

            var key = keys.FirstOrDefault(k => !string.IsNullOrWhiteSpace(k));
            if (string.IsNullOrWhiteSpace(key) || seen.Add(key))
            {
                deduplicated.Add(candidate);
            }
        }

        return deduplicated;
    }
}

public sealed class SimpleNewsRanker : INewsRanker
{
    public IReadOnlyList<RankedNewsCandidate> Rank(IReadOnlyList<NewsCandidate> candidates)
    {
        return candidates
            .Select(candidate => new RankedNewsCandidate(
                candidate,
                CalculateMarketRelevanceScore(candidate),
                DetermineImpact(candidate),
                DetermineSentiment(candidate),
                BuildWhyItMatters(candidate)))
            .OrderByDescending(r => r.MarketRelevanceScore)
            .ThenByDescending(r => r.NormalizedCandidate.PublishedAtUtc)
            .ToList();
    }

    private static int CalculateMarketRelevanceScore(NewsCandidate candidate)
    {
        var score = candidate.Scope switch
        {
            NewsScope.India => 50,
            NewsScope.Global => 40,
            _ => 10
        };

        score += candidate.Category switch
        {
            NewsCategory.Geopolitical => 25,
            NewsCategory.OilEnergy => 20,
            NewsCategory.MonetaryMacro => 18,
            NewsCategory.IndiaPolicyRegulation => 18,
            NewsCategory.FinancialSystem => 22,
            NewsCategory.NaturalDisaster => 15,
            _ => 5
        };

        score += candidate.SourceType switch
        {
            NewsSourceType.Official => 10,
            NewsSourceType.Aggregator => 8,
            NewsSourceType.Publisher => 6,
            _ => 2
        };

        return score;
    }

    private static NewsImpact DetermineImpact(NewsCandidate candidate) =>
        candidate.Category switch
        {
            NewsCategory.Geopolitical => NewsImpact.High,
            NewsCategory.OilEnergy => NewsImpact.High,
            NewsCategory.MonetaryMacro => NewsImpact.High,
            NewsCategory.IndiaPolicyRegulation => NewsImpact.Medium,
            NewsCategory.FinancialSystem => NewsImpact.High,
            NewsCategory.NaturalDisaster => NewsImpact.Medium,
            _ => NewsImpact.Low
        };

    private static NewsSentiment DetermineSentiment(NewsCandidate candidate) =>
        candidate.Category switch
        {
            NewsCategory.Geopolitical => NewsSentiment.Negative,
            NewsCategory.OilEnergy => NewsSentiment.Mixed,
            NewsCategory.MonetaryMacro => NewsSentiment.Neutral,
            NewsCategory.IndiaPolicyRegulation => NewsSentiment.Mixed,
            NewsCategory.FinancialSystem => NewsSentiment.Negative,
            NewsCategory.NaturalDisaster => NewsSentiment.Mixed,
            _ => NewsSentiment.Neutral
        };

    private static string BuildWhyItMatters(NewsCandidate candidate)
    {
        return candidate.Scope switch
        {
            NewsScope.India => $"Policy and domestic sentiment shifts can influence local market positioning and risk appetite.",
            NewsScope.Global => $"Global dynamics can affect commodity prices, capital flows, and risk sentiment for Indian equities.",
            _ => $"This item may influence broader market sentiment and macro expectations."
        };
    }
}

public sealed class MarketNewsPipeline
{
    private readonly IReadOnlyList<INewsSourceProvider> _providers;
    private readonly INewsNormalizer _normalizer;
    private readonly INewsDeduplicator _deduplicator;
    private readonly INewsRanker _ranker;
    private readonly IMarketNewsExporter _exporter;
    private readonly ILogger<MarketNewsPipeline> _logger;

    public MarketNewsPipeline(
        IEnumerable<INewsSourceProvider> providers,
        INewsNormalizer normalizer,
        INewsDeduplicator deduplicator,
        INewsRanker ranker,
        IMarketNewsExporter exporter,
        ILogger<MarketNewsPipeline>? logger = null)
    {
        _providers = providers.ToList();
        _normalizer = normalizer;
        _deduplicator = deduplicator;
        _ranker = ranker;
        _exporter = exporter;
        _logger = logger ?? NullLogger<MarketNewsPipeline>.Instance;
    }

    public async Task<MarketNewsDocument> RunAsync(ExternalContextOptions options, CancellationToken cancellationToken)
    {
        options.Validate();

        var now = DateTimeOffset.UtcNow;
        var fromUtc = now.AddHours(-Math.Max(1, options.LookbackHours));
        var collected = new List<NewsCandidate>();

        foreach (var provider in _providers)
        {
            if (!provider.Enabled)
            {
                continue;
            }

            try
            {
                var results = await provider.GetNewsAsync(
                    new NewsSourceRequest
                    {
                        FromUtc = fromUtc,
                        ToUtc = now
                    },
                    cancellationToken);

                collected.AddRange(results);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Caller/host requested cancellation; stop the pipeline instead of swallowing it.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "External Context provider {ProviderName} failed with {ExceptionType} and was skipped.", provider.Name, ex.GetType().Name);
            }
        }

        if (options.MaxCandidates > 0 && collected.Count > options.MaxCandidates)
        {
            collected = collected.Take(options.MaxCandidates).ToList();
        }

        var normalized = _normalizer.Normalize(collected);
        var deduplicated = _deduplicator.Deduplicate(normalized);
        var ranked = _ranker.Rank(deduplicated);
        var outputLimit = Math.Max(1, options.MaxOutputItems);

        var items = ranked
            .Take(outputLimit)
            .Select((entry, index) => new MarketNewsItem
            {
                Rank = index + 1,
                Scope = entry.NormalizedCandidate.Scope,
                Category = entry.NormalizedCandidate.Category,
                Impact = entry.Impact,
                Sentiment = entry.Sentiment,
                Headline = entry.NormalizedCandidate.Headline,
                WhyItMatters = entry.WhyItMatters,
                Source = entry.NormalizedCandidate.SourceName,
                PublishedAtUtc = entry.NormalizedCandidate.PublishedAtUtc,
                Url = entry.NormalizedCandidate.ArticleUrl.ToString()
            })
            .ToList();

        var document = new MarketNewsDocument
        {
            GeneratedAtUtc = now,
            LookbackHours = Math.Max(1, options.LookbackHours),
            Items = items
        };

        await _exporter.ExportAsync(document, cancellationToken);

        return document;
    }
}
