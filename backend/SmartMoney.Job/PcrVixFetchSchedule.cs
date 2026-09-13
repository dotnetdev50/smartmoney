using System.Globalization;

namespace SmartMoney.Job;

public static class PcrVixFetchSchedule
{
    private static readonly TimeSpan IstOffset = TimeSpan.FromHours(5.5);

    public static bool ShouldDeferUntilStart(DateTime marketDate, DateTimeOffset utcNow, string? startAtIst)
    {
        var istNow = utcNow.ToOffset(IstOffset);
        if (!TimeSpan.TryParse(startAtIst, CultureInfo.InvariantCulture, out var startAt))
            return false;

        return marketDate.Date == istNow.Date && istNow.TimeOfDay < startAt;
    }
}
