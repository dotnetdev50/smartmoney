# SmartMoney Decisions

## ADR-001 — Quantitative Engine Remains Deterministic

**Decision:** Market scores, regimes, participant biases, and quantitative metrics are calculated deterministically in application code. AI may interpret deterministic outputs but must not invent or override them.

**Reason:** Quantitative outputs must remain testable, explainable, and reproducible.

**Status:** Accepted

## ADR-002 — Static JSON Is the Canonical Production Frontend Integration

**Decision:** The current production flow remains `SmartMoney.Job -> exported JSON -> Vue frontend -> GitHub Pages`. The ASP.NET Core API is not the canonical production frontend path.

**Reason:** This is the implemented and deployed runtime path.

**Status:** Accepted

## ADR-003 — Quant Changes Require Evidence

**Decision:** Changes to formulas, windows, weights, thresholds, scaling, regime logic, divergence logic, or scoring semantics require tests and historical/backtest comparison.

**Reason:** Model behavior must be evidence-based and reviewable.

**Status:** Accepted

## ADR-004 — External Context Is Supplementary

**Decision:** News, macro, layoffs, and external context may enrich interpretation but do not implicitly modify the deterministic SmartMoney score.

**Reason:** Supplementary context must not compromise the quantitative decision path.

**Status:** Accepted

## ADR-005 — Architecture Has One Canonical Detailed Document

**Decision:** `docs/architecture.md` is the canonical detailed application architecture document. Do not duplicate it into `.ai/ARCHITECTURE.md`.

**Reason:** One detailed owner prevents architecture drift.

**Status:** Accepted

## ADR-006 — Skills Are Progressive-Disclosure Workflows

**Decision:** Only stable, repeated specialized workflows should become skills. Do not create a skill merely because a feature exists.

**Reason:** Skills should reduce repeated decision work without becoming a second documentation system.

**Status:** Accepted

## ADR-007 — Market-Data Abstraction Is Deferred

**Decision:** Do not introduce a generic `IMarketDataProvider` abstraction as part of the AI harness. Address provider architecture only after explicit design approval.

**Reason:** The current task improves development workflow, not runtime architecture.

**Status:** Accepted