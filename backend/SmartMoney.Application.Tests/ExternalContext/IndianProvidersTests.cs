using System.Xml.Linq;
using SmartMoney.ExternalContext.Configuration;
using SmartMoney.ExternalContext.Contracts;
using SmartMoney.ExternalContext.Export;
using SmartMoney.ExternalContext.Pipeline;
using SmartMoney.ExternalContext.Providers;
using Xunit;

namespace SmartMoney.Application.Tests.ExternalContext;

public sealed class IndianProvidersTests
{
    private static readonly DateTimeOffset SampleNow = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly NewsSourceRequest StandardRequest = new()
    {
        FromUtc = SampleNow.AddDays(-7),
        ToUtc = SampleNow.AddDays(1)
    };

    // --- SEBI TESTS (1-9) ---

    [Fact]
    public void Sebi_ValidMaterialAnnouncement_ParsedCorrectly()
    {
        var xml = CreateSebiItemXml(
            "SEBI to review Settlement Price methodology for Derivative Contracts",
            "https://www.sebi.gov.in/press/104260.html",
            "SEBI press release summary",
            "03 Sep, 2026 +0000");

        var candidate = SebiNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.NotNull(candidate);
        Assert.Equal("SEBI to review Settlement Price methodology for Derivative Contracts", candidate.Headline);
    }

    [Fact]
    public void Sebi_Scope_IsIndia()
    {
        var xml = CreateSebiItemXml("SEBI Master Circular on Mutual Funds", "https://www.sebi.gov.in/mf.html", "MF Circular", "03 Sep, 2026 +0000");
        var candidate = SebiNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.NotNull(candidate);
        Assert.Equal(NewsScope.India, candidate.Scope);
    }

    [Fact]
    public void Sebi_SourceName_IsSEBI()
    {
        var xml = CreateSebiItemXml("SEBI Framework for FPI Investments", "https://www.sebi.gov.in/fpi.html", "FPI", "03 Sep, 2026 +0000");
        var candidate = SebiNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.NotNull(candidate);
        Assert.Equal("SEBI", candidate.SourceName);
    }

    [Fact]
    public void Sebi_CategoryMapping_Works()
    {
        var xml = CreateSebiItemXml("SEBI circular on Derivatives Settlement", "https://www.sebi.gov.in/derivatives.html", "Desc", "03 Sep, 2026 +0000");
        var candidate = SebiNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.NotNull(candidate);
        Assert.Equal(NewsCategory.IndiaPolicyRegulation, candidate.Category);
    }

    [Fact]
    public void Sebi_CanonicalUrl_MatchesLink()
    {
        var link = "https://www.sebi.gov.in/media-and-notifications/press-releases/sep-2026/sebi-signs-mou_104279.html";
        var xml = CreateSebiItemXml("SEBI signs MoU with ESMA", link, "MoU", "03 Sep, 2026 +0000");
        var candidate = SebiNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.NotNull(candidate);
        Assert.Equal(link, candidate.ArticleUrl.ToString());
    }

    [Fact]
    public void Sebi_StableId_IsDeterministic()
    {
        var xml = CreateSebiItemXml("SEBI Order on Insider Trading", "https://www.sebi.gov.in/order.html", "Desc", "03 Sep, 2026 +0000");
        var c1 = SebiNewsSourceProvider.MapItem(xml, StandardRequest);
        var c2 = SebiNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.NotNull(c1);
        Assert.NotNull(c2);
        Assert.Equal(c1.Id, c2.Id);
        Assert.StartsWith("sebi-", c1.Id);
    }

    [Fact]
    public void Sebi_TimeWindowFiltering_RejectsOutdatedItem()
    {
        var xml = CreateSebiItemXml("Old SEBI Order", "https://www.sebi.gov.in/old.html", "Desc", "01 Jan, 2020 +0000");
        var candidate = SebiNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.Null(candidate);
    }

    [Fact]
    public void Sebi_MalformedItem_Skipped()
    {
        var xml = new XElement("item", new XElement("title", "No Link Or Date"));
        var candidate = SebiNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.Null(candidate);
    }

