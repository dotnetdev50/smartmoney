# CONTRIBUTING.md

## Guidelines

This repository separates runtime configuration into two concerns:

- `NseOptions` (library/API configuration)
  - Purpose: contains network endpoints, archive URLs, and other API-specific defaults used by application services (e.g., `FoBhavCopyService`, `OpBhavCopyService`, `PrPcrService`, `VixFetchService`).
  - Where to configure: only in API/hosting projects (backend services, web APIs) or in integration tests that simulate the API host environment.
  - Do NOT set or override `NseOptions` from the batch job project (`SmartMoney.Job`).

- `NseJobOptions` (job configuration)
  - Purpose: contains job scheduling, retry, and runtime flags consumed by `SmartMoney.Job` only (e.g., `StartAtIst`, `PcrVixMaxRetries`, `PcrVixRetryMinutes`).
  - Where to configure: in `SmartMoney.Job`'s `Program.cs` via `services.Configure<NseJobOptions>(...)` or environment variables specifically for the job.

## Standards

- Single Responsibility for config types: Keep `NseOptions` and `NseJobOptions` separate. Do not duplicate values across them.
- Job-only configuration: The job project must only configure `NseJobOptions`. If the job needs to change API endpoints for debugging, do so via environment variables that the API host reads or by running the API/hosting project with overrides; do not put API URLs in `SmartMoney.Job`.
- Centralize NSE endpoint management: The canonical NSE endpoint configuration belongs to the application/library (where `NseOptions` is defined). Any runtime override for endpoints should be done at the application host, not the job.

## How to apply

- In `SmartMoney.Job/Program.cs` configure only `NseJobOptions` and register the typed HttpClients without hardcoding NSE base addresses.
- In hosting projects (API), configure `NseOptions` with the authoritative archive URLs.

## Example (Job)

```text
# Environment variables (for job)
JOB_START_AT_IST=20:30
PCR_VIX_MAX_RETRIES=2
PCR_VIX_RETRY_MINUTES=10
```

## Example (API host)

```text
# Environment variables (for API host)
NSE_FO_BHAV_COPY_BASE_URL=https://nsearchives.nseindia.com/content/historical/DERIVATIVES/
NSE_PR_BASE_URL=https://nsearchives.nseindia.com/content/fo/
NSE_VIX_ARCHIVE_URL=https://nsearchives.nseindia.com/content/indices/hist_vix_data.csv
```

## Rationale

Separating job runtime behavior from API endpoint configuration reduces unexpected coupling, makes the code easier to audit, and prevents accidental divergence of archive URLs across projects.