---
description: Validate a proposed SmartMoney quantitative-model change without silently retuning the model.
---

Review the proposed SmartMoney quantitative change against the repository implementation and `docs/scoring-model.md`.

Perform the following:

1. Identify the exact formula or model behavior being changed.
2. Show the current implementation.
3. State the mathematically intended behavior.
4. Identify all direct references and downstream consumers.
5. Flag changes to:
   - normalization;
   - lookback windows;
   - indicator direction;
   - clipping/capping;
   - weights;
   - thresholds;
   - final transformation;
   - classifications.
6. Determine the smallest safe code change.
7. Identify tests required.
8. State whether historical persisted scores will eventually require recomputation.
9. State whether backtesting will be required.
10. Do not modify unrelated model parameters to compensate for changed outputs.

For Phase 0 specifically:

- validate z-score as `(x - mean) / standardDeviation`;
- trace its consumers;
- preserve weights and thresholds;
- do not rebuild full historical scores yet;
- do not add runtime AI interpretation.

Return:

- Finding
- Proposed change
- Files affected
- Downstream impact
- Validation required
- Risks / unknowns
- Recommendation