    [Fact]
    public void Sebi_RoutineEnforcementOrAppeal_FilteredOut()
    {
        var xml = CreateSebiItemXml(
            "Adjudication order in respect of Laxmi Chhugani in the matter of trading in Illiquid Stock Options at BSE",
            "https://www.sebi.gov.in/orders/104282.html",
            "Adjudication Order",
            "04 Sep, 2026 +0000");

        var candidate = SebiNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.Null(candidate);
    }

    [Fact]
    public void Sebi_SettlementOrder_IsFilteredOut()
    {
        var xml = CreateSebiItemXml(
            "Settlement Order in the matter of DMI Income Fund Pte Ltd.",
            "https://www.sebi.gov.in/enforcement/orders/104207.html",
            "Settlement order",
            "01 Sep, 2026 +0000");

        Assert.Null(SebiNewsSourceProvider.MapItem(xml, StandardRequest));
    }

    [Fact]
    public void Sebi_SettlementMethodologyRule_IsAccepted()
    {
        var xml = CreateSebiItemXml(
            "SEBI to review Settlement Price methodology for Derivative Contracts",
            "https://www.sebi.gov.in/press/104260.html",
            "Market-wide methodology consultation",
            "03 Sep, 2026 +0000");

        Assert.NotNull(SebiNewsSourceProvider.MapItem(xml, StandardRequest));
    }

    // --- PIB TESTS (10-16) ---

    [Fact]
    public void Pib_RelevantEconomicAnnouncement_Accepted()
    {
        var xml = CreatePibItemXml(
            "India-EU Free Trade Agreement opens big opportunities for MSMEs and exporters: Commerce Minister",
            "https://pib.gov.in/PressReleaseIframePage.aspx?PRID=2306838",
            "Commerce Ministry release",
            "04 Sep 2026 10:00:00 +0000");

        var candidate = PibNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.NotNull(candidate);
        Assert.Equal("India-EU Free Trade Agreement opens big opportunities for MSMEs and exporters: Commerce Minister", candidate.Headline);
    }

    [Fact]
    public void Pib_IrrelevantNonMarketAnnouncement_Rejected()
    {
        var xml = CreatePibItemXml(
            "Union Home Minister extends warm greetings on Shri Krishna Janmashtami",
            "https://pib.gov.in/PressReleaseIframePage.aspx?PRID=2306833",
            "Greetings release",
            "04 Sep 2026 10:00:00 +0000");

        var candidate = PibNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.Null(candidate);
    }

    [Fact]
    public void Pib_Scope_IsIndia()
    {
        var xml = CreatePibItemXml("GST Revenue Collection for August 2026", "https://pib.gov.in/PressReleaseIframePage.aspx?PRID=2306900", "GST", "04 Sep 2026 10:00:00 +0000");
        var candidate = PibNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.NotNull(candidate);
        Assert.Equal(NewsScope.India, candidate.Scope);
    }

    [Fact]
    public void Pib_SourceName_IsPIB()
    {
        var xml = CreatePibItemXml("Cabinet approves new Infrastructure Project funding", "https://pib.gov.in/PressReleaseIframePage.aspx?PRID=2306901", "Cabinet", "04 Sep 2026 10:00:00 +0000");
        var candidate = PibNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.NotNull(candidate);
        Assert.Equal("PIB", candidate.SourceName);
    }

    [Fact]
    public void Pib_CanonicalUrl_NormalizesIframePage()
    {
        var rawUrl = "https://pib.gov.in/PressReleaseIframePage.aspx?PRID=2306838";
        var xml = CreatePibItemXml("Finance Ministry Tariff update", rawUrl, "Tariff", "04 Sep 2026 10:00:00 +0000");
        var candidate = PibNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.NotNull(candidate);
        Assert.Equal("https://pib.gov.in/PressReleasePage.aspx?PRID=2306838", candidate.ArticleUrl.ToString());
    }

