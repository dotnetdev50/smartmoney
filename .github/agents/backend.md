# Backend Agent

You are an expert in the SmartMoney backend — a .NET 8 solution located in the `backend/` directory.

## Solution layout

```
backend/
  SmartMoney.sln
  SmartMoney.Job/          ← console project (primary focus)
  SmartMoney/              ← ASP.NET Core 8 Web API project (secondary; not used in CI/CD)
  SmartMoney.Application/  ← application-layer services and options
  SmartMoney.Domain/       ← domain entities and enums
  SmartMoney.Infrastructure/ ← EF Core DbContext, migrations
```

## Primary project: SmartMoney.Job (console)

This is the only project that runs in CI/CD (GitHub Actions). It is a `net8.0` console application.

### What it does

1. **Calculates an IST date range** (last 90 calendar days up to today) to ensure ≥ 20 trading days of history.
2. **Bootstraps a DI container** using `Microsoft.Extensions.DependencyInjection` — no `WebApplication` or host builder.
3. **Runs EF Core migrations** against a local SQLite database (`jobdb.sqlite`) so the schema is always up-to-date before any work starts.
4. **Ingests raw CSV data** from NSE archives for every weekday in the range, using `CsvIngestionService`.
5. **Runs the daily pipeline** for the same range using `DailyPipelineService`, which computes z-score-based participant metrics and a composite market-bias score.
6. **Exports JSON files** to `frontend/dist/data/` so the static Vue dashboard can read them after deployment:
   - `market_today.json` — latest date's bias, regime, shock score, and per-participant bias.
   - `market_history_30.json` — last 30 days of bias history.
   - `job_ingest_result.json` / `job_run_result.json` — debug artifacts.

Entry point: `backend/SmartMoney.Job/Program.cs` (top-level statements).

### Key services (in SmartMoney.Application)

| Service | Responsibility |
|---|---|
| `CsvIngestionService` | Downloads NSE participant OI CSV, parses it, upserts `ParticipantRawData` rows into SQLite. |
| `DailyPipelineService` | Loads raw data, computes short/long z-scores for futures, put-writing proxy, call-writing proxy per participant, derives composite `MarketBias` and `ParticipantMetric` rows. |
| `MarketPresentationService` | Formats bias labels and natural-language explanations (used by the API; not called by the console job). |
| `NormalizationService` / `ParticipantScoreCalculator` / `BiasEngineService` | Additional calculation helpers. |

### Database

- **Provider**: SQLite (via `Microsoft.EntityFrameworkCore.Sqlite`).
- **DbContext**: `SmartMoneyDbContext` in `SmartMoney.Infrastructure`.
- **Tables**: `ParticipantRawData`, `ParticipantMetrics`, `MarketBiases`, `JobRunLogs`.
- **Migrations** live in `backend/SmartMoney.Infrastructure/Migrations/`.
- The DB file is ephemeral in CI (`jobdb.sqlite` next to the working directory).

### Configuration / options

| Options class | Used for |
|---|---|
| `NseOptions` | NSE archives base URL and CSV template (`fao_participant_oi_{ddMMyyyy}.csv`). |
| `NseJobOptions` | Feature flag (`Enabled`), retry schedule, expected rows per day. |

Options are configured in `Program.cs` using `services.Configure<T>()` inline (no `appsettings.json` in the console project).

### How to run locally

```bash
cd backend
dotnet restore
dotnet run --project SmartMoney.Job/SmartMoney.Job.csproj
```

### How to add a migration

```bash
cd backend
dotnet ef migrations add <MigrationName> --project SmartMoney.Infrastructure --startup-project SmartMoney
```

### CI/CD

The workflow `.github/workflows/pages.yml` runs on a schedule (weekdays at 9:40 PM IST) and on `workflow_dispatch`:

1. Builds the Vue frontend (`npm run build`).
2. Builds the .NET solution (`dotnet build -c Release`).
3. Runs `dotnet run -c Release --project backend/SmartMoney.Job/SmartMoney.Job.csproj`.
4. Uploads `frontend/dist` as the GitHub Pages artifact.

## Secondary project: SmartMoney (Web API)

A standard ASP.NET Core 8 Web API used for local development or future hosting. It uses SQL Server (`Microsoft.EntityFrameworkCore.SqlServer`) via a `ConnectionStrings:Default` setting. It exposes:

- `GET  /api/market/today` — latest market bias.
- `GET  /api/market/history?days=30` — historical bias.
- `POST /api/admin/ingest/participant-oi?date=` — manual ingestion trigger.
- `POST /api/admin/ingest/range?from=&to=` — range ingestion.
- `POST /api/admin/run?date=` — run pipeline for a single day.
- `POST /api/admin/run/range?from=&to=` — run pipeline over a date range.
- `POST /api/admin/bootstrap?days=45` — ingest + run in one call.
- `GET  /api/admin/job/status` — last job run metadata.

## Domain model summary

- **`ParticipantType`** enum: `FII`, `DII`, `Pro`, `Retail` — each has a weight in the composite score (0.4 / 0.2 / 0.3 / 0.1).
- **`Regime`** enum: `Normal` (long-biased blend) or `Shock` (short-biased blend; triggered when aggregate divergence > 1.5).
- **`ParticipantRawData`** — raw futures/options net from NSE CSV per date.
- **`ParticipantMetric`** — z-score components and computed participant bias for a date.
- **`MarketBias`** — composite final score (tanh-scaled to [-100, 100]), regime, and shock score for a date.

## Coding conventions

- C# 12, .NET 8, nullable reference types enabled, implicit usings enabled.
- Primary constructors used for services.
- `sealed` classes preferred for application services.
- All async methods accept and forward `CancellationToken ct`.
- EF Core queries use `.AsNoTracking()` for read-only paths.
- Upserts are implemented as delete-then-insert for simplicity.
