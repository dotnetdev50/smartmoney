// Isolated external-context fetch utility: Alpha Vantage spot prices -> frontend/public/data/precious_metals.json
//
// This script is intentionally independent of SmartMoney.Job and the deterministic scoring
// pipeline. It never touches market_today.json, the SQLite database, or any scoring code.
//
// Provider failures rewrite complete last-known-good data as stale. Without a
// complete prior document, the script exits non-zero and does not create output.
//
// Usage: node scripts/fetch-precious-metals.mjs

import { mkdir, readFile, rename, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(__dirname, "..");
const OUTPUT_PATH = path.join(REPO_ROOT, "frontend", "public", "data", "precious_metals.json");
const PROVIDER = "Alpha Vantage";
const SOURCE_URL = "https://www.alphavantage.co/documentation/#gold-silver-spot";
const FX_SOURCE_URL = "https://www.alphavantage.co/documentation/#currency-exchange";
const API_URL = "https://www.alphavantage.co/query";
const REQUEST_INTERVAL_MS = 1100;
const GRAMS_PER_TROY_OUNCE = 31.1034768;
const DISPLAY_WEIGHT_GRAMS = 10;

const METALS = [
  {
    key: "gold",
    symbol: "GOLD",
    quoteSymbol: "XAU",
    name: "Gold",
  },
  {
    key: "silver",
    symbol: "SILVER",
    quoteSymbol: "XAG",
    name: "Silver",
  },
];

function normalizeAsOfDate(rawDate, symbol) {
  const date = typeof rawDate === "string" ? rawDate.slice(0, 10) : "";
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date)) {
    throw new Error(`${symbol} response did not contain a valid date.`);
  }

  const parsed = new Date(`${date}T00:00:00Z`);
  if (Number.isNaN(parsed.getTime()) || parsed.toISOString().slice(0, 10) !== date) {
    throw new Error(`${symbol} response contained an invalid date.`);
  }

  return date;
}

function providerError(payload, symbol) {
  if (!payload || typeof payload !== "object" || Array.isArray(payload)) {
    return `${symbol} response was not a JSON object.`;
  }

  for (const key of ["Error Message", "Note", "Information"]) {
    if (typeof payload[key] === "string" && payload[key].trim()) {
      return `${symbol} provider response contained ${key}.`;
    }
  }

  return null;
}

export function parseAlphaVantageQuote(payload, metal) {
  const error = providerError(payload, metal.symbol);
  if (error) throw new Error(error);

  const expectedNominal = `${metal.quoteSymbol}USD`;
  if (payload.nominal !== expectedNominal) {
    throw new Error(`${metal.symbol} response contained an unexpected nominal.`);
  }

  const rawValue = payload.price;
  const normalizedValue = typeof rawValue === "string" ? rawValue.trim() : rawValue;
  if ((typeof normalizedValue !== "number" && typeof normalizedValue !== "string") || normalizedValue === "") {
    throw new Error(`${metal.symbol} response did not contain a numeric price.`);
  }

  const price = Number(normalizedValue);
  if (!Number.isFinite(price) || price <= 0) {
    throw new Error(`${metal.symbol} response contained an invalid price.`);
  }

  return {
    symbol: metal.quoteSymbol,
    name: metal.name,
    price_usd: price,
    unit: "USD/troy ounce",
    as_of_date: normalizeAsOfDate(payload.timestamp, metal.symbol),
    series_id: metal.symbol,
    series_url: SOURCE_URL,
  };
}

export function parseAlphaVantageExchangeRate(payload) {
  const error = providerError(payload, "USD/INR");
  if (error) throw new Error(error);

  const quote = payload["Realtime Currency Exchange Rate"];
  if (
    !quote ||
    quote["1. From_Currency Code"] !== "USD" ||
    quote["3. To_Currency Code"] !== "INR"
  ) {
    throw new Error("USD/INR response contained an unexpected currency pair.");
  }

  const rate = Number(quote["5. Exchange Rate"]);
  if (!Number.isFinite(rate) || rate <= 0) {
    throw new Error("USD/INR response contained an invalid exchange rate.");
  }

  return {
    rate,
    asOfDate: normalizeAsOfDate(quote["6. Last Refreshed"], "USD/INR"),
  };
}

export async function fetchQuote(metal, apiKey, fetchImpl = fetch) {
  const url = new URL(API_URL);
  url.searchParams.set("function", "GOLD_SILVER_SPOT");
  url.searchParams.set("symbol", metal.symbol);
  url.searchParams.set("apikey", apiKey);

  const response = await fetchImpl(url, { headers: { accept: "application/json" } });

  if (!response.ok) {
    throw new Error(`${metal.symbol} request failed with HTTP ${response.status}.`);
  }

  let payload;
  try {
    payload = await response.json();
  } catch {
    throw new Error(`${metal.symbol} response was not valid JSON.`);
  }

  return parseAlphaVantageQuote(payload, metal);
}

