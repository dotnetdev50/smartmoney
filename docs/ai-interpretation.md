# SmartMoney AI Daily Interpretation

## Status

Phase 7 Step 7.2 provides a provider-neutral foundation. It contains no real AI provider, SDK, credential, or network call. The feature is disabled by default.

## Runtime architecture

The canonical path is:

    SmartMoney.Job
    -> deterministic scoring
    -> MarketNarrativeDecomposition
    -> deterministic V2 explanation
    -> MarketInterpretationInputFactory
    -> deterministic fingerprint and reuse check
    -> optional IMarketInterpretationProvider
    -> deterministic MarketInterpretationValidator
    -> optional ai_interpretation in market_today.json

The AI path begins only after all quantitative and presentation values are finalized. It is not referenced by `DailyPipelineService`, `MarketScoringCalculator`, or the decomposition and narrative implementations.

## Responsibility boundary

Deterministic SmartMoney code remains authoritative for FinalScore, RawBias, z-scores, weights, ShockScore, Regime, thresholds, displayed direction and strength, drivers, divergence, alignment, concentration, and backtesting statistics. The deterministic V2 explanation remains the authoritative explanation of model mechanics.

A future AI provider may only add a concise interpretation of the supplied relationships. It must not recalculate, correct, override, or relabel deterministic facts. It must not provide trading recommendations, targets, guaranteed outcomes, forecasts, external facts, or new numeric calculations.

## Input contract

`MarketInterpretationInput` contains only:

- signal date;
- FinalScore, canonical displayed direction, strength, Regime, and ShockScore;
- weighted participant contributions and their deterministic main driver;
- weighted indicator contributions and their deterministic main driver;
- Smart, Retail, and DII biases;
- Smart/Retail and Smart/DII divergence and state;
- participant concentration and participant/indicator alignment;
- the DII/Smart relationship;
- the deterministic V2 explanation.

It excludes z-scores, raw positions, weights, PCR, VIX, history, news, macro data, and backtesting metrics. The input factory copies finalized values without rounding or deriving new model facts. Canonical displayed direction comes from the Job's `MarketNarrative.ScoreLabel` result.

## Output contract

A successful validated result contains:

- `summary` (required);
- `key_observation` (required);
- `uncertainty` (required);
- `context` (optional).

Each field is limited to 400 characters and all prose together to 1000 characters. Failure states carry status/provenance only; rejected provider prose is not exported.

Statuses are `generated`, `reused`, `unavailable`, and `invalid`. When disabled, `ai_interpretation` is omitted from serialized `market_today.json`.

## Failure behavior

Provider unavailability, cancellation, timeout, provider exceptions, missing/malformed output, and validation failures are converted into nonthrowing attempts. The deterministic explanation and normal Job outputs are preserved. No fabricated fallback prose is created. AI failure does not change Job success or scoring behavior.

## Fingerprint and reuse

The fingerprint is SHA-256 over canonical UTF-8 JSON with fixed property order and invariant JSON number formatting. It includes:

- input and output schema versions;
- every interpretation input field;
- prompt version and prompt-content SHA-256;
- provider and model/deployment identifiers;
- response-affecting generation settings sorted by ordinal key.

It excludes credentials, generated timestamps, timeout, and retry configuration.

V1 reuse reads the existing same-date `market_today.json`. Reuse requires an exact fingerprint match, a prior `generated` or `reused` status, and successful current validation of the cached prose. The original `generated_at` value is preserved. There is no database, distributed cache, or new persistence table.

## Configuration

The Job supports these environment variables:

| Variable | Default | Purpose |
|---|---|---|
| `AI_INTERPRETATION_ENABLED` | `false` | Completely enables/disables interpretation |
| `AI_INTERPRETATION_PROVIDER` | `disabled` | Provider identifier used in the fingerprint |
| `AI_INTERPRETATION_MODEL` | `none` | Model/deployment identifier used in the fingerprint |
| `AI_INTERPRETATION_ENDPOINT` | unset | Future provider endpoint |
| `AI_INTERPRETATION_API_CREDENTIAL_ENVIRONMENT_VARIABLE` | unset | Name/reference of a future credential environment variable, never the secret value |
| `AI_INTERPRETATION_TIMEOUT_SECONDS` | `15` | Bounded provider timeout; excluded from fingerprint |
| `AI_INTERPRETATION_PROMPT_VERSION` | `market-daily-v1` | Repository-controlled prompt identifier |

No provider-specific implementation currently consumes endpoint or credential configuration. Secrets must never be committed, logged, fingerprinted, or written to output JSON.

## Validation and safety rules

The deterministic validator rejects:

- missing or blank required fields;
- per-field or combined length violations;
- explicit buy, sell, hold, price-target, guarantee, or imperative trading language;
- unsupported prediction/forecast language;
- numeric claims in V1 prose;
- Bullish/Bearish/Neutral mentions contradicting the canonical displayed direction;
- Shock/Normal mentions contradicting the deterministic Regime;
- largest/main/dominant participant or indicator claims contradicting deterministic drivers;
- obvious references to news, earnings, policy announcements, or economic events.

Ordinary invalid content returns structured validation errors and does not throw.

## JSON contract

`market_today.json` may contain:

```json
{
  "ai_interpretation": {
    "status": "generated",
    "prompt_version": "market-daily-v1",
    "input_fingerprint": "sha256:...",
    "generated_at": "2026-08-18T12:34:56+00:00",
    "summary": "...",
    "key_observation": "...",
    "uncertainty": "...",
    "context": null
  }
}
```

The property is additive and nullable. AI data is not added to `market_history_30.json`, API/controller contracts, or the frontend.

## Runtime prompt

The V1 prompt identifier is `market-daily-v1`. Its repository-controlled source is:

    backend/SmartMoney.Job/AI/Prompts/market-daily-interpretation-v1.md

The Job project copies it into the runtime output directory.

## Test matrix

Automated tests cover exact factory mapping; stable and invalidated fingerprints; timeout exclusion; disabled behavior; valid, missing, oversized, advisory, numeric, predictive, external, classification-conflicting, and driver-conflicting output; provider failure; malformed output; reuse and invalidation; generation timestamp preservation; and invariants that deterministic narrative and scoring values remain unchanged.
