using System.Net;
using Microsoft.Extensions.Options;
using SmartMoney.ExternalContext.Contracts;
using SmartMoney.ExternalContext.Providers;
using Xunit;

namespace SmartMoney.Application.Tests.ExternalContext;

public sealed class ProviderHealthTests
{
    private static readonly NewsSourceRequest Request = new()
    {
        FromUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        ToUtc = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero)
    };

    [Fact]
    public async Task ValidProviderWithNoCandidates_IsSuccessful()
    {
        var result = await ((INewsSourceProvider)new EmptyProvider()).GetNewsResultAsync(Request, CancellationToken.None);

        Assert.Equal(NewsProviderRunStatus.Success, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task HttpFailure_IsFailed()
    {
        var result = await ((INewsSourceProvider)new HttpFailingProvider()).GetNewsResultAsync(Request, CancellationToken.None);

        Assert.Equal(NewsProviderRunStatus.Failed, result.Status);
        Assert.Equal("HTTP_REQUEST_FAILED", result.DiagnosticCode);
    }

    [Fact]
    public async Task DisabledProvider_IsDisabled()
    {
        var result = await ((INewsSourceProvider)new EmptyProvider { Enabled = false }).GetNewsResultAsync(Request, CancellationToken.None);

        Assert.Equal(NewsProviderRunStatus.Disabled, result.Status);
    }

    [Fact]
    public async Task NseNonFeedResponse_IsDegraded()
    {
        using var client = new HttpClient(new StubHttpMessageHandler("<html><body>NSE page</body></html>"));
        var provider = new NseNewsSourceProvider(client, Microsoft.Extensions.Options.Options.Create(new NseNewsSourceOptions()));

        var result = await provider.GetNewsResultAsync(Request, CancellationToken.None);

        Assert.Equal(NewsProviderRunStatus.Degraded, result.Status);
        Assert.Equal("NON_FEED_RESPONSE", result.DiagnosticCode);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ((INewsSourceProvider)new CancellingProvider()).GetNewsResultAsync(Request, cancellation.Token));
    }

    private sealed class EmptyProvider : INewsSourceProvider
    {
        public string Name => "Empty";
        public bool Enabled { get; set; } = true;

        public Task<IReadOnlyList<NewsCandidate>> GetNewsAsync(NewsSourceRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NewsCandidate>>([]);
    }

    private sealed class HttpFailingProvider : INewsSourceProvider
    {
        public string Name => "HttpFailing";

        public Task<IReadOnlyList<NewsCandidate>> GetNewsAsync(NewsSourceRequest request, CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<NewsCandidate>>(new HttpRequestException("Synthetic HTTP failure."));
    }

    private sealed class CancellingProvider : INewsSourceProvider
    {
        public string Name => "Cancelling";

        public Task<IReadOnlyList<NewsCandidate>> GetNewsAsync(NewsSourceRequest request, CancellationToken cancellationToken) =>
            Task.FromCanceled<IReadOnlyList<NewsCandidate>>(cancellationToken);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _body;

        public StubHttpMessageHandler(string body)
        {
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_body) });
    }
}