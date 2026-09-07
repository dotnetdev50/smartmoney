// Isolated external-context fetch utility: FRED/LBMA daily prices -> frontend/public/data/precious_metals.json
//
// This script is intentionally independent of SmartMoney.Job and the deterministic scoring
// pipeline. It never touches market_today.json, the SQLite database, or any scoring code.
//
// Behavior on failure (network error, source change, missing values, bad payload):
//   - log a clear warning
//   - leave any existing valid precious_metals.json untouched
//   - exit with code 0 so the caller (GitHub Actions build) is never blocked
//
// Usage: node scripts/fetch-precious-metals.mjs

import { mkdir, readFile, rename, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(__dirname, "..");
const OUTPUT_PATH = path.join(REPO_ROOT, "frontend", "public", "data", "precious_metals.json");
const SOURCE_NAME = "FRED (LBMA)";
const SOURCE_URL = "https://fred.stlouisfed.org/";

const SERIES = [
  {
    key: "gold",
    symbol: "XAU",
    name: "Gold",
    series_id: "GOLDAMGBD228NLBM",
    series_url: "https://fred.stlouisfed.org/series/GOLDAMGBD228NLBM",
  },
  {
    key: "silver",
    symbol: "XAG",
    name: "Silver",
    series_id: "SLVPRUSD",
    series_url: "https://fred.stlouisfed.org/series/SLVPRUSD",
  },
];

function csvUrl(seriesId) {
  return `https://fred.stlouisfed.org/graph/fredgraph.csv?id=${seriesId}`;
}

function parseSeriesCsv(csvText, series) {
  const lines = csvText.trim().split(/\r?\n/);

  if (lines.length < 2) {
    throw new Error(`${series.series_id} CSV did not contain any data rows.`);
  }

  for (let index = lines.length - 1; index >= 1; index -= 1) {
    const line = lines[index]?.trim();
    if (!line) continue;

    const commaIndex = line.indexOf(",");
    if (commaIndex <= 0) continue;

    const date = line.slice(0, commaIndex).trim();
    const rawValue = line.slice(commaIndex + 1).trim();

    if (!date || !rawValue || rawValue === ".") continue;

    const price = Number.parseFloat(rawValue);
    if (!Number.isFinite(price) || price <= 0) {
      throw new Error(`${series.series_id} contains invalid latest price "${rawValue}".`);
    }

    return {
      symbol: series.symbol,
      name: series.name,
      price_usd: price,
      unit: "USD/troy ounce",
      as_of_date: date,
      series_id: series.series_id,
      series_url: series.series_url,
    };
  }

  throw new Error(`${series.series_id} had no usable latest data row.`);
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
  if (quote.series_id !== expected.series_id) {
    errors.push(`series_id mismatch: expected ${expected.series_id}, got ${quote.series_id}`);
  }
  if (!quote.series_url.startsWith("https://fred.stlouisfed.org/series/")) {
    errors.push(`series_url must be a FRED series URL, got ${quote.series_url}`);
  }

  if (errors.length > 0) {
    throw new Error(`Validation failed for ${expected.key}: ${errors.join("; ")}`);
  }
}

async function fetchQuote(series) {
  const response = await fetch(csvUrl(series.series_id), {
    headers: {
      "user-agent": "SmartMoney/1.0 (+https://github.com/dotnetdev50/smartmoney)",
      accept: "text/csv,text/plain;q=0.9,*/*;q=0.8",
    },
  });

  if (!response.ok) {
    throw new Error(`${series.series_id} request failed with HTTP ${response.status}.`);
  }

  const csvText = await response.text();
  const quote = parseSeriesCsv(csvText, series);
  validateQuote(quote, series);
  return quote;
}

async function fetchPreciousMetals() {
  const [gold, silver] = await Promise.all(SERIES.map((series) => fetchQuote(series)));

  return {
    retrieved_at_utc: new Date().toISOString(),
    source: SOURCE_NAME,
    source_url: SOURCE_URL,
    gold,
    silver,
  };
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
      typeof json?.retrieved_at_utc === "string" &&
      typeof json?.gold?.price_usd === "number" &&
      json.gold.price_usd > 0 &&
      typeof json?.silver?.price_usd === "number" &&
      json.silver.price_usd > 0
    );
  } catch {
    return false;
  }
}

async function main() {
  console.log("[precious-metals] Fetching gold and silver daily prices from FRED/LBMA...");

  try {
    const summary = await fetchPreciousMetals();
    await writeJsonAtomically(OUTPUT_PATH, summary);
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

main().catch((err) => {
  console.warn(`[precious-metals] WARNING: unexpected error: ${err?.message ?? err}`);
  process.exitCode = 0;
});
