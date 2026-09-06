# Quantitative Change Workflow

Use for changes involving z-score mathematics, normalization, 5D/20D windows, participant or indicator weights, participant bias, Smart/Retail or Smart/DII divergence, shock score, regime threshold or blending, tanh/final-score transformation, score classifications that affect model semantics, or new deterministic quantitative factors.

1. State the hypothesis or problem.
2. Classify the change: defect correction, calibration, new factor, or model redesign.
3. Identify the exact current formula and affected outputs.
4. Read `docs/scoring-model.md`, the relevant calculator/service and tests, and `.github/prompts/validate-model-change.prompt.md`.
5. Do not alter unrelated model parameters.
6. Add or update mathematical tests.
7. Implement the smallest change.
8. Run relevant tests.
9. Run a historical/backtest comparison where applicable.
10. Compare score distribution, extreme/saturation behavior, regime frequency, directional accuracy, forward returns, and other existing `BacktestRunner` metrics.
11. Never tune a parameter merely because recent output appears more intuitive.
12. Report evidence and a recommendation.

Current formulas and numeric settings belong in `docs/scoring-model.md`, not this workflow.