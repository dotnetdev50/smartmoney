using SmartMoney.Domain.Entities;
using SmartMoney.Domain.Enums;
using SmartMoney.Job.Export;
using Xunit;

namespace SmartMoney.Application.Tests.Export;

public sealed class ParticipantActivityFactoryTests
{
    [Fact]
    public void CreateRows_UsesDirectionalCallSignal_ForProRegressionFixture()
    {
        var previous = new ParticipantRawData
        {
            Participant = ParticipantType.Pro,
            FuturesNet = 15_800,
            FuturesChange = 0,
            PutOiChange = 119_777,
            CallOiChange = 74_441
        };

        var current = new ParticipantRawData
        {
            Participant = ParticipantType.Pro,
            FuturesNet = 14_608,
            FuturesChange = -1_192,
            PutOiChange = 136_270,
            CallOiChange = -73_689
        };

        var rows = ParticipantActivityFactory.CreateRows(current, previous).ToDictionary(x => x.instrument);

        Assert.Equal(-1_192, rows["Futures"].net_oi_change);
        Assert.Equal(16_493, rows["Puts"].net_oi_change);
        Assert.Equal(148_130, rows["Calls"].net_oi_change);
        Assert.Equal(198.99, rows["Calls"].vs_yesterday_pct);
    }

    [Theory]
    [InlineData(ParticipantType.FII, 12_000, 3_000, 9_000)]
    [InlineData(ParticipantType.DII, 3_500, 4_000, -500)]
    [InlineData(ParticipantType.Pro, 74_441, -73_689, 148_130)]
    [InlineData(ParticipantType.Retail, -6_000, -2_000, -4_000)]
    public void CreateRows_FlipsCallWritingProxyIntoDirectionalCallSignal(
        ParticipantType participant,
        double previousCallWritingProxy,
        double currentCallWritingProxy,
        double expectedDirectionalCallSignal)
    {
        var previous = new ParticipantRawData
        {
            Participant = participant,
            FuturesNet = 1,
            PutOiChange = 1,
            CallOiChange = previousCallWritingProxy
        };

        var current = new ParticipantRawData
        {
            Participant = participant,
            FuturesNet = 1,
            PutOiChange = 1,
            CallOiChange = currentCallWritingProxy
        };

        var callRow = ParticipantActivityFactory.CreateRows(current, previous)
            .Single(x => x.instrument == "Calls");

        Assert.Equal(expectedDirectionalCallSignal, callRow.net_oi_change);
    }
}
