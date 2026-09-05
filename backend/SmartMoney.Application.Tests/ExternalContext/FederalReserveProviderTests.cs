using System.Xml.Linq;
using SmartMoney.ExternalContext.Contracts;
using SmartMoney.ExternalContext.Providers;
using Xunit;

namespace SmartMoney.Application.Tests.ExternalContext;

public sealed class FederalReserveProviderTests
{
    private static readonly NewsSourceRequest StandardRequest = new()
    {
        FromUtc = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
        ToUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)
    };

    [Fact]
    public void MonetaryPolicyRssItem_ParsesWithRequiredMapping()
    {
        var candidate = FederalReserveNewsSourceProvider.MapItem(CreateItem("Federal Reserve issues FOMC statement", "Wed, 29 Jul 2026 18:00:00 GMT"), StandardRequest);

        Assert.NotNull(candidate);
        Assert.Equal(NewsScope.Global, candidate.Scope);
        Assert.Equal(NewsCategory.MonetaryMacro, candidate.Category);
        Assert.Equal("Federal Reserve", candidate.SourceName);
        Assert.StartsWith("fed-", candidate.Id);
    }

    [Fact]
    public void FomcMinutes_AreAccepted()
    {
        var candidate = FederalReserveNewsSourceProvider.MapItem(CreateItem("Minutes of the Federal Open Market Committee, July 28-29, 2026", "Wed, 19 Aug 2026 18:00:00 GMT"), StandardRequest);

        Assert.NotNull(candidate);
    }

    [Fact]
    public void EnforcementAction_DoesNotEnterMonetaryPolicyFeedResults()
    {
        var candidate = FederalReserveNewsSourceProvider.MapItem(
            CreateItem("Federal Reserve announces enforcement action against Example Bank", "Wed, 29 Jul 2026 18:00:00 GMT", category: "Enforcement Actions"),
            StandardRequest);

        Assert.Null(candidate);
    }

    [Fact]
    public void TimeWindowFiltering_RejectsOutdatedItem()
    {
        var candidate = FederalReserveNewsSourceProvider.MapItem(CreateItem("Federal Reserve issues FOMC statement", "Wed, 29 Jul 2020 18:00:00 GMT"), StandardRequest);

        Assert.Null(candidate);
    }

    private static XElement CreateItem(string title, string pubDate, string category = "Monetary Policy") =>
        new("item",
            new XElement("title", title),
            new XElement("link", "https://www.federalreserve.gov/newsevents/pressreleases/monetary20260729a.htm"),
            new XElement("guid", "https://www.federalreserve.gov/newsevents/pressreleases/monetary20260729a.htm"),
            new XElement("category", category),
            new XElement("description", "Federal Reserve monetary policy release."),
            new XElement("pubDate", pubDate));
}