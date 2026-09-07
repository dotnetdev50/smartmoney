using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartMoney.Application.Options;
using SmartMoney.Application.Services;
using Xunit;

namespace SmartMoney.Application.Tests.Services;

public sealed class VixFetchServiceTests
{
    [Fact]
    public async Task FetchVixAsync_UsesHistoricalEndpointAndParsesDataArray()
    {
        var handler = new SequenceHttpMessageHandler(
            CreateResponse(HttpStatusCode.OK, "<html>ok</html>"),
            CreateResponse(HttpStatusCode.OK, """
                {"data":[{"EOD_TIMESTAMP":"07-Sep-2026","EOD_CLOSE_INDEX_VAL":"13.456"}]}
                """));

        using var http = new HttpClient(handler);
        var options = Microsoft.Extensions.Options.Options.Create(new NseOptions());
        var sut = new VixFetchService(http, options, NullLogger<VixFetchService>.Instance);

        var result = await sut.FetchVixAsync(new DateTime(2026, 9, 7), CancellationToken.None);

        Assert.Equal(13.46, result);
        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Equal("/api/historical/vixhistory", handler.RequestUris[1].AbsolutePath);
        Assert.Contains("from=08-09-2025", handler.RequestUris[1].Query);
        Assert.Contains("to=07-09-2026", handler.RequestUris[1].Query);
        Assert.DoesNotContain("csv=", handler.RequestUris[1].Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchVixAsync_FallsBackToArchiveCsv_WhenApiFails()
    {
        var archiveUrl = "https://nsearchives.nseindia.com/content/indices/hist_vix_data.csv";
        var handler = new SequenceHttpMessageHandler(
            CreateResponse(HttpStatusCode.OK, "<html>ok</html>"),
            CreateResponse(HttpStatusCode.Forbidden, string.Empty),
            CreateResponse(HttpStatusCode.OK, """
                Date,Open,High,Low,Close,Prev Close,Change,%Change
                07-Sep-2026,1,1,1,15.67,15.00,0.67,4.47
                """));

        using var http = new HttpClient(handler);
        var options = Microsoft.Extensions.Options.Options.Create(new NseOptions { VixArchiveUrl = archiveUrl });
        var sut = new VixFetchService(http, options, NullLogger<VixFetchService>.Instance);

        var result = await sut.FetchVixAsync(new DateTime(2026, 9, 7), CancellationToken.None);

        Assert.Equal(15.67, result);
        Assert.Equal(3, handler.RequestUris.Count);
        Assert.Equal(new Uri(archiveUrl), handler.RequestUris[2]);
    }

    private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode) { Content = new StringContent(body) };

    private sealed class SequenceHttpMessageHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly HttpResponseMessage[] _responses = responses;
        private int _index;

        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            var response = _responses[_index];
            _index++;
            return Task.FromResult(response);
        }
    }
}
