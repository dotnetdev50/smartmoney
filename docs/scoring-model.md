# SmartMoney Scoring Model

## Status

This document is the human-readable specification for SmartMoney quantitative scoring.

Current implementation phase: **Phase 0 — z-score correctness and model validation**.

This document should be updated as the actual backend implementation is inspected. Do not invent missing model rules.

## 1. Processing Flow

Current implemented flow (verified from repository code):

        Participant raw data (FuturesChange, PutOiChange, CallOiChange)
                -> per participant rolling windows (Short=5, Long=20)
                -> z-scores for each indicator and window
                -> divergence per participant
                -> ShockScore (participant-weighted divergence sum)
                -> Regime (Shock or Normal)
                -> regime-dependent short/long blend
                -> participant bias
                -> participant-weighted marketRaw
                -> finalScore = tanh(marketRaw / 2.0) * 100.0
                -> API classification label/strength
                -> deterministic explanation
                -> API response and dashboard consumers

Primary implementation location:

- backend/SmartMoney.Application/Services/DailyPipelineService.cs

## 2. Z-Score

Active implementation:

- Method: DailyPipelineService.Z(List<double> values)
- File: backend/SmartMoney.Application/Services/DailyPipelineService.cs
- Formula: z = (latestValue - mean) / std
- mean = Average(values)
- variance (population) = Sum((v - mean)^2) / values.Count
- std = Sqrt(variance)
- epsilon = 1e-8
- if Abs(std) < epsilon, return 0

Active windows used to create z-score inputs:

- Short window: 5
- Long window: 20

Active z-score indicators:

- FuturesChange
- PutOiChange
- CallOiChange

Participant processing currently observed:

- FII
- Pro
- DII
- Retail

### Sanity Cases

Controlled validation cases (same-list mean/std, latest value = last element):

| Case | Input list | Mean | Std dev | Expected z |
|---|---|---:|---:|---:|
| Constant values | [5, 5, 5, 5, 5] | 5.00000000 | 0.00000000 | 0.00000000 |
| Increasing around 100 | [90, 95, 100, 105, 110] | 100.00000000 | 7.07106781 | 1.41421356 |
| Decreasing around 100 | [110, 105, 100, 95, 90] | 100.00000000 | 7.07106781 | -1.41421356 |
| Increasing 1..10 | [1, 2, 3, 4, 5, 6, 7, 8, 9, 10] | 5.50000000 | 2.87228132 | 1.56669890 |
| Decreasing 10..1 | [10, 9, 8, 7, 6, 5, 4, 3, 2, 1] | 5.50000000 | 2.87228132 | -1.56669890 |

### Edge Cases

Verified behavior in active path:

- standard deviation = 0: returns 0 because Abs(std) < epsilon
- near-zero standard deviation: returns 0 because epsilon check uses 1e-8
- insufficient observations: participant skipped unless 20 rows available; if all skipped, no metrics produced for that date
- null/missing data: raw fields are non-nullable doubles in entities; no additional null guard in z method
- NaN: no explicit handling in z method
- positive/negative Infinity: no explicit handling in z method

Do not change these behaviors during Phase 0 unless they are demonstrably incorrect and the change is explicitly reviewed.

## 3. Z-Score Consumers

| Consumer / indicator | File / class | How z-score is used | Downstream impact |
|---|---|---|---|
| FuturesChange short/long | backend/SmartMoney.Application/Services/DailyPipelineService.cs (DailyPipelineService) | ComputeZShortLong -> Z(shortVals), Z(longVals) | Contributes to divergence, regime, participant bias, market raw and final score |
| PutOiChange short/long | backend/SmartMoney.Application/Services/DailyPipelineService.cs (DailyPipelineService) | ComputeZShortLong -> Z(shortVals), Z(longVals) | Contributes to divergence, regime, participant bias, market raw and final score |
| CallOiChange short/long | backend/SmartMoney.Application/Services/DailyPipelineService.cs (DailyPipelineService) | ComputeZShortLong -> Z(shortVals), Z(longVals) | Contributes to divergence, regime, participant bias, market raw and final score |

## 4. Indicator Scoring

| Indicator | Calculation | Direction | Weight | Notes |
|---|---|---|---:|---|
| Futures | z short/long, regime blend, then +0.5 contribution to participant bias | Positive z is bullish | 0.5 in participant bias | Uses FuturesChange; no explicit clipping of z in active pipeline |
| Puts | z short/long, regime blend, then +0.3 contribution to participant bias | Positive z is bullish (put writing proxy) | 0.3 in participant bias | Uses PutOiChange |
| Calls | z short/long, regime blend, then -0.2 contribution to participant bias | Positive z is bearish (call writing proxy), implemented by subtraction | 0.2 in participant bias (subtracted) | Uses CallOiChange |

Regime-dependent blend used per indicator:

- Shock: 0.7 * shortZ + 0.3 * longZ
- Normal: 0.7 * longZ + 0.3 * shortZ

Participant bias formula:

- participantBias = 0.5 * futuresEff + 0.3 * putEff - 0.2 * callEff

Shock/divergence formula:

- divergence = Abs(futuresShort - futuresLong) + Abs(putShort - putLong) + Abs(callShort - callLong)
- ShockScore = Sum over participants of ParticipantWeight(participant) * divergence

Regime rule:

- Shock when ShockScore > 1.5
- otherwise Normal

## 5. Composite Score

