using SmartMoney.ExternalContext.Configuration;
using SmartMoney.ExternalContext.Contracts;
using SmartMoney.ExternalContext.Export;
using SmartMoney.ExternalContext.Pipeline;
using SmartMoney.ExternalContext.Providers;
using Xunit;

namespace SmartMoney.Application.Tests.ExternalContext;

public sealed class NewsRankingTests
{
    private readonly SimpleNewsRanker _ranker = new();

    [Fact]
    public void IndiaMonetaryDecision_OutranksRoutineRegulation()
    {
        var ranked = _ranker.Rank([Candidate("RBI rate decision", NewsScope.India, NewsCategory.MonetaryMacro), Candidate("Routine disclosure update", NewsScope.India, NewsCategory.IndiaPolicyRegulation)]);

        Assert.Equal("RBI rate decision", ranked[0].NormalizedCandidate.Headline);
    }

    [Fact]
    public void GlobalMonetaryDecision_ReceivesHighScore()
    {
        var score = _ranker.Score(Candidate("Federal rate decision", NewsScope.Global, NewsCategory.MonetaryMacro));

        Assert.True(score.MarketRelevanceScore >= 75);
    }

    [Fact]
    public void IndiaPolicy_ReceivesMaximumIndiaRelevance()
    {
        var score = _ranker.Score(Candidate("GST policy reform", NewsScope.India, NewsCategory.IndiaPolicyRegulation));

        Assert.Equal(20, score.ScoreBreakdown.IndiaRelevance);
    }

    [Fact]
    public void OlderMajorEvent_OutranksRecentTrivialItem()
    {
        var oldMajor = Candidate("Monetary policy decision", NewsScope.India, NewsCategory.MonetaryMacro, DateTimeOffset.UtcNow.AddDays(-6));
        var recentTrivial = Candidate("Minor bulletin", NewsScope.India, NewsCategory.Other);

        Assert.True(_ranker.Score(oldMajor).MarketRelevanceScore > _ranker.Score(recentTrivial).MarketRelevanceScore);
    }

    [Fact]
    public void OfficialAuthority_UsesGenericSourceTypeWeighting()
    {
        var official = Candidate("Financial stability update", NewsScope.Global, NewsCategory.FinancialSystem);
        var publisher = Candidate("Financial stability update", NewsScope.Global, NewsCategory.FinancialSystem, sourceType: NewsSourceType.Publisher);

        Assert.True(_ranker.Score(official).ScoreBreakdown.SourceAuthority > _ranker.Score(publisher).ScoreBreakdown.SourceAuthority);
    }

    [Fact]
    public void ScoresRemainWithinZeroToOneHundred()
    {
        var scores = Enum.GetValues<NewsCategory>().Select(category => _ranker.Score(Candidate("Material market event", NewsScope.India, category)).MarketRelevanceScore);

        Assert.All(scores, score => Assert.InRange(score, 0, 100));
    }

    [Fact]
    public void RecencyUsesPublicationTimeNotRetrievalTime()
    {
        var recent = Candidate("Policy announcement", NewsScope.India, NewsCategory.MonetaryMacro);
        var old = Candidate("Policy announcement", NewsScope.India, NewsCategory.MonetaryMacro, DateTimeOffset.UtcNow.AddDays(-4));
        var delayedRetrieval = Candidate("Policy announcement", NewsScope.India, NewsCategory.MonetaryMacro, recent.PublishedAtUtc, retrievedAtUtc: DateTimeOffset.UtcNow.AddDays(-30));

        Assert.True(_ranker.Score(recent).ScoreBreakdown.Recency > _ranker.Score(old).ScoreBreakdown.Recency);
        Assert.Equal(_ranker.Score(recent).ScoreBreakdown.Recency, _ranker.Score(delayedRetrieval).ScoreBreakdown.Recency);
    }

    [Theory]
    [InlineData(NewsCategory.MonetaryMacro, NewsScope.India, NewsSourceType.Official, NewsImpact.High)]
    [InlineData(NewsCategory.IndiaPolicyRegulation, NewsScope.India, NewsSourceType.Publisher, NewsImpact.Medium)]
    [InlineData(NewsCategory.Other, NewsScope.Global, NewsSourceType.Other, NewsImpact.Low)]
    public void ImpactBands_AreDeterministic(NewsCategory category, NewsScope scope, NewsSourceType sourceType, NewsImpact expected)
    {
        Assert.Equal(expected, _ranker.Score(Candidate("Market update", scope, category, sourceType: sourceType, publishedAtUtc: DateTimeOffset.UtcNow.AddDays(-10))).Impact);
    }

