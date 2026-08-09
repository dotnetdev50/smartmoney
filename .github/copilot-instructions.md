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

## Runtime Architecture

Canonical SmartMoney runtime path:

     Raw/source data
     -> SmartMoney.Job / DailyNseJob
     -> DailyPipelineService
     -> MarketScoringCalculator
     -> persisted state where applicable
     -> job-generated JSON/output files
     -> frontend

Runtime rules:

1. The frontend primarily consumes job-generated output files, such as:
    - market_today.json
    - market_history_30.json
2. The API/controller layer is not the canonical frontend/runtime path.
3. API/AdminController functionality should be treated as debugging, diagnostics, and local/admin utilities unless a task explicitly requires API behavior.
4. For new features, prefer integration with:
    - SmartMoney.Job
    - scoring/domain/application services
    - job DTO/output generation
    - frontend JSON contracts
5. Do not automatically modify MarketController, AdminController, or API response DTOs when implementing dashboard/runtime features.
6. Include API changes only when explicitly requested or when a feature genuinely requires an API consumer.
7. For Phase 4 Smart/Retail divergence, expected runtime path is:
    calculation
    -> job output DTO
    -> generated frontend JSON
    -> dashboard
    Do not include API changes in Phase 4 V1.
8. Preserve the principle that quantitative logic stays deterministic and outside frontend/AI.

## AI Roadmap

Runtime AI capabilities are planned only after the quantitative foundation is validated. Until then, AI is an engineering assistant only.
