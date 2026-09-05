# SmartMoney Runtime Architecture

## Canonical Runtime Path

The canonical SmartMoney runtime path is:

    Raw/source data
    -> SmartMoney.Job / DailyNseJob
    -> DailyPipelineService
    -> MarketScoringCalculator
    -> persisted state where applicable
    -> job-generated JSON/output files
    -> frontend

## Frontend Runtime Contract

The frontend primarily consumes job-generated output files such as:

- market_today.json
- market_history_30.json

## API Positioning

The API/controller layer is not the canonical frontend/runtime path.

Treat API/AdminController functionality as:

- debugging
- diagnostics
- local/admin utilities

unless a task explicitly requires API behavior.

## Feature Integration Rules

For new features, prefer integration with:

- SmartMoney.Job
- scoring/domain/application services
- job DTO/output generation
- frontend JSON contracts

Do not automatically modify:

- MarketController
- AdminController
- API response DTOs

when implementing dashboard/runtime features.

Only include API changes when explicitly requested or when a feature genuinely requires an API consumer.

## Phase 4 Smart/Retail Divergence

Expected Phase 4 V1 runtime path:

    calculation
    -> job output DTO
    -> generated frontend JSON
    -> dashboard

Do not include API changes in Phase 4 V1.

## Deterministic Quantitative Principle

Quantitative logic must remain deterministic and must stay outside frontend/AI decision paths.

## External Context: Tech Layoffs (layoffs.fyi)

A separate, independent path feeds the dashboard's "Tech Layoffs YTD" KPI:

    layoffs.fyi
    -> scheduled external-context fetch (scripts/fetch-layoffs-summary.mjs)
    -> frontend/public/data/layoffs_summary.json
    -> dashboard KPI

This path is intentionally decoupled from `SmartMoney.Job`, `DailyPipelineService`, and
`MarketScoringCalculator`. Layoffs data is informational only: it does not affect FinalScore,
participant scoring, PCR/VIX, Regime, ShockScore, Smart/Retail/DII calculations, narrative
decomposition, deterministic explanation, AI interpretation input, or backtesting, and it is never
written into `market_today.json`. If the external fetch fails, the previous valid
`layoffs_summary.json` is preserved (or the KPI shows "Unavailable"); the NSE/scoring pipeline is
never blocked by this fetch.

## External Context

The SmartMoney core pipeline remains independent and deterministic:

    SmartMoney Core
        └── independent deterministic market pipeline

The External Context subsystem is a separate informational stream that can later ingest
approved news providers, normalize content, deduplicate candidate items, rank them, and export
public JSON for the dashboard:

    External Context
      ├── approved news providers
      ├── normalize
      ├── exact and conservative event deduplication
      ├── deterministic relevance scoring
      ├── diversity and minimum-score threshold
      ├── Top 5
      └── market_news.json

External Context is informational and does not participate in `FinalScore`, `Regime`, or
participant calculations.

External Context failures must not fail the core SmartMoney market-data pipeline.

### External Context Provider Architecture

External Context providers are plugins behind `INewsSourceProvider`. Adding or removing a
provider must not require changes to collection, normalization, deduplication, ranking, or
export pipeline logic.

```
                       INewsSourceProvider
                              ↑
    ┌───────┬───────┬─────────┼─────────┬──────────────┬────────┐
    │       │       │         │         │              │        │
   RBI    SEBI     PIB       NSE       Fed           GDACS
    │       │       │         │         │              │        │
    └───────┴───────┴─────────┼─────────┴──────────────┴────────┘
                              ↓
              IEnumerable<INewsSourceProvider>
                              ↓
                   MarketNewsPipeline
                              ↓
                      Normalization
                              ↓
                     Deduplication
                              ↓
                        Ranking
                              ↓
                        Export
```

- Implemented providers:
  - Official India: RBI, SEBI, PIB, NSE
  - Official Global: Federal Reserve, GDACS
- Each provider owns its source-specific HTTP request construction and XML parsing,
  filtering, stable ID generation, and mapping to `NewsCandidate`.
- Provider-specific options (e.g. `RbiNewsSourceOptions`, `SebiNewsSourceOptions`,
  `PibNewsSourceOptions`, `NseNewsSourceOptions`, `FederalReserveNewsSourceOptions`,
  `GdacsNewsSourceOptions`) are independent of each other and of `ExternalContextOptions`.
- Each provider can be enabled/disabled independently (`Enabled` on its options); disabling one
  provider does not affect the others, and `MarketNewsPipeline` skips disabled providers
  generically without any provider-name branching.
- A failure in one provider is isolated by `MarketNewsPipeline` and does not block or fail the
  other providers' candidates from being collected.
- `PublishedAtUtc` is source-authoritative publication/event time only; an item with no
  authoritative source time is skipped. `RetrievedAtUtc` records when SmartMoney fetched it.
- Provider runs have provider-neutral health results: `Success` may have zero qualifying
  candidates, `Degraded` identifies a usable response with an unusable feed payload, `Failed`
  identifies a transport/execution failure, and `Disabled` identifies an intentionally skipped
  provider. Degraded or failed providers never manufacture fallback candidates.
- Explicit DI registration (`AddExternalContextProviders`) is the composition boundary where new
  providers are wired up; `MarketNewsPipeline` itself has no concrete provider references.
- Runtime reflection or plugin DLL loading is intentionally not used — explicit interfaces and
  DI registration are sufficient for this plugin model.
- External Context remains independent of SmartMoney scoring, as described above.
- The Top 5 news ranking is deterministic and independent of SmartMoney's trading score. It uses
  centralized category relevance, potential impact, source authority, India relevance, and
  publication-time recency components; candidate `RetrievedAtUtc` is never used for freshness.
- Event deduplication requires matching scope/category, publication proximity, and highly similar
  important headline tokens. Output selection requires a score of at least 45 and permits at most
  two items per category, so weak or repetitive candidates do not fill the Top 5.

#### Adding a future provider (example: a new official source)

Adding a new provider (e.g. a CBDT/Income Tax feed) is expected to require only:

- a provider file such as `CbdtNewsSourceProvider.cs`
- a matching `CbdtNewsSourceOptions.cs`
- provider-specific tests
- DI registration in `AddExternalContextProviders`
- configuration under `ExternalContext:Providers:CBDT`

and no changes to `MarketNewsPipeline`, normalization, deduplication, ranking, export, or any
existing provider.

#### Provider feed conventions

- Federal Reserve monetary-policy context comes only from the official Monetary Policy RSS feed
  (`https://www.federalreserve.gov/feeds/press_monetary.xml`), excluding general press releases
  and Enforcement Actions.
- NSE circular context comes from the NSE-linked Circulars RSS feed
  (`https://feeds.feedburner.com/nseindia/circulars`). It has no session-primed JSON API or
  browser-cookie dependency; the authoritative link supplied by the feed is retained.
- The RBI press-release RSS feed currently emits timezone-less local publication times, for
  example `Fri, 04 Sep 2026 19:05:00`. These are interpreted deterministically as India Standard
  Time (Asia/Kolkata, `+05:30`) and converted to UTC.
