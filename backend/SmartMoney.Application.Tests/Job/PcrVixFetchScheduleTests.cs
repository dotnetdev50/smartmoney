using SmartMoney.Job;
using Xunit;

namespace SmartMoney.Application.Tests.Job;

public sealed class PcrVixFetchScheduleTests
{
    [Fact]
    public void ShouldDeferUntilStart_ReturnsFalse_ForPreviousTradingDateBeforeStartTime()
    {
        var utcNow = new DateTimeOffset(2026, 9, 7, 2, 36, 0, TimeSpan.Zero); // 08:06 IST

        var result = PcrVixFetchSchedule.ShouldDeferUntilStart(
            new DateTime(2026, 9, 4),
            utcNow,
            "20:30");

        Assert.False(result);
    }

    [Fact]
    public void ShouldDeferUntilStart_ReturnsTrue_ForSameDayBeforeStartTime()
    {
        var utcNow = new DateTimeOffset(2026, 9, 7, 2, 36, 0, TimeSpan.Zero); // 08:06 IST

        var result = PcrVixFetchSchedule.ShouldDeferUntilStart(
            new DateTime(2026, 9, 7),
            utcNow,
            "20:30");

        Assert.True(result);
    }
}
