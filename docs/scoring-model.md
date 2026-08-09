# SmartMoney Scoring Model

## Status

This document is the human-readable specification for SmartMoney quantitative scoring.

Current implementation phase: **Phase 0 — z-score correctness and model validation**.

This document should be updated as the actual backend implementation is inspected. Do not invent missing model rules.

## 1. Processing Flow

Current target flow:

    Market / derived input data
        -> rolling statistics
        -> normalization / z-score
        -> indicator-level scores
        -> weighted aggregation
        -> final transformation / normalization
        -> classification
        -> deterministic explanation
        -> dashboard / API consumers

Exact class names, methods, fields, windows, weights, and thresholds must be populated from the repository.

## 2. Z-Score

Canonical definition:

    z = (x - mean) / standardDeviation

Where:

- `x` = current/latest observation
- `mean` = mean of the configured historical/lookback population
- `standardDeviation` = standard deviation calculated over the same intended population

### Sanity Cases

| x | mean | std dev | expected z |
|---:|---:|---:|---:|
| 100 | 100 | 10 | 0 |
| 110 | 100 | 10 | +1 |
| 90 | 100 | 10 | -1 |
| 120 | 100 | 10 | +2 |
| 80 | 100 | 10 | -2 |

### Edge Cases

The repository implementation must be checked and the actual intended behavior documented for:

- standard deviation = 0;
- near-zero standard deviation;
- insufficient observations;
- null/missing data;
- NaN;
- positive/negative Infinity.

Do not change these behaviors during Phase 0 unless they are demonstrably incorrect and the change is explicitly reviewed.

## 3. Z-Score Consumers

Populate after code inspection.

| Consumer / indicator | File / class | How z-score is used | Downstream impact |
|---|---|---|---|
| TBD | TBD | TBD | TBD |

## 4. Indicator Scoring

Populate from the actual implementation.

For each indicator document:

- source data;
- lookback window;
- calculation;
- direction/sign convention;
- normalization;
- clipping/capping;
- weight;
- missing-data behavior.

| Indicator | Calculation | Direction | Weight | Notes |
|---|---|---|---:|---|
| TBD | TBD | TBD | TBD | TBD |

## 5. Composite Score

Populate from code.

Questions to verify:

- Which indicators contribute?
- Are weights normalized?
- Are any values clipped before aggregation?
- Is `tanh` or another nonlinear transformation used?
- What is the final score range?
- What happens when an indicator is unavailable?

No Phase-0 change should be made here merely to compensate for corrected z-scores.

## 6. Classification

Populate existing thresholds and labels from code.

| Score range / rule | Classification |
|---|---|
| TBD | TBD |

Phase 0 rule: **record current thresholds; do not retune them.**

## 7. Phase 0 Validation Notes

Record validation observations here after the z-score fix.

### Before fix
- Formula:
- Representative outputs:
- Unexpected behavior:

### After fix
- Formula:
- Representative outputs:
- Observed score changes:
- Indicators most affected:
- Potential follow-up for later phases:

## 8. Model Change Discipline

Future model changes follow:

    Hypothesis
      -> code
      -> quantitative unit test
      -> historical recomputation
      -> backtest
      -> before/after metrics
      -> accept/reject

AI-generated narrative must never be treated as evidence that a model change is quantitatively correct.
