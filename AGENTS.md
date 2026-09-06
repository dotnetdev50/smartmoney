# SmartMoney Engineering Guardrails

## General Rules

- Inspect the existing implementation before changing it.
- Preserve existing behavior unless explicitly requested.
- Prefer small, loosely coupled changes and respect existing abstractions and conventions.
- Do not refactor unrelated code.
- Validate with repository-native build and test tooling.
- Review `git diff` and `git status` before finishing.
- Do not commit, push, deploy, or perform destructive actions unless requested.
- Preserve unrelated in-flight working-tree changes.

## SmartMoney Invariants

- The quantitative engine is deterministic.
- AI interpretation consumes deterministic outputs; it must not invent, override, or silently recalculate market bias.
- Quantitative changes require mathematical tests and historical/backtest validation.
- `SmartMoney.Job -> static JSON -> Vue -> GitHub Pages` is the canonical production runtime path.
- The ASP.NET Core `SmartMoney` API is secondary and diagnostic, not the production frontend path.
- External Context/news is supplementary and must not implicitly alter the deterministic bias score.

## Canonical References

- Detailed architecture: `docs/architecture.md`
- Quantitative formulas: `docs/scoring-model.md`
- Runtime AI interpretation: `docs/ai-interpretation.md`
- Current implementation status: `.ai/STATE.md`
- Durable decisions: `.ai/DECISIONS.md`
