using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartMoney.Application.Scoring;
using SmartMoney.Domain.Entities;
using SmartMoney.Domain.Enums;
using SmartMoney.Infrastructure.Persistence;

namespace SmartMoney.Application.Services;

public sealed class DailyPipelineService(
    SmartMoneyDbContext db,
    ILogger<DailyPipelineService> log,
    MarketScoringCalculator scoring)
{
    public async Task<bool> IsMarketBiasPresentAsync(DateTime date, CancellationToken ct)
    {
        date = date.Date;
        return await db.MarketBiases.AnyAsync(x => x.Date == date, ct);
    }

    public async Task<PipelineRunResult> RunAsync(DateTime date, CancellationToken ct)
    {
        date = date.Date;

        // 1) Load last 20 days raw including today
        var raw = await db.ParticipantRawData
            .AsNoTracking()
            .Where(x => x.Date <= date)
            .OrderBy(x => x.Date)
            .ToListAsync(ct);

        if (raw.Count == 0)
            return new PipelineRunResult(date, false, "No raw data found.");

        // 2) Group by participant
        var grouped = raw
            .GroupBy(x => x.Participant)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 3) Compute per participant metrics for 'date'
        var metricsForDate = new List<ParticipantMetric>();
        var participantBias = new Dictionary<ParticipantType, double>();

        // First compute divergence and shock score (regime depends on all participants)
        double shockScore = 0;

        // store z values to reuse
        var zStore = new Dictionary<ParticipantType, ZPack>();

        foreach (var (participant, series) in grouped)
        {
            var todayRow = series.Count > 0 && series[series.Count - 1].Date == date
                ? series[series.Count - 1]
                : null;
            if (todayRow is null) continue;

            // Use only last LongWindow points up to date
            var window = series.Where(x => x.Date <= date).TakeLast(MarketScoringCalculator.LongWindow).ToList();
            if (window.Count < MarketScoringCalculator.LongWindow)
            {
                log.LogInformation("Not enough data for {Participant}: {Count}/{Need}", participant, window.Count, MarketScoringCalculator.LongWindow);
                continue;
            }

            // Build indicator series from raw
            var futures = window.Select(x => x.FuturesChange).ToList();
            var puts = window.Select(x => x.PutOiChange).ToList();
            var calls = window.Select(x => x.CallOiChange).ToList();

            var fz = scoring.ComputeZShortLong(futures);
            var pz = scoring.ComputeZShortLong(puts);
            var cz = scoring.ComputeZShortLong(calls);

            var z = new ZPack(fz.Short, fz.Long, pz.Short, pz.Long, cz.Short, cz.Long);
            zStore[participant] = z;

            // divergence = sum of abs(short-long) across signals
            var divergence =
                Math.Abs(fz.Short - fz.Long) +
                Math.Abs(pz.Short - pz.Long) +
                Math.Abs(cz.Short - cz.Long);

            shockScore += scoring.GetParticipantWeight(participant) * divergence;
        }

        var regime = scoring.ComputeRegime(shockScore);

        // 4) Now compute bias and persist metrics rows (idempotent overwrite for date)
        foreach (var (participant, z) in zStore)
        {
            var fEff = scoring.Blend(z.FuturesShort, z.FuturesLong, regime);
            var pEff = scoring.Blend(z.PutShort, z.PutLong, regime);
            var cEff = scoring.Blend(z.CallShort, z.CallLong, regime);

            // For V1: futures = directional, put writing = bullish, call writing = bearish
            // We subtract call component.
            var bias = scoring.ComputeParticipantBias(fEff, pEff, cEff);

            participantBias[participant] = bias;

            metricsForDate.Add(new ParticipantMetric
            {
                Id = Guid.NewGuid(),
                Date = date,
                Participant = participant,

                FuturesZShort = z.FuturesShort,
                FuturesZLong = z.FuturesLong,
                PutZShort = z.PutShort,
                PutZLong = z.PutLong,
                CallZShort = z.CallShort,
                CallZLong = z.CallLong,

                ParticipantBias = bias
            });
        }

        if (metricsForDate.Count == 0)
            return new PipelineRunResult(date, false, "No metrics produced (likely insufficient history).");

        // 5) Composite market bias + tanh scaling
        var marketRaw = scoring.ComputeMarketRawScore(participantBias);

        // scale [-100..100] feel; tanh keeps it bounded
        var finalScore = scoring.ComputeFinalScore(marketRaw);

        // 6) Persist: delete existing date rows then insert (simple idempotency)
        var existingMetrics = await db.ParticipantMetrics.Where(x => x.Date == date).ToListAsync(ct);
        if (existingMetrics.Count > 0) db.ParticipantMetrics.RemoveRange(existingMetrics);

        var existingMarket = await db.MarketBiases.Where(x => x.Date == date).ToListAsync(ct);
        if (existingMarket.Count > 0) db.MarketBiases.RemoveRange(existingMarket);

        await db.ParticipantMetrics.AddRangeAsync(metricsForDate, ct);

        await db.MarketBiases.AddAsync(new MarketBias
        {
            Id = Guid.NewGuid(),
            Date = date,
            RawBias = marketRaw,
            FinalScore = finalScore,
            Regime = regime,
            ShockScore = shockScore
        }, ct);

        await db.SaveChangesAsync(ct);

        return new PipelineRunResult(date, true, $"OK. Metrics={metricsForDate.Count}, Regime={regime}, Final={finalScore:F1}");
    }

    public async Task<object> RunRangeAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        from = from.Date;
        to = to.Date;

        var ok = new List<object>();
        var failed = new List<object>();
        var skipped = 0;

        for (var d = from; d <= to; d = d.AddDays(1))
        {
            ct.ThrowIfCancellationRequested();

            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                skipped++;
                continue;
            }

            try
            {
                var r = await RunAsync(d, ct);
                if (!r.Success)
                {
                    failed.Add(new { date = d.ToString("yyyy-MM-dd"), reason = r.Note });
                }
                else
                {
                    ok.Add(new { date = d.ToString("yyyy-MM-dd"), note = r.Note });
                }
            }
            catch (Exception ex)
            {
                failed.Add(new { date = d.ToString("yyyy-MM-dd"), reason = ex.Message });
            }
        }

        return new
        {
            from = from.ToString("yyyy-MM-dd"),
            to = to.ToString("yyyy-MM-dd"),
            skippedWeekends = skipped,
            successDays = ok.Count,
            failedDays = failed.Count,
            ok,
            failed
        };
    }

    private sealed record ZPack(
        double FuturesShort, double FuturesLong,
        double PutShort, double PutLong,
        double CallShort, double CallLong
    );
}

public sealed record PipelineRunResult(DateTime Date, bool Success, string Note);