# SmartMoney AI Agent Instructions

## Purpose

SmartMoney is a quantitative market-analysis application. AI coding agents may assist development, but quantitative logic must remain deterministic, testable, explainable, and reproducible.

## Repository Rules

1. Do not change scoring formulas, indicator direction, weights, thresholds, normalization, or classification rules without explicitly identifying the quantitative impact.
2. Treat backend/domain scoring logic as the source of truth. Do not duplicate scoring logic in the frontend.
3. Prefer small, reviewable changes. Do not combine quantitative-model changes with unrelated refactoring.
4. Preserve existing application behavior unless the task explicitly requires a change.
5. Do not silently tune thresholds or weights to make outputs “look better”.
6. Do not introduce LLM-generated values into quantitative scores.
7. Any future AI interpretation must consume deterministic SmartMoney outputs; it must not replace the scoring engine.

## Quantitative Change Protocol

For every change affecting the model:

1. State the current formula/behavior.
2. State the proposed formula/behavior.
3. Explain the mathematical reason.
4. Identify downstream consumers.
5. Add/update quantitative unit tests.
6. Recompute affected historical scores when required.
7. Run backtesting when the framework is available.
8. Compare before/after metrics.
9. Record whether the change is accepted or rejected.

## Phase 0 Guardrails

Current Phase 0 objective:

- Correct and validate z-score calculation.
- Trace all downstream consumers.
- Validate current/recent model outputs.
- Do not retune weights or thresholds.
- Do not rebuild all historical scores yet.
- Do not introduce runtime AI features yet.

Canonical z-score:

    z = (x - mean) / standardDeviation

Special cases must be explicit and deterministic, especially zero or near-zero standard deviation, insufficient observations, nulls, NaN, and Infinity.

## Definition of Done for Phase 0

Phase 0 is complete only when:

- z-score implementation is mathematically correct;
- edge-case behavior is known;
- all z-score consumers are identified;
- score calculation flow is documented;
- representative recent calculations are sanity checked;
- unrelated thresholds/weights remain unchanged.

## Working Style for AI Agents

Before editing code, inspect the actual implementation and references.

After editing:
- summarize files changed;
- explain model impact;
- report build/test results;
- call out any uncertainty;
- never claim validation that was not actually performed.
