using System.Xml.Linq;
using SmartMoney.ExternalContext.Contracts;
using SmartMoney.ExternalContext.Providers;
using Xunit;

namespace SmartMoney.Application.Tests.ExternalContext;

public sealed class RbiProviderTests
{
    private static readonly DateTimeOffset SampleNow = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly NewsSourceRequest StandardRequest = new()
    {
        FromUtc = SampleNow.AddDays(-7),
        ToUtc = SampleNow.AddDays(1)
    };

    [Fact]
    public void RealisticPressReleasePayload_Parses()
    {
        var xml = CreateItemXml(
            "Reserve Bank of India issues Statement on Developmental and Regulatory Policies",
            "https://www.rbi.org.in/scripts/BS_PressReleaseDisplay.aspx?prid=63528",
            "The Reserve Bank today issued a statement on developmental and regulatory policies.",
            "Fri, 04 Sep 2026 19:05:00",
            guid: null);

        var candidate = RbiNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.NotNull(candidate);
        Assert.Equal("RBI", candidate.SourceName);
        Assert.Equal(NewsScope.India, candidate.Scope);
        Assert.Equal(NewsCategory.MonetaryMacro, candidate.Category);
    }

    [Fact]
    public void CanonicalSourceUrl_IsRetained()
    {
        var link = "https://www.rbi.org.in/scripts/BS_PressReleaseDisplay.aspx?prid=63528";
        var xml = CreateItemXml("RBI Monetary Policy Statement", link, "Statement", "Fri, 04 Sep 2026 19:05:00", guid: null);

        var candidate = RbiNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.NotNull(candidate);
        Assert.Equal(link, candidate.ArticleUrl.ToString());
    }

    [Fact]
    public void TimezoneLessLiveFormat_IsInterpretedAsIndiaStandardTime()
    {
        var xml = CreateItemXml("RBI Monetary Policy Statement", "https://www.rbi.org.in/scripts/BS_PressReleaseDisplay.aspx?prid=63528", "Statement", "Fri, 04 Sep 2026 19:05:00", guid: null);

        var candidate = RbiNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.NotNull(candidate);
        Assert.Equal(new DateTimeOffset(2026, 9, 4, 13, 35, 0, TimeSpan.Zero), candidate.PublishedAtUtc);
    }

    [Fact]
    public void StableId_IsDeterministic()
    {
        var xml = CreateItemXml("RBI Master Direction on Digital Lending", "https://www.rbi.org.in/scripts/BS_PressReleaseDisplay.aspx?prid=63600", "Direction", "Fri, 04 Sep 2026 19:05:00", guid: null);

        var c1 = RbiNewsSourceProvider.MapItem(xml, StandardRequest);
        var c2 = RbiNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.NotNull(c1);
        Assert.NotNull(c2);
        Assert.Equal(c1.Id, c2.Id);
        Assert.StartsWith("rbi-", c1.Id);
    }

    [Fact]
    public void TimeWindowFiltering_RejectsOutdatedItem()
    {
        var xml = CreateItemXml("Old RBI Notification", "https://www.rbi.org.in/scripts/BS_PressReleaseDisplay.aspx?prid=1", "Old", "Fri, 01 Jan 2010 12:00:00", guid: null);

        var candidate = RbiNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.Null(candidate);
    }

    [Fact]
    public void MalformedItem_SkipsSafely()
    {
        var xml = new XElement("item", new XElement("title", "No Link Or Date"));

        var candidate = RbiNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.Null(candidate);
    }

    [Fact]
    public void RoutineMonetaryPenalty_IsFilteredOut()
    {
        var xml = CreateItemXml(
            "RBI imposes monetary penalty on Hinduja Leyland Finance Limited",
            "https://www.rbi.org.in/scripts/BS_PressReleaseDisplay.aspx?prid=63527",
            "Penalty order",
            "Fri, 04 Sep 2026 18:35:00",
            guid: null);

        var candidate = RbiNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.Null(candidate);
    }

    [Fact]
    public void RoutineTreasuryBillAuction_IsFilteredOut()
    {
        var xml = CreateItemXml(
            "Auction of 91-Day, 182-Day and 364-Day Treasury Bills",
            "https://www.rbi.org.in/scripts/BS_PressReleaseDisplay.aspx?prid=63524",
            "Auction notice",
            "Fri, 04 Sep 2026 18:20:00",
            guid: null);

        var candidate = RbiNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.Null(candidate);
    }

    [Fact]
    public void Guid_PreferredOverLinkForStableId()
    {
        var xml = CreateItemXml(
            "RBI Statement on Developmental Policies",
            "https://www.rbi.org.in/scripts/BS_PressReleaseDisplay.aspx?prid=63528",
            "Statement",
            "Fri, 04 Sep 2026 19:05:00",
            guid: "urn:rbi:pr:63528");

        var candidate = RbiNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.NotNull(candidate);
        Assert.Equal("urn:rbi:pr:63528", candidate.ExternalId);
    }

    private static XElement CreateItemXml(string title, string link, string description, string pubDate, string? guid)
    {
        var item = new XElement("item",
            new XElement("title", title),
            new XElement("link", link),
            new XElement("description", description),
            new XElement("pubDate", pubDate));

        if (guid is not null)
        {
            item.Add(new XElement("guid", guid));
        }

        return item;
    }
}
