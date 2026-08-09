# GitHub Copilot Instructions — SmartMoney

SmartMoney contains deterministic quantitative market-analysis logic and a frontend dashboard.

## Core Principle

Quantitative calculations are authoritative. AI assistance must improve engineering productivity without becoming part of the numerical decision path unless explicitly introduced in a later AI phase.

## Coding Guidance

- Follow the existing language, framework, naming, dependency-injection, logging, and repository patterns.
- Keep quantitative/domain logic in backend services or domain components, not in UI components.
- Avoid broad refactors while fixing model defects.
- Prefer explicit readable formulas over clever expressions.
- Use parentheses in mathematical expressions when precedence could be ambiguous.
- Avoid magic numbers; preserve existing constants unless the task explicitly changes them.
- Do not alter weights, thresholds, classifications, lookback windows, or score mappings as a side effect.
- Do not duplicate formulas across services when an existing shared implementation is appropriate.

## Quantitative Changes

Before modifying scoring logic:

1. Find all references to the affected method/function.
2. Identify direct and downstream score consumers.
3. Compare the code to the documented formula in `docs/scoring-model.md`.
4. Make the smallest correctness change possible.
5. Add tests in the appropriate phase.
6. Report any historical-data implications.

## Phase 0

The current development objective is z-score correctness and current-model validation.

Expected mathematical definition:

    z = (x - mean) / standardDeviation

Do not:
- retune the model;
- modify score weights;
- modify classification thresholds;
- recompute historical persisted scores;
- introduce runtime LLM/AI interpretation.

If fixing the z-score materially changes output, report the impact rather than compensating for it elsewhere.

## Frontend

The frontend displays/explains backend results. It must not independently recreate model calculations.

## AI Roadmap

Runtime AI capabilities are planned only after the quantitative foundation is validated. Until then, AI is an engineering assistant only.
