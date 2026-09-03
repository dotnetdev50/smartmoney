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
