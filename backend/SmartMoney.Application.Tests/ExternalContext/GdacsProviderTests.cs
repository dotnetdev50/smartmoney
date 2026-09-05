using System.Text.Json;
using SmartMoney.ExternalContext.Contracts;
using SmartMoney.ExternalContext.Providers;
using Xunit;

namespace SmartMoney.Application.Tests.ExternalContext;

public sealed class GdacsProviderTests
{
    private static readonly DateTimeOffset SampleNow = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly NewsSourceRequest StandardRequest = new()
    {
        FromUtc = SampleNow.AddDays(-35),
        ToUtc = SampleNow.AddDays(1)
    };

    [Fact]
    public void CurrentApiFixture_Parses()
    {
        var feature = CreateFeature("EQ", 1562260, "Earthquake in China", "Earthquake in China", "Orange", "China", "2026-08-28T05:13:35", "2026-08-28T05:13:35", "https://www.gdacs.org/report.aspx?eventid=1562260&episodeid=1&eventtype=EQ");

        var candidate = GdacsNewsSourceProvider.MapItem(feature, StandardRequest);

        Assert.NotNull(candidate);
        Assert.Equal("GDACS", candidate.SourceName);
        Assert.Equal(NewsCategory.NaturalDisaster, candidate.Category);
    }

    [Fact]
    public void OrangeEvent_IsAccepted()
    {
        var feature = CreateFeature("FL", 1104081, "Flood in China", "Flood in China", "Orange", "China", "2026-08-31T01:00:00", "2026-08-31T01:00:00", "https://www.gdacs.org/report.aspx?eventid=1104081&episodeid=16&eventtype=FL");

        var candidate = GdacsNewsSourceProvider.MapItem(feature, StandardRequest);

        Assert.NotNull(candidate);
    }

    [Fact]
    public void RedEvent_IsAccepted()
    {
        var feature = CreateFeature("EQ", 1558059, "Earthquake in Indonesia", "Earthquake in Indonesia", "Red", "Indonesia", "2026-08-14T21:58:21", "2026-08-14T21:58:21", "https://www.gdacs.org/report.aspx?eventid=1558059&episodeid=1&eventtype=EQ");

        var candidate = GdacsNewsSourceProvider.MapItem(feature, StandardRequest);

        Assert.NotNull(candidate);
    }

    [Fact]
    public void GreenEvent_IsRejected()
    {
        var feature = CreateFeature("FL", 1104123, "Flood in China", "Flood in China", "Green", "China", "2026-08-22T01:00:00", "2026-08-22T01:00:00", "https://www.gdacs.org/report.aspx?eventid=1104123&episodeid=4&eventtype=FL");

        var candidate = GdacsNewsSourceProvider.MapItem(feature, StandardRequest);

        Assert.Null(candidate);
    }

    [Fact]
    public void StableId_IsDeterministic()
    {
        var feature = CreateFeature("WF", 1030252, "Forest fires in Serbia", "Forest fires in Serbia", "Orange", "Serbia", "2026-08-05T00:00:00", "2026-08-25T00:00:00", "https://www.gdacs.org/report.aspx?eventid=1030252&episodeid=40&eventtype=WF");

        var c1 = GdacsNewsSourceProvider.MapItem(feature, StandardRequest);
        var c2 = GdacsNewsSourceProvider.MapItem(feature, StandardRequest);

        Assert.NotNull(c1);
        Assert.NotNull(c2);
        Assert.Equal(c1.Id, c2.Id);
        Assert.StartsWith("gdacs-", c1.Id);
    }

    [Fact]
    public void OfficialEventReportUrl_IsRetained()
    {
        var url = "https://www.gdacs.org/report.aspx?eventid=1557236&episodeid=1&eventtype=EQ";
        var feature = CreateFeature("EQ", 1557236, "Earthquake in Colombia", "Earthquake in Colombia", "Red", "Colombia", "2026-08-10T12:34:28", "2026-08-10T12:34:28", url);

        var candidate = GdacsNewsSourceProvider.MapItem(feature, StandardRequest);

        Assert.NotNull(candidate);
        Assert.Equal(url, candidate.ArticleUrl.ToString());
    }

    [Fact]
    public void TimeWindowFiltering_RejectsOutOfRangeEvent()
    {
        var feature = CreateFeature("EQ", 1, "Old Earthquake", "Old Earthquake", "Red", "Nepal", "2010-01-01T00:00:00", "2010-01-01T00:00:00", "https://www.gdacs.org/report.aspx?eventid=1&episodeid=1&eventtype=EQ");

        var candidate = GdacsNewsSourceProvider.MapItem(feature, StandardRequest);

        Assert.Null(candidate);
    }

    [Fact]
    public void OngoingEvent_StartedBeforeWindow_IsAcceptedViaOverlap()
    {
        // GDACS server-side search returns events whose active window overlaps the query range,
        // so an event that started before the lookback window but is still ongoing must be kept.
        var feature = CreateFeature("FL", 1104081, "Flood in China", "Flood in China", "Orange", "China", "2026-07-31T01:00:00", "2026-09-03T01:00:00", "https://www.gdacs.org/report.aspx?eventid=1104081&episodeid=16&eventtype=FL");

        var candidate = GdacsNewsSourceProvider.MapItem(feature, StandardRequest);

        Assert.NotNull(candidate);
    }

    private static JsonElement CreateFeature(string eventType, int eventId, string name, string description, string alertLevel, string country, string fromDate, string toDate, string reportUrl)
    {
        var json = JsonSerializer.Serialize(new
        {
            properties = new
            {
                eventtype = eventType,
                eventid = eventId,
                name,
                description,
                alertlevel = alertLevel,
                country,
                fromdate = fromDate,
                todate = toDate,
                url = new { report = reportUrl }
            }
        });

        return JsonDocument.Parse(json).RootElement;
    }
}