    [Theory]
    [InlineData("War escalation creates severe supply disruption", NewsSentiment.Negative)]
    [InlineData("Ceasefire resolution supports stability", NewsSentiment.Positive)]
    [InlineData("Policy framework consultation", NewsSentiment.Mixed)]
    [InlineData("Routine data publication", NewsSentiment.Neutral)]
    public void Sentiment_UsesConservativeStrongSignals(string headline, NewsSentiment expected)
    {
        Assert.Equal(expected, _ranker.Score(Candidate(headline, NewsScope.Global, NewsCategory.Geopolitical)).Sentiment);
    }

    [Theory]
    [InlineData(NewsScope.India, NewsCategory.MonetaryMacro, "rates, banking liquidity")]
    [InlineData(NewsScope.Global, NewsCategory.OilEnergy, "India's import bill")]
    [InlineData(NewsScope.Global, NewsCategory.NaturalDisaster, "if disruption is material")]
    public void WhyItMatters_IsCategoryAppropriate(NewsScope scope, NewsCategory category, string expectedText)
    {
        Assert.Contains(expectedText, _ranker.Score(Candidate("Material event", scope, category)).WhyItMatters);
    }

    [Fact]
    public void ExactUrlAndConservativeEventDuplicates_CollapseButDistinctEventsRemain()
    {
        var deduplicator = new DefaultNewsDeduplicator();
        var first = Candidate("Government announces crude import policy change", NewsScope.India, NewsCategory.IndiaPolicyRegulation, url: "https://example.com/event");
        var sameUrl = Candidate("Different headline", NewsScope.India, NewsCategory.IndiaPolicyRegulation, url: "https://example.com/event");
        var closeEvent = Candidate("Government announces change in crude import policy", NewsScope.India, NewsCategory.IndiaPolicyRegulation);
        var distinctEvent = Candidate("Government announces GST collection policy", NewsScope.India, NewsCategory.IndiaPolicyRegulation);

        var result = deduplicator.Deduplicate([first, sameUrl, closeEvent, distinctEvent]);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, item => item.Headline == distinctEvent.Headline);
    }

    [Fact]
    public async Task DiversityAndThreshold_KeepStrongAlternativesAndAllowFewerThanFive()
    {
        var candidates = new List<NewsCandidate>
        {
            Candidate("Monetary policy rate decision alpha", NewsScope.India, NewsCategory.MonetaryMacro),
            Candidate("Liquidity policy framework beta", NewsScope.India, NewsCategory.MonetaryMacro),
            Candidate("Inflation policy consultation gamma", NewsScope.India, NewsCategory.MonetaryMacro),
            Candidate("Financial system liquidity update", NewsScope.India, NewsCategory.FinancialSystem),
            Candidate("Minor foreign bulletin", NewsScope.Global, NewsCategory.Other, sourceType: NewsSourceType.Other)
        };

        var document = await RunPipelineAsync(candidates);

        Assert.True(document.Items.Count < 5);
        Assert.Equal(2, document.Items.Count(item => item.Category == NewsCategory.MonetaryMacro));
        Assert.Contains(document.Items, item => item.Category == NewsCategory.FinancialSystem);
        Assert.DoesNotContain(document.Items, item => item.Headline == "Minor foreign bulletin");
    }

    private static NewsCandidate Candidate(string headline, NewsScope scope, NewsCategory category, DateTimeOffset? publishedAtUtc = null, NewsSourceType sourceType = NewsSourceType.Official, DateTimeOffset? retrievedAtUtc = null, string? url = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Scope = scope,
        Category = category,
        Headline = headline,
        SourceName = "Test Source",
        SourceType = sourceType,
        ArticleUrl = new Uri(url ?? $"https://example.com/{Guid.NewGuid():N}"),
        PublishedAtUtc = publishedAtUtc ?? DateTimeOffset.UtcNow.AddHours(-1),
        RetrievedAtUtc = retrievedAtUtc ?? DateTimeOffset.UtcNow
    };

    private static async Task<MarketNewsDocument> RunPipelineAsync(IReadOnlyList<NewsCandidate> candidates)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"smartmoney-ranking-test-{Guid.NewGuid():N}.json");
        try
        {
            var pipeline = new MarketNewsPipeline([new CandidateProvider(candidates)], new DefaultNewsNormalizer(), new DefaultNewsDeduplicator(), new SimpleNewsRanker(), new JsonMarketNewsExporter(outputPath));
            return await pipeline.RunAsync(new ExternalContextOptions { MaxOutputItems = 5 }, CancellationToken.None);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    private sealed class CandidateProvider(IReadOnlyList<NewsCandidate> candidates) : INewsSourceProvider
    {
        public Task<IReadOnlyList<NewsCandidate>> GetNewsAsync(NewsSourceRequest request, CancellationToken cancellationToken) => Task.FromResult(candidates);
    }
}