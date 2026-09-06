# SmartMoney Current State

## Runtime

- .NET 8 `SmartMoney.Job` is the canonical production job.
- It persists scoring data in SQLite and exports static JSON to `frontend/public/data/`.
- The scoring engine is deterministic.
- The frontend is Vue 3 and deployment is GitHub Pages.
- The runtime AI interpretation foundation is optional.
- External Context is implemented independently.
- `SmartMoney.BacktestRunner` provides offline evaluation.

## Implemented Capabilities

- Participant OI ingestion and FII/DII/PRO/Retail scoring
- Smart Money versus Retail divergence
- NIFTY and BANKNIFTY PCR where available, and India VIX
- Participant activity, deterministic narrative, and historical market bias
- Backtesting, forward-return evaluation, and signal-accuracy metrics
- Runtime AI interpretation foundation and external context/news

## Partial or Missing Capabilities

- FII long/short legs are not separately persisted or exported.
- Institutional momentum is not a dedicated metric.
- Market structure is partial, not a standalone model.
- Participant activity export is partial for Retail.
- Ask SmartMoney conversational capability is missing.
- AI interpretation infrastructure exists; effective behavior depends on runtime configuration.
- Core market-data providers remain concrete and NSE-coupled.

## Technical Risks

- Core NSE provider coupling
- Limited mocked HTTP tests for PCR/VIX and market-data scraping
- CI does not execute the xUnit suite
- API and canonical job use different persistence paths
- PCR fallback works primarily at aggregate-result level rather than filling each missing field independently

## Existing Working Tree

`frontend/src/pages/Dashboard.vue` contains unrelated in-flight changes. Preserve it until that work is resolved.

## Canonical References

- `AGENTS.md`
- `docs/architecture.md`
- `docs/scoring-model.md`
- `docs/ai-interpretation.md`
- `.ai/DECISIONS.md`