    [Fact]
    public void Pib_StableId_ContainsPrid()
    {
        var xml = CreatePibItemXml("Banking sector reforms update", "https://pib.gov.in/PressReleaseIframePage.aspx?PRID=2306836", "Banking", "04 Sep 2026 10:00:00 +0000");
        var candidate = PibNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.NotNull(candidate);
        Assert.StartsWith("pib-2306836-", candidate.Id);
    }

    [Fact]
    public void Pib_TimeFiltering_RejectsOutOfRange()
    {
        var xml = CreatePibItemXml("Cabinet disinvestment decision", "https://pib.gov.in/PressReleaseIframePage.aspx?PRID=2000000", "Disinvestment", "01 Jan 2020 10:00:00 +0000");
        var candidate = PibNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.Null(candidate);
    }

    [Fact]
    public void Pib_MissingAuthoritativePublicationDate_IsSkipped()
    {
        var xml = CreatePibItemXml(
            "GST Revenue Collection for August 2026",
            "https://pib.gov.in/PressReleaseIframePage.aspx?PRID=2306902",
            "Ministry of Finance release",
            string.Empty);

        Assert.Null(PibNewsSourceProvider.MapItem(xml, StandardRequest));
    }

    [Fact]
    public void Pib_CorporateMitraAnnouncement_IsRejected()
    {
        var xml = CreatePibItemXml(
            "First Batch of Corporate Mitra Course Commences with 2879 Learners registered",
            "https://pib.gov.in/PressReleaseIframePage.aspx?PRID=2306813",
            "Technology course announcement",
            "04 Sep 2026 10:00:00 +0000");

        Assert.Null(PibNewsSourceProvider.MapItem(xml, StandardRequest));
    }

    [Fact]
    public void Pib_InstitutionalCeremonialAnnouncement_IsRejected()
    {
        var xml = CreatePibItemXml(
            "106th Internal Finance Committee Meeting of CCRAS Held at Itanagar",
            "https://pib.gov.in/PressReleaseIframePage.aspx?PRID=2306785",
            "Institutional meeting update",
            "04 Sep 2026 10:00:00 +0000");

        Assert.Null(PibNewsSourceProvider.MapItem(xml, StandardRequest));
    }

    // --- NSE TESTS (17-23) ---

    [Fact]
    public void Nse_RealisticCircularRssItem_Parses()
    {
        var xml = CreateNseItemXml(
            "Applicability of Short-Term Additional Surveillance Measure (ST-ASM)",
            "https://nsearchives.nseindia.com/content/circulars/SURV76195.zip",
            "NSE/SURV/76195",
            "Thu, 04 Sep 2026 09:30:00 GMT",
            "NSE surveillance circular");

        var candidate = NseNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.NotNull(candidate);
        Assert.Equal("Applicability of Short-Term Additional Surveillance Measure (ST-ASM)", candidate.Headline);
    }

    [Fact]
    public void Nse_RoutineCompanyDisclosure_Rejected()
    {
        var xml = CreateNseItemXml(
            "Availability of Bandhan Contra Fund on NSE MF Invest Platform",
            "https://nsearchives.nseindia.com/content/circulars/NMF76066.pdf",
            "NSE/NMF/76066",
            "Thu, 04 Sep 2026 09:30:00 GMT",
            "Mutual fund listing");

        var candidate = NseNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.Null(candidate);
    }

    [Fact]
    public void Nse_Scope_IsIndia()
    {
        var xml = CreateNseItemXml("Revised MWPL and Position Limits for Derivatives", "https://nsearchives.nseindia.com/content/circulars/CMPT76085.zip", "NSE/CMPT/76085", "Thu, 04 Sep 2026 09:30:00 GMT", "NSE clearing circular");
        var candidate = NseNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.NotNull(candidate);
        Assert.Equal(NewsScope.India, candidate.Scope);
    }

    [Fact]
    public void Nse_SourceName_IsNSE()
    {
        var xml = CreateNseItemXml("NSE Circular on Margin Requirements", "https://nsearchives.nseindia.com/content/circulars/FAOP76000.pdf", "NSE/FAOP/76000", "Thu, 04 Sep 2026 09:30:00 GMT", "NSE derivatives circular");
        var candidate = NseNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.NotNull(candidate);
        Assert.Equal("NSE", candidate.SourceName);
    }

