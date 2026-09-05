using SmartMoney.ExternalContext.Contracts;

namespace SmartMoney.ExternalContext.Providers;

public interface INewsSourceProvider
{
    string Name => GetType().Name;
    bool Enabled => true;

    Task<IReadOnlyList<NewsCandidate>> GetNewsAsync(NewsSourceRequest request, CancellationToken cancellationToken);
}