Composite participant weights (for both ShockScore weighting and marketRaw aggregation):

- FII = 0.4
- Pro = 0.3
- DII = 0.2
- Retail = 0.1

marketRaw aggregation:

- marketRaw = Sum over participants of ParticipantWeight(participant) * participantBias

Final transformation:

- finalScore = tanh(marketRaw / 2.0) * 100.0

Observed implications:

- tanh bounds output to approximately [-100, +100]
- no separate clipping step is present in active pipeline before tanh
- if a participant has insufficient history, it is skipped for that date

No Phase-0 change should be made here merely to compensate for corrected z-scores.

## 6. Classification

Canonical Job-generated frontend output uses `MarketNarrative.ScoreLabel`:

| Score range / rule | Job presentation |
|---|---|
| abs(finalScore) < 20 | Strength = Neutral |
| abs(finalScore) >= 20 and < 40 | Strength = Mild |
| abs(finalScore) >= 40 and < 70 | Strength = Moderate |
| abs(finalScore) >= 70 | Strength = Strong |
| finalScore >= 40 | Bias label = Bullish |
| finalScore <= -40 | Bias label = Bearish |
| finalScore > -40 and < 40 | Bias label = Neutral |

The user-facing narrative direction uses the canonical Job bias label: scores at or above 40 are displayed as Bullish, scores at or below -40 are displayed as Bearish, and scores between those boundaries are displayed as Neutral. Raw positive, negative, or zero sign remains a quantitative diagnostic for decomposition, comparisons, and backtesting; it is not the displayed user-facing bias label.

The previously documented 15 / 35 / 60 boundaries and Weak / Mild / Moderate / Strong names belong to the non-canonical legacy/API presentation logic. They are not used by the Job-generated `market_today.json` frontend output.

Phase 0 rule: **record current thresholds; do not retune them.**

Additional participant label logic in active API presentation path (DescribeParticipant):

- abs(participantBias) < 0.5: Neutral
- abs(participantBias) >= 0.5 and < 1.2: Mild Bullish or Mild Bearish
- abs(participantBias) >= 1.2 and < 2.5: Bullish or Bearish
- abs(participantBias) >= 2.5: Strong Bullish or Strong Bearish

## 7. Phase 0 Validation Notes

Record validation observations here after the z-score fix.

### Before fix
- Active z formula behavior was incorrect in DailyPipelineService.Z before correction.
- Representative persisted values for 2026-02-27 (before correction run):
    - Regime: Shock
    - ShockScore: 33.4311
    - FinalScore: +78.8820
    - DII CallZShort example: -153.6765
- Unexpected behavior:
    - Extremely large z magnitudes were observed (example above), indicating distorted normalization.

### After fix
- Formula (active): z = (latestValue - mean) / std, with std from population variance and epsilon guard (1e-8).
- Controlled sanity results matched expected values exactly for constant/increasing/decreasing test lists.
- Representative persisted values for 2026-02-27 after corrected run:
    - Regime: Normal
    - ShockScore: 1.2557285699
    - RawBias: -0.5844066572
    - FinalScore: -28.4161493062
    - Classification: Bearish / Mild
    - DII CallZShort: 0.5000
- Observed score changes:
    - Regime flipped from Shock to Normal.
    - FinalScore moved from strongly bullish positive to mild bearish negative.
- Indicators most affected:
    - Call z-score values showed material correction impact (example: DII CallZShort).
- Potential follow-up for later phases:
    - Previous persisted scores are not reliable for model evaluation until historical recomputation is performed in Phase 2.

Legacy/non-primary normalization path note:

- backend/SmartMoney.Application/Services/NormalizationService.cs (Normalize)
- backend/SmartMoney.Application/Services/BiasEngine.cs (BiasEngineService)
- Repository analysis found no active runtime caller beyond DI registration during current Phase 0 flow inspection.
- This path is documented as non-primary/legacy in current flow, but not classified as dead code.

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

## 9. Quantitative Test Coverage

Current automated quantitative unit-test coverage includes:

- Z-score:
    - canonical behavior for positive and negative z values;
    - near-zero standard deviation returning 0;
    - finite-output invariant (no NaN/Infinity for representative finite inputs).
- 5/20 windows:
    - short z uses only last 5 values;
    - long z uses last 20 values;
    - values older than last 20 are ignored when input length is greater than 20.
- Caller contract note:
    - production pipeline enforces at least 20 observations before invoking short/long scoring path.
- Regime boundary:
    - ShockScore = 1.5 is Normal;
    - ShockScore > 1.5 is Shock.
- Blending:
    - Normal uses 0.7 * long + 0.3 * short;
    - Shock uses 0.7 * short + 0.3 * long.
- Participant bias:
    - 0.5 * futures + 0.3 * puts - 0.2 * calls;
    - sign/zero/mixed-input behavior covered.
- Participant weights:
    - FII=0.4, Pro=0.3, DII=0.2, Retail=0.1;
    - invariant that weights sum to 1.0;
    - unknown participant behavior returns zero weight.
- Raw market aggregation:
    - weighted participant-bias aggregation behavior, including mixed/negative and unknown-participant entries.
- tanh final score:
    - formula, symmetry, monotonicity, bounded/saturating behavior under double precision,
    - finite-output invariant for representative valid raw values.

Current quantitative unit-test run status:

- total tests: 67
- passed: 67
- failed: 0
- skipped: 0