    [Fact]
    public void Nse_StableId_PrefersGuid()
    {
        var xml = CreateNseItemXml("Revision in Trading Hours for F&O Segment", "https://nsearchives.nseindia.com/content/circulars/TRAD76100.pdf", "NSE/TRAD/76100", "Thu, 04 Sep 2026 09:30:00 GMT", "NSE trading circular");
        var candidate = NseNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.NotNull(candidate);
        Assert.StartsWith("nse-", candidate.Id);
    }

    [Fact]
    public void Nse_CanonicalUrl_MatchesFileLink()
    {
        var fileLink = "https://nsearchives.nseindia.com/content/circulars/SURV76195.zip";
        var xml = CreateNseItemXml("Applicability of ST-ASM", fileLink, "NSE/SURV/76195", "Thu, 04 Sep 2026 09:30:00 GMT", "NSE surveillance circular");
        var candidate = NseNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.NotNull(candidate);
        Assert.Equal(fileLink, candidate.ArticleUrl.ToString());
    }

    [Fact]
    public void Nse_TimeFiltering_RejectsOutOfRangeDate()
    {
        var xml = CreateNseItemXml("Old Surveillance Circular", "https://nsearchives.nseindia.com/content/circulars/SURV10000.zip", "NSE/SURV/10000", "Wed, 01 Jan 2020 09:30:00 GMT", "NSE surveillance circular");
        var candidate = NseNewsSourceProvider.MapItem(xml, StandardRequest);

        Assert.Null(candidate);
    }

    [Fact]
    public async Task Nse_RssRetrieval_UsesOneDirectRequestWithoutSessionPriming()
    {
        var handler = new StubHttpMessageHandler("""
            <rss><channel><item><title>Revision in Trading Hours for F&amp;O Segment</title><link>https://nsearchives.nseindia.com/content/circulars/TRAD76100.pdf</link><guid>NSE/TRAD/76100</guid><pubDate>Thu, 04 Sep 2026 09:30:00 GMT</pubDate><description>NSE trading circular</description></item></channel></rss>
            """);
        using var httpClient = new HttpClient(handler);
        var provider = new NseNewsSourceProvider(httpClient, Microsoft.Extensions.Options.Options.Create(new NseNewsSourceOptions
        {
            Endpoint = "https://feeds.feedburner.com/nseindia/circulars"
        }));

        var candidates = await provider.GetNewsAsync(StandardRequest, CancellationToken.None);

        Assert.Single(candidates);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("https://feeds.feedburner.com/nseindia/circulars", handler.RequestUris.Single().ToString());
    }

    // --- ARCHITECTURE TESTS (24-27) ---

    [Fact]
    public void MarketNewsPipeline_Constructor_DoesNotReferenceConcreteProviders()
    {
        var constructor = typeof(MarketNewsPipeline).GetConstructors().Single();
        var providersParam = constructor.GetParameters().Single(p => p.Name == "providers");

        Assert.Equal(typeof(IEnumerable<INewsSourceProvider>), providersParam.ParameterType);

        var pipelineType = typeof(MarketNewsPipeline);
        var fields = pipelineType.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        foreach (var field in fields)
        {
            Assert.False(field.FieldType.Name.EndsWith("NewsSourceProvider") && !field.FieldType.IsInterface,
                $"Pipeline field {field.Name} exposes concrete provider type {field.FieldType.Name}");
        }
    }

    [Fact]
    public async Task Pipeline_DisablingSebi_DoesNotAffectPibOrNse()
    {
        var disabledSebi = new MockProvider("SEBI") { Enabled = false };
        var pib = new MockProvider("PIB");
        var nse = new MockProvider("NSE");

        var doc = await RunPipelineAsync([disabledSebi, pib, nse]);

        Assert.False(disabledSebi.Called);
        Assert.True(pib.Called);
        Assert.True(nse.Called);
        Assert.Equal(["NSE", "PIB"], doc.Items.Select(i => i.Source).OrderBy(s => s));
    }

