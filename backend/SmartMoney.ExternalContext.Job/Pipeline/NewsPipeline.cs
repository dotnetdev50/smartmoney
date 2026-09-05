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

public sealed record NewsScoreBreakdown(
    int BaseMarketRelevance,
    int PotentialImpact,
    int SourceAuthority,
    int IndiaRelevance,
    int Recency)
{
    public int Total => BaseMarketRelevance + PotentialImpact + SourceAuthority + IndiaRelevance + Recency;
}

public sealed record RankedNewsCandidate(
    NewsCandidate NormalizedCandidate,
    int MarketRelevanceScore,
    NewsImpact Impact,
    NewsSentiment Sentiment,
    string WhyItMatters,
    NewsScoreBreakdown ScoreBreakdown);

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

            var nonEmptyKeys = keys.Where(key => !string.IsNullOrWhiteSpace(key)).ToList();
            if (nonEmptyKeys.All(key => !seen.Contains(key)))
            {
                deduplicated.Add(candidate);
                foreach (var key in nonEmptyKeys)
                {
                    seen.Add(key);
                }
            }
        }

        return DeduplicateEvents(deduplicated);
    }

    private static IReadOnlyList<NewsCandidate> DeduplicateEvents(IReadOnlyList<NewsCandidate> candidates)
    {
        var events = new List<NewsCandidate>(candidates.Count);
        foreach (var candidate in candidates.OrderByDescending(item => item.PublishedAtUtc))
        {
            if (!events.Any(existing => IsSameEvent(existing, candidate)))
            {
                events.Add(candidate);
            }
        }

        return events;
    }

    private static bool IsSameEvent(NewsCandidate first, NewsCandidate second)
    {
        if (first.Scope != second.Scope || first.Category != second.Category
            || (first.PublishedAtUtc - second.PublishedAtUtc).Duration() > TimeSpan.FromHours(24))
        {
            return false;
        }

        var firstTokens = ImportantTokens(first.Headline);
        var secondTokens = ImportantTokens(second.Headline);
        if (firstTokens.Count >= 3 && firstTokens.SetEquals(secondTokens))
        {
            return true;
        }

        var overlap = firstTokens.Intersect(secondTokens, StringComparer.Ordinal).Count();
        var union = firstTokens.Union(secondTokens, StringComparer.Ordinal).Count();
        return overlap >= 3 && union > 0 && (double)overlap / union >= 0.8;
    }

    private static HashSet<string> ImportantTokens(string value) => value
        .ToLowerInvariant()
        .Split([' ', '-', '/', ',', ':', ';', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
        .Where(token => token.Length >= 4 && token is not ("with" or "from" or "that" or "this" or "under" or "into" or "market" or "india"))
        .ToHashSet(StringComparer.Ordinal);
}

public sealed class SimpleNewsRanker : INewsRanker
{
    public const int MinimumOutputScore = 45;

    public IReadOnlyList<RankedNewsCandidate> Rank(IReadOnlyList<NewsCandidate> candidates)
    {
        return candidates
            .Select(Score)
            .OrderByDescending(r => r.MarketRelevanceScore)
            .ThenByDescending(r => r.NormalizedCandidate.PublishedAtUtc)
            .ToList();
    }

    public RankedNewsCandidate Score(NewsCandidate candidate)
    {
        var breakdown = new NewsScoreBreakdown(
            BaseMarketRelevance: BaseMarketRelevance(candidate.Category),
            PotentialImpact: PotentialImpact(candidate.Category),
            SourceAuthority: SourceAuthority(candidate.SourceType),
            IndiaRelevance: IndiaRelevance(candidate),
            Recency: Recency(candidate.PublishedAtUtc, DateTimeOffset.UtcNow));
        var score = Math.Clamp(breakdown.Total, 0, 100);

        return new RankedNewsCandidate(
            candidate,
            score,
            score >= 75 ? NewsImpact.High : score >= 50 ? NewsImpact.Medium : NewsImpact.Low,
            DetermineSentiment(candidate),
            BuildWhyItMatters(candidate),
            breakdown);
    }

    private static int BaseMarketRelevance(NewsCategory category) => category switch
    {
        NewsCategory.MonetaryMacro => 30,
        NewsCategory.FinancialSystem => 28,
        NewsCategory.Geopolitical => 27,
        NewsCategory.OilEnergy => 27,
        NewsCategory.IndiaPolicyRegulation => 24,
        NewsCategory.NaturalDisaster => 12,
        _ => 5
    };

    private static int PotentialImpact(NewsCategory category) => category switch
    {
        NewsCategory.MonetaryMacro => 25,
        NewsCategory.FinancialSystem => 24,
        NewsCategory.Geopolitical => 23,
        NewsCategory.OilEnergy => 23,
        NewsCategory.IndiaPolicyRegulation => 18,
        NewsCategory.NaturalDisaster => 10,
        _ => 3
    };

    private static int SourceAuthority(NewsSourceType sourceType) => sourceType switch
    {
        NewsSourceType.Official => 15,
        NewsSourceType.Aggregator => 9,
        NewsSourceType.Publisher => 7,
        _ => 3
    };

    private static int IndiaRelevance(NewsCandidate candidate)
    {
        if (candidate.Scope == NewsScope.India)
        {
            return 20;
        }

        return candidate.Category is NewsCategory.MonetaryMacro or NewsCategory.FinancialSystem or NewsCategory.Geopolitical or NewsCategory.OilEnergy ? 12 : 3;
    }

    private static int Recency(DateTimeOffset publishedAtUtc, DateTimeOffset now)
    {
        var age = now - publishedAtUtc;
        return age.TotalHours switch
        {
            <= 6 => 10,
            <= 24 => 8,
            <= 48 => 6,
            <= 72 => 4,
            <= 120 => 2,
            _ => 0
        };
    }

    private static NewsSentiment DetermineSentiment(NewsCandidate candidate)
    {
        var text = $"{candidate.Headline} {candidate.Summary}".ToLowerInvariant();
        if (new[] { "ceasefire", "resolution", "liquidity support", "substantial easing" }.Any(text.Contains)) return NewsSentiment.Positive;
        if (new[] { "escalation", "war expansion", "major disruption", "emergency rate hike", "systemic failure", "severe supply disruption" }.Any(text.Contains)) return NewsSentiment.Negative;
        if (new[] { "policy", "rate decision", "framework", "methodology", "regulation" }.Any(text.Contains)) return NewsSentiment.Mixed;
        return NewsSentiment.Neutral;
    }

    private static string BuildWhyItMatters(NewsCandidate candidate)
    {
        return candidate.Category switch
        {
            NewsCategory.MonetaryMacro when candidate.Scope == NewsScope.India => "Could affect rates, banking liquidity, bond yields and rate-sensitive sectors.",
            NewsCategory.MonetaryMacro => "Could influence global yields, dollar strength, foreign flows and Indian risk sentiment.",
            NewsCategory.OilEnergy => "Could affect India's import bill, inflation, rupee and crude-sensitive sectors.",
            NewsCategory.Geopolitical => "Could affect global risk appetite, commodity prices and foreign capital flows.",
            NewsCategory.IndiaPolicyRegulation => "Could affect market structure, derivatives positioning or investor behavior.",
            NewsCategory.FinancialSystem => "Could affect financial stability, liquidity and risk appetite.",
            NewsCategory.NaturalDisaster => "Could affect regional supply, infrastructure or risk sentiment if disruption is material.",
            _ => "Could affect market context if the development becomes material."
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

    public IReadOnlyList<NewsProviderResult> LastProviderResults { get; private set; } = Array.Empty<NewsProviderResult>();
    public IReadOnlyList<RankedNewsCandidate> LastSelectedCandidates { get; private set; } = Array.Empty<RankedNewsCandidate>();

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
        var providerResults = new List<NewsProviderResult>();

        foreach (var provider in _providers)
        {
            try
            {
                var result = await provider.GetNewsResultAsync(
                    new NewsSourceRequest
                    {
                        FromUtc = fromUtc,
                        ToUtc = now
                    },
                    cancellationToken);

                providerResults.Add(result);
                collected.AddRange(result.Candidates);
                if (result.Status is NewsProviderRunStatus.Degraded or NewsProviderRunStatus.Failed)
                {
                    _logger.LogWarning("External Context provider {ProviderName} completed with {Status}: {DiagnosticCode}.", result.ProviderName, result.Status, result.DiagnosticCode);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Caller/host requested cancellation; stop the pipeline instead of swallowing it.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "External Context provider {ProviderName} failed with {ExceptionType} and was skipped.", provider.Name, ex.GetType().Name);
                providerResults.Add(new NewsProviderResult
                {
                    ProviderName = provider.Name,
                    Status = NewsProviderRunStatus.Failed,
                    RetrievedAtUtc = DateTimeOffset.UtcNow,
                    DiagnosticCode = "PIPELINE_PROVIDER_FAILURE"
                });
            }
        }

        LastProviderResults = providerResults;

        if (options.MaxCandidates > 0 && collected.Count > options.MaxCandidates)
        {
            collected = collected.Take(options.MaxCandidates).ToList();
        }

        var normalized = _normalizer.Normalize(collected);
        var deduplicated = _deduplicator.Deduplicate(normalized);
        var ranked = _ranker.Rank(deduplicated);
        var outputLimit = Math.Max(1, options.MaxOutputItems);

        var selected = ApplyOutputSelection(ranked, outputLimit);
        LastSelectedCandidates = selected;
        var items = selected
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

    private static IReadOnlyList<RankedNewsCandidate> ApplyOutputSelection(IReadOnlyList<RankedNewsCandidate> ranked, int outputLimit)
    {
        var selected = new List<RankedNewsCandidate>(outputLimit);
        var categoryCounts = new Dictionary<NewsCategory, int>();
        foreach (var entry in ranked.Where(entry => entry.MarketRelevanceScore >= SimpleNewsRanker.MinimumOutputScore))
        {
            categoryCounts.TryGetValue(entry.NormalizedCandidate.Category, out var categoryCount);
            if (categoryCount >= 2)
            {
                continue;
            }

            selected.Add(entry);
            categoryCounts[entry.NormalizedCandidate.Category] = categoryCount + 1;
            if (selected.Count == outputLimit)
            {
                break;
            }
        }

        return selected;
    }
}
