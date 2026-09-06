using SmartMoney.Domain.Entities;

namespace SmartMoney.Job.Export;

public static class ParticipantActivityFactory
{
    private const double DisplayEpsilon = 1e-10;

    public static IReadOnlyList<ParticipantActivityRowDto> CreateRows(ParticipantRawData todayRow, ParticipantRawData? prevRow)
    {
        var participant = todayRow.Participant.ToString().ToUpperInvariant();

        var previousFuturesNet = prevRow?.FuturesNet ?? 0.0;
        var previousPutWritingSignal = prevRow?.PutOiChange ?? 0.0;
        var previousCallWritingSignal = prevRow?.CallOiChange ?? 0.0;
        var previousPutDirectionalSignal = previousPutWritingSignal;
        var previousCallDirectionalSignal = -previousCallWritingSignal;

        // User-facing convention: positive = bullish directional pressure, negative = bearish.
        // FuturesChange already follows that convention.
        // PutOiChange is a put-writing proxy, so positive is already bullish.
        // CallOiChange is a call-writing proxy, so invert it for display.
        var futuresSignal = todayRow.FuturesChange;
        var putSignal = todayRow.PutOiChange - previousPutWritingSignal;
        var callSignal = -(todayRow.CallOiChange - previousCallWritingSignal);

        var futuresPct = Math.Abs(previousFuturesNet) > 1.0
            ? Math.Round(futuresSignal / Math.Abs(previousFuturesNet) * 100.0, 2)
            : (double?)null;
        var putPct = Math.Abs(previousPutDirectionalSignal) > DisplayEpsilon
            ? Math.Round(putSignal / Math.Abs(previousPutDirectionalSignal) * 100.0, 2)
            : (double?)null;
        var callPct = Math.Abs(previousCallDirectionalSignal) > DisplayEpsilon
            ? Math.Round(callSignal / Math.Abs(previousCallDirectionalSignal) * 100.0, 2)
            : (double?)null;

        return
        [
            new ParticipantActivityRowDto(participant, "Futures", futuresSignal, futuresPct),
            new ParticipantActivityRowDto(participant, "Calls", callSignal, callPct),
            new ParticipantActivityRowDto(participant, "Puts", putSignal, putPct)
        ];
    }
}
