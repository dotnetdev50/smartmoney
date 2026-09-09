// Isolated external-context fetch utility: public daily prices -> frontend/public/data/precious_metals.json
//
// This script is intentionally independent of SmartMoney.Job and the deterministic scoring
// pipeline. It never touches market_today.json, the SQLite database, or any scoring code.
// Source strategy: GoldPrice.org primary, FRED LBMA series fallback per metal.
//
// Behavior on failure (network error, source change, missing values, bad payload):
//   - log a clear warning
//   - leave any existing valid precious_metals.json untouched
//   - exit with code 0 so the caller (GitHub Actions build) is never blocked
//
// Usage: node scripts/fetch-precious-metals.mjs

import { mkdir, readFile, rename, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(__dirname, "..");
const OUTPUT_PATH = path.join(REPO_ROOT, "frontend", "public", "data", "precious_metals.json");
const SOURCE_NAME = "GoldPrice.org";
const SOURCE_URL = "https://data-asg.goldprice.org/dbXRates/USD";
const FRED_SOURCE_NAME = "FRED";
const FRED_SOURCE_URL = "https://fred.stlouisfed.org/graph/fredgraph.csv";

const SERIES = [
  {
    key: "gold",
    symbol: "XAU",
    name: "Gold",
    series_ids: ["xauPrice"],
    fallback_series_ids: ["GOLDPMGBD228NLBM"],
  },
  {
    key: "silver",
    symbol: "XAG",
    name: "Silver",
    series_ids: ["xagPrice"],
    fallback_series_ids: ["SLVRUSD", "SLVPRUSD"],
  },
];

function normalizeAsOfDate(rawDate) {
  if (typeof rawDate !== "string" || rawDate.trim() === "") {
    throw new Error(`source response did not include a usable date: ${rawDate}`);
  }

  const parsed = new Date(rawDate);
  if (Number.isNaN(parsed.getTime())) {
    throw new Error(`source response date was not parseable: ${rawDate}`);
  }

  return parsed.toISOString().slice(0, 10);
}

export function parseSourceQuote(payload, series, seriesId = series.series_ids[0]) {
  const item = payload?.items?.[0];
  if (!item || typeof item !== "object") {
    throw new Error("source payload did not contain items[0].");
  }

  const rawValue = item?.[seriesId];
  if (rawValue == null || rawValue === "") {
    throw new Error(`${seriesId} was missing from source payload.`);
  }

  const price = Number.parseFloat(String(rawValue));
  if (!Number.isFinite(price) || price <= 0) {
    throw new Error(`${seriesId} contains invalid latest price "${rawValue}".`);
  }

  return {
    symbol: series.symbol,
    name: series.name,
    price_usd: price,
    unit: "USD/troy ounce",
    as_of_date: normalizeAsOfDate(payload?.date),
    series_id: seriesId,
    series_url: SOURCE_URL,
  };
}

function validateQuote(quote, expected) {
  const errors = [];

  if (quote.symbol !== expected.symbol) {
    errors.push(`symbol mismatch: expected ${expected.symbol}, got ${quote.symbol}`);
  }
  if (quote.name !== expected.name) {
    errors.push(`name mismatch: expected ${expected.name}, got ${quote.name}`);
  }
  if (!(quote.price_usd > 0)) {
    errors.push(`price_usd must be > 0, got ${quote.price_usd}`);
  }
  if (!/^\d{4}-\d{2}-\d{2}$/.test(quote.as_of_date)) {
    errors.push(`as_of_date must be YYYY-MM-DD, got ${quote.as_of_date}`);
  }
  const expectedSeriesIds = [...expected.series_ids, ...(expected.fallback_series_ids ?? [])];
  if (!expectedSeriesIds.includes(quote.series_id)) {
    errors.push(`series_id mismatch: expected one of ${expectedSeriesIds.join(", ")}, got ${quote.series_id}`);
  }
  const expectedUrls = [SOURCE_URL, FRED_SOURCE_URL];
  if (!expectedUrls.includes(quote.series_url)) {
    errors.push(`series_url mismatch: expected one of ${expectedUrls.join(", ")}, got ${quote.series_url}`);
  }

  if (errors.length > 0) {
    throw new Error(`Validation failed for ${expected.key}: ${errors.join("; ")}`);
  }
}

export async function fetchQuote(series) {
  const primaryError = await (async () => {
    try {
      const response = await fetch(SOURCE_URL, {
        headers: {
          "user-agent": "SmartMoney/1.0 (+https://github.com/dotnetdev50/smartmoney)",
          accept: "application/json,text/plain;q=0.9,*/*;q=0.8",
        },
      });

      if (!response.ok) {
        throw new Error(`${series.series_ids[0]} request failed with HTTP ${response.status}.`);
      }

      const payload = await response.json();
      const quote = parseSourceQuote(payload, series);
      validateQuote(quote, series);
      return quote;
    } catch (err) {
      return err;
    }
  })();

  if (!(primaryError instanceof Error)) {
    return primaryError;
  }

  const fallbackErrors = [];
  for (const seriesId of series.fallback_series_ids ?? []) {
    try {
      const fallbackUrl = `${FRED_SOURCE_URL}?id=${seriesId}`;
      const response = await fetch(fallbackUrl, {
        headers: {
          "user-agent": "SmartMoney/1.0 (+https://github.com/dotnetdev50/smartmoney)",
          accept: "text/csv,text/plain;q=0.9,*/*;q=0.8",
        },
      });
      if (!response.ok) {
        throw new Error(`${seriesId} request failed with HTTP ${response.status}.`);
      }

      const csv = await response.text();
      const quote = parseFredQuote(csv, series, seriesId);
      validateQuote(quote, series);
      return quote;
    } catch (err) {
      fallbackErrors.push(err?.message ?? String(err));
    }
  }

  const fallbackMessage = fallbackErrors.length > 0 ? ` Fallback ${FRED_SOURCE_NAME} failed: ${fallbackErrors.join(" ")}` : "";
  throw new Error(`${primaryError.message}${fallbackMessage}`);
}

function parseFredQuote(csv, series, seriesId) {
  if (typeof csv !== "string" || csv.trim() === "") {
    throw new Error(`${seriesId} returned an empty CSV payload.`);
  }

  const lines = csv.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
  if (lines.length < 2) {
    throw new Error(`${seriesId} CSV payload did not contain data rows.`);
  }

  for (let i = lines.length - 1; i >= 1; i -= 1) {
    const [rawDate, rawValue] = lines[i].split(",");
    if (!rawDate || !rawValue || rawValue === "." || rawValue === "") {
      continue;
    }

    const price = Number.parseFloat(rawValue);
    if (!Number.isFinite(price) || price <= 0) {
      continue;
    }

    return {
      symbol: series.symbol,
      name: series.name,
      price_usd: price,
      unit: "USD/troy ounce",
      as_of_date: normalizeAsOfDate(rawDate),
      series_id: seriesId,
      series_url: FRED_SOURCE_URL,
    };
  }

  throw new Error(`${seriesId} CSV payload did not contain a usable latest value.`);
}

function getValidExistingQuote(summary, series) {
  const quote = summary?.[series.key];
  if (!quote) return null;

  try {
    validateQuote(quote, series);
    return quote;
  } catch {
    return null;
  }
}

function summaryHasAnyQuote(summary) {
  return SERIES.some((series) => getValidExistingQuote(summary, series));
}

async function readExistingSummary(filePath) {
  try {
    const raw = await readFile(filePath, "utf8");
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

export async function fetchPreciousMetals(existingSummary = null) {
  const results = await Promise.allSettled(SERIES.map((series) => fetchQuote(series)));
  const errors = [];
  const summary = {
    retrieved_at_utc: new Date().toISOString(),
    source: SOURCE_NAME,
    source_url: SOURCE_URL,
    gold: null,
    silver: null,
  };

  results.forEach((result, index) => {
    const series = SERIES[index];
    if (result.status === "fulfilled") {
      summary[series.key] = result.value;
      return;
    }

    const message = result.reason?.message ?? String(result.reason);
    errors.push(`${series.key}: ${message}`);
    summary[series.key] = getValidExistingQuote(existingSummary, series);
  });

  if (!summaryHasAnyQuote(summary)) {
    throw new Error(errors.join(" ") || "No precious metals quotes available.");
  }

  return { summary, errors };
}

async function writeJsonAtomically(filePath, data) {
  await mkdir(path.dirname(filePath), { recursive: true });
  const tempPath = `${filePath}.tmp-${process.pid}`;
  await writeFile(tempPath, `${JSON.stringify(data, null, 2)}\n`, "utf8");
  await rename(tempPath, filePath);
}

async function existingFileIsValid(filePath) {
  const summary = await readExistingSummary(filePath);
  return summaryHasAnyQuote(summary);
}

async function main() {
  console.log(`[precious-metals] Fetching gold and silver daily prices from ${SOURCE_NAME}...`);
  const existingSummary = await readExistingSummary(OUTPUT_PATH);

  try {
    const { summary, errors } = await fetchPreciousMetals(existingSummary);
    await writeJsonAtomically(OUTPUT_PATH, summary);
    if (errors.length > 0) {
      console.warn(`[precious-metals] WARNING: partial refresh completed: ${errors.join(" ")}`);
    }
    console.log(`[precious-metals] Wrote ${OUTPUT_PATH}:`, JSON.stringify(summary));
  } catch (err) {
    console.warn(`[precious-metals] WARNING: failed to fetch/parse precious metals data: ${err.message}`);
    const hasValidExisting = await existingFileIsValid(OUTPUT_PATH);
    if (hasValidExisting) {
      console.warn(`[precious-metals] Preserving existing valid ${OUTPUT_PATH}.`);
    } else {
      console.warn(
        `[precious-metals] No valid existing ${OUTPUT_PATH} found. Dashboard will show "Unavailable" for these KPIs.`,
      );
    }
    process.exitCode = 0;
    return;
  }
}

if (process.argv[1] && pathToFileURL(process.argv[1]).href === import.meta.url) {
  main().catch((err) => {
    console.warn(`[precious-metals] WARNING: unexpected error: ${err?.message ?? err}`);
    process.exitCode = 0;
  });
}