async function fetchUsdInrRate(apiKey, fetchImpl = fetch) {
  const url = new URL(API_URL);
  url.searchParams.set("function", "CURRENCY_EXCHANGE_RATE");
  url.searchParams.set("from_currency", "USD");
  url.searchParams.set("to_currency", "INR");
  url.searchParams.set("apikey", apiKey);

  const response = await fetchImpl(url, { headers: { accept: "application/json" } });
  if (!response.ok) {
    throw new Error(`USD/INR request failed with HTTP ${response.status}.`);
  }

  let payload;
  try {
    payload = await response.json();
  } catch {
    throw new Error("USD/INR response was not valid JSON.");
  }

  return parseAlphaVantageExchangeRate(payload);
}

function withIndianPrice(quote, usdInrRate) {
  return {
    ...quote,
    price_inr_10g: Number(
      ((quote.price_usd * usdInrRate * DISPLAY_WEIGHT_GRAMS) / GRAMS_PER_TROY_OUNCE).toFixed(2),
    ),
  };
}

function validateExistingQuote(quote, metal) {
  return (
    quote?.symbol === metal.quoteSymbol &&
    quote?.name === metal.name &&
    typeof quote?.price_usd === "number" &&
    Number.isFinite(quote.price_usd) &&
    quote.price_usd > 0 &&
    typeof quote?.price_inr_10g === "number" &&
    Number.isFinite(quote.price_inr_10g) &&
    quote.price_inr_10g > 0 &&
    typeof quote?.unit === "string" &&
    /^\d{4}-\d{2}-\d{2}$/.test(quote?.as_of_date ?? "") &&
    typeof quote?.series_id === "string" &&
    quote.series_id.length > 0 &&
    typeof quote?.series_url === "string" &&
    quote.series_url.length > 0
  );
}

export function isValidSummary(summary) {
  return (
    summary != null &&
    typeof summary === "object" &&
    typeof summary.usd_inr_rate === "number" &&
    Number.isFinite(summary.usd_inr_rate) &&
    summary.usd_inr_rate > 0 &&
    /^\d{4}-\d{2}-\d{2}$/.test(summary.usd_inr_as_of ?? "") &&
    METALS.every((metal) => validateExistingQuote(summary[metal.key], metal))
  );
}

async function readExistingSummary(filePath) {
  try {
    const raw = await readFile(filePath, "utf8");
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

function summaryAsOf(summary) {
  return METALS.map((metal) => summary[metal.key].as_of_date).sort().at(-1);
}

function freshSummary(quotes, exchangeRate, now) {
  const summary = {
    retrieved_at_utc: now().toISOString(),
    source: PROVIDER,
    source_url: SOURCE_URL,
    provider: PROVIDER,
    asOf: null,
    stale: false,
    usd_inr_rate: exchangeRate.rate,
    usd_inr_as_of: exchangeRate.asOfDate,
    fx_source_url: FX_SOURCE_URL,
    gold: withIndianPrice(quotes[0], exchangeRate.rate),
    silver: withIndianPrice(quotes[1], exchangeRate.rate),
  };
  summary.asOf = summaryAsOf(summary);
  return summary;
}

function staleSummary(existing) {
  return {
    ...existing,
    provider: existing.provider ?? existing.source ?? "Unknown",
    asOf: existing.asOf ?? summaryAsOf(existing),
    stale: true,
  };
}

async function writeJsonAtomically(filePath, data) {
  await mkdir(path.dirname(filePath), { recursive: true });
  const tempPath = `${filePath}.tmp-${process.pid}`;
  await writeFile(tempPath, `${JSON.stringify(data, null, 2)}\n`, "utf8");
  await rename(tempPath, filePath);
}

export async function run({
  apiKey = process.env.ALPHA_VANTAGE_API_KEY,
  fetchImpl = fetch,
  outputPath = OUTPUT_PATH,
  now = () => new Date(),
  wait = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds)),
} = {}) {
  const existing = await readExistingSummary(outputPath);

  try {
    if (typeof apiKey !== "string" || !apiKey.trim()) {
      throw new Error("ALPHA_VANTAGE_API_KEY is not configured.");
    }

    const quotes = [];
    for (const metal of METALS) {
      if (quotes.length > 0) await wait(REQUEST_INTERVAL_MS);
      quotes.push(await fetchQuote(metal, apiKey, fetchImpl));
    }
    await wait(REQUEST_INTERVAL_MS);
    const exchangeRate = await fetchUsdInrRate(apiKey, fetchImpl);
    const summary = freshSummary(quotes, exchangeRate, now);
    await writeJsonAtomically(outputPath, summary);
    console.log(`[precious-metals] Fresh ${PROVIDER} gold and silver prices obtained.`);
    return summary;
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    if (isValidSummary(existing)) {
      const summary = staleSummary(existing);
      await writeJsonAtomically(outputPath, summary);
      console.warn(`[precious-metals] Provider refresh failed (${message}) Falling back to last-known-good prices.`);
      return summary;
    }

    throw new Error(`Fatal: provider refresh failed and no valid last-known-good data exists. ${message}`);
  }
}

if (process.argv[1] && pathToFileURL(process.argv[1]).href === import.meta.url) {
  run().catch((error) => {
    console.error(`[precious-metals] ${error instanceof Error ? error.message : String(error)}`);
    process.exitCode = 1;
  });
}
