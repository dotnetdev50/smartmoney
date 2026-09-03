// Isolated external-context fetch utility: layoffs.fyi -> frontend/public/data/layoffs_summary.json
//
// This script is intentionally independent of SmartMoney.Job and the deterministic scoring
// pipeline. It never touches market_today.json, the SQLite database, or any scoring code.
//
// Behavior on failure (network error, layout change, missing year page, bad values):
//   - log a clear warning
//   - leave any existing valid layoffs_summary.json untouched
//   - exit with code 0 so the caller (GitHub Actions build) is never blocked
//
// Usage: node scripts/fetch-layoffs-summary.mjs

import { chromium } from "playwright";
import { mkdir, readFile, rename, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(__dirname, "..");
const OUTPUT_PATH = path.join(REPO_ROOT, "frontend", "public", "data", "layoffs_summary.json");
const SOURCE_NAME = "layoffs.fyi";
const NAV_TIMEOUT_MS = 30_000;
const WAIT_FOR_TEXT_TIMEOUT_MS = 20_000;

function currentYear() {
  return new Date().getUTCFullYear();
}

function buildSourceUrl(year) {
  return `https://layoffs.fyi/${year}-layoffs/`;
}

function parseIntFromCommaNumber(raw) {
  const digitsOnly = raw.replace(/,/g, "").trim();
  const value = Number.parseInt(digitsOnly, 10);
  return Number.isFinite(value) ? value : NaN;
}

/**
 * Extracts the three summary counters from the page's rendered text.
 * Parsing is based on semantic phrases (e.g. "employees laid off") rather than CSS classes,
 * since layoffs.fyi is a generated/obfuscated-class site whose markup can change at any time.
 */
function parseSummaryFromText(text) {
  const employeesMatch = text.match(/([\d,]+)\s*employees\s*laid\s*off/i);
  const companiesMatch = text.match(/([\d,]+)\s*companies\s*(?:w\/|with)\s*layoffs/i);
  const eventsMatch = text.match(/([\d,]+)\s*layoff\s*events/i);

  if (!employeesMatch || !companiesMatch || !eventsMatch) {
    throw new Error(
      `Could not locate all three summary phrases in page text. ` +
        `employees=${!!employeesMatch} companies=${!!companiesMatch} events=${!!eventsMatch}`,
    );
  }

  return {
    employees_laid_off: parseIntFromCommaNumber(employeesMatch[1]),
    companies_with_layoffs: parseIntFromCommaNumber(companiesMatch[1]),
    layoff_events: parseIntFromCommaNumber(eventsMatch[1]),
  };
}

function validateSummary(summary, expectedYear, sourceUrl) {
  const errors = [];

  if (summary.year !== expectedYear) {
    errors.push(`year mismatch: expected ${expectedYear}, got ${summary.year}`);
  }
  if (!(summary.employees_laid_off > 0)) {
    errors.push(`employees_laid_off must be > 0, got ${summary.employees_laid_off}`);
  }
  if (!(summary.companies_with_layoffs > 0)) {
    errors.push(`companies_with_layoffs must be > 0, got ${summary.companies_with_layoffs}`);
  }
  if (!(summary.layoff_events > 0)) {
    errors.push(`layoff_events must be > 0, got ${summary.layoff_events}`);
  }
  if (!/^https:\/\/(www\.)?layoffs\.fyi\//i.test(sourceUrl)) {
    errors.push(`source_url must be an https layoffs.fyi URL, got ${sourceUrl}`);
  }

  if (summary.layoff_events < summary.companies_with_layoffs) {
    // Not necessarily invalid (the source itself may genuinely report it this way),
    // but it's unusual enough to flag for investigation.
    console.warn(
      `[layoffs-fetch] WARNING: layoff_events (${summary.layoff_events}) is less than ` +
        `companies_with_layoffs (${summary.companies_with_layoffs}). Proceeding, but this is unusual.`,
    );
  }

  if (errors.length > 0) {
    throw new Error(`Validation failed: ${errors.join("; ")}`);
  }
}

async function fetchLayoffsSummary(year) {
  const sourceUrl = buildSourceUrl(year);
  const browser = await chromium.launch({ headless: true });

  try {
    const page = await browser.newPage();
    page.setDefaultTimeout(NAV_TIMEOUT_MS);
    await page.goto(sourceUrl, { waitUntil: "domcontentloaded", timeout: NAV_TIMEOUT_MS });

    await page.waitForFunction(
      () => document.body && document.body.innerText.toLowerCase().includes("employees laid off"),
      { timeout: WAIT_FOR_TEXT_TIMEOUT_MS },
    );

    const bodyText = await page.evaluate(() => document.body.innerText);
    const finalUrl = page.url();

    if (!finalUrl.includes(String(year))) {
      throw new Error(
        `Rendered page URL (${finalUrl}) does not reference expected year ${year}; ` +
          `the ${year}-layoffs page may not exist yet.`,
      );
    }

    const parsed = parseSummaryFromText(bodyText);

    const summary = {
      year,
      employees_laid_off: parsed.employees_laid_off,
      companies_with_layoffs: parsed.companies_with_layoffs,
      layoff_events: parsed.layoff_events,
      retrieved_at_utc: new Date().toISOString(),
      source: SOURCE_NAME,
      source_url: sourceUrl,
    };

    validateSummary(summary, year, sourceUrl);
    return summary;
  } finally {
    await browser.close();
  }
}

async function writeJsonAtomically(filePath, data) {
  await mkdir(path.dirname(filePath), { recursive: true });
  const tempPath = `${filePath}.tmp-${process.pid}`;
  await writeFile(tempPath, `${JSON.stringify(data, null, 2)}\n`, "utf8");
  await rename(tempPath, filePath);
}

async function existingFileIsValid(filePath) {
  try {
    const raw = await readFile(filePath, "utf8");
    const json = JSON.parse(raw);
    return (
      typeof json.year === "number" &&
      typeof json.employees_laid_off === "number" &&
      json.employees_laid_off > 0
    );
  } catch {
    return false;
  }
}

async function main() {
  const year = currentYear();
  console.log(`[layoffs-fetch] Fetching current-year (${year}) layoffs summary from ${SOURCE_NAME}...`);

  try {
    const summary = await fetchLayoffsSummary(year);
    await writeJsonAtomically(OUTPUT_PATH, summary);
    console.log(`[layoffs-fetch] Wrote ${OUTPUT_PATH}:`, JSON.stringify(summary));
  } catch (err) {
    console.warn(`[layoffs-fetch] WARNING: failed to fetch/parse layoffs summary: ${err.message}`);
    const hasValidExisting = await existingFileIsValid(OUTPUT_PATH);
    if (hasValidExisting) {
      console.warn(`[layoffs-fetch] Preserving existing valid ${OUTPUT_PATH}.`);
    } else {
      console.warn(
        `[layoffs-fetch] No valid existing ${OUTPUT_PATH} found. Dashboard will show "Unavailable" for this KPI.`,
      );
    }
    // Never fail the build/pipeline for this external-context metric.
    process.exitCode = 0;
    return;
  }
}

main().catch((err) => {
  console.warn(`[layoffs-fetch] WARNING: unexpected error: ${err?.message ?? err}`);
  process.exitCode = 0;
});
