namespace SmartMoney.ExternalContext.Ranking;

using SmartMoney.ExternalContext.Contracts;
using SmartMoney.ExternalContext.Pipeline;

public sealed class NewsRanker : INewsRanker
{
    private readonly SimpleNewsRanker _inner = new();

    public IReadOnlyList<RankedNewsCandidate> Rank(IReadOnlyList<NewsCandidate> candidates)
    {
        return _inner.Rank(candidates);
    }
}
