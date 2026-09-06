# GitHub Copilot Instructions — SmartMoney

Read `AGENTS.md` first for repository-wide engineering rules and invariants.

Use `.ai/STATE.md` for the verified current implementation state and `.ai/DECISIONS.md` for durable decisions. Read the applicable canonical document before specialized work:

- `docs/architecture.md` for runtime architecture
- `docs/scoring-model.md` for quantitative changes
- `docs/ai-interpretation.md` for runtime AI interpretation

Use an applicable workflow under `.agents/skills/` when one matches the task. Keep this file Copilot-specific: inspect the implementation, preserve unrelated working-tree changes, and follow the repository guardrails rather than duplicating them here.