    [Fact]
    public async Task Pipeline_DisablingPib_DoesNotAffectSebiOrNse()
    {
        var sebi = new MockProvider("SEBI");
        var disabledPib = new MockProvider("PIB") { Enabled = false };
        var nse = new MockProvider("NSE");

        var doc = await RunPipelineAsync([sebi, disabledPib, nse]);

        Assert.True(sebi.Called);
        Assert.False(disabledPib.Called);
        Assert.True(nse.Called);
        Assert.Equal(["NSE", "SEBI"], doc.Items.Select(i => i.Source).OrderBy(s => s));
    }

    [Fact]
    public async Task Pipeline_OneProviderFailing_DoesNotBlockOthers()
    {
        var sebi = new MockProvider("SEBI");
        var failingPib = new FailingProvider("PIB");
        var nse = new MockProvider("NSE");

        var doc = await RunPipelineAsync([sebi, failingPib, nse]);

        Assert.Equal(["NSE", "SEBI"], doc.Items.Select(i => i.Source).OrderBy(s => s));
    }

    // --- HELPER METHODS ---

    private static XElement CreateSebiItemXml(string title, string link, string description, string pubDate)
    {
        return new XElement("item",
            new XElement("title", title),
            new XElement("link", link),
            new XElement("description", description),
            new XElement("pubDate", pubDate));
    }

    private static XElement CreatePibItemXml(string title, string link, string description, string pubDate)
    {
        return new XElement("item",
            new XElement("title", title),
            new XElement("link", link),
            new XElement("description", description),
            new XElement("pubDate", pubDate));
    }

    private static XElement CreateNseItemXml(string title, string link, string guid, string pubDate, string description)
    {
        return new XElement("item",
            new XElement("title", title),
            new XElement("link", link),
            new XElement("guid", guid),
            new XElement("pubDate", pubDate),
            new XElement("description", description));
    }

    private static async Task<MarketNewsDocument> RunPipelineAsync(IEnumerable<INewsSourceProvider> providers)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"smartmoney-test-{Guid.NewGuid():N}.json");
        try
        {
            var pipeline = new MarketNewsPipeline(
                providers,
                new DefaultNewsNormalizer(),
                new DefaultNewsDeduplicator(),
                new SimpleNewsRanker(),
                new JsonMarketNewsExporter(tempFile));

            return await pipeline.RunAsync(new ExternalContextOptions { MaxOutputItems = 10 }, CancellationToken.None);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public StubHttpMessageHandler(string responseBody)
        {
            _responseBody = responseBody;
        }

        public int RequestCount { get; private set; }
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody)
            });
        }
    }

    private sealed class MockProvider : INewsSourceProvider
    {
        public MockProvider(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public bool Enabled { get; set; } = true;
        public bool Called { get; private set; }

        public Task<IReadOnlyList<NewsCandidate>> GetNewsAsync(NewsSourceRequest request, CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult<IReadOnlyList<NewsCandidate>>([
                new NewsCandidate
                {
                    Id = $"{Name.ToLowerInvariant()}-item-1",
                    Scope = NewsScope.India,
                    Category = NewsCategory.IndiaPolicyRegulation,
                    Headline = $"{Name} Announcement Headline",
                    SourceName = Name,
                    SourceType = NewsSourceType.Official,
                    ArticleUrl = new Uri($"https://example.com/{Name.ToLowerInvariant()}"),
                    PublishedAtUtc = SampleNow,
                    RetrievedAtUtc = SampleNow
                }
            ]);
        }
    }

    private sealed class FailingProvider : INewsSourceProvider
    {
        public FailingProvider(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public bool Enabled { get; set; } = true;

        public Task<IReadOnlyList<NewsCandidate>> GetNewsAsync(NewsSourceRequest request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException($"Provider {Name} failed unexpectedly.");
        }
    }
}
