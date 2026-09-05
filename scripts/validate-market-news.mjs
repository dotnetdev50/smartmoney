import { readFile } from "node:fs/promises";

const scopes = new Set(["India", "Global"]);
const categories = new Set(["Geopolitical", "OilEnergy", "MonetaryMacro", "IndiaPolicyRegulation", "FinancialSystem", "NaturalDisaster", "Other"]);
const impacts = new Set(["High", "Medium", "Low"]);
const sentiments = new Set(["Positive", "Negative", "Mixed", "Neutral"]);

function requireNonEmptyString(value, name) {
  if (typeof value !== "string" || value.trim() === "") {
    throw new Error(`${name} must be a non-empty string`);
  }
}

function validateDate(value, name) {
  requireNonEmptyString(value, name);
  if (Number.isNaN(Date.parse(value))) {
    throw new Error(`${name} must be a valid timestamp`);
  }
}

export function validateMarketNews(document) {
  if (!document || typeof document !== "object") throw new Error("document must be an object");
  validateDate(document.generated_at_utc, "generated_at_utc");
  if (!(Number.isInteger(document.lookback_hours) && document.lookback_hours > 0)) {
    throw new Error("lookback_hours must be a positive integer");
  }
  if (!Array.isArray(document.items) || document.items.length > 5) {
    throw new Error("items must be an array with at most five entries");
  }

  for (const [index, item] of document.items.entries()) {
    if (!(Number.isInteger(item.rank) && item.rank >= 1)) throw new Error(`items[${index}].rank must be at least 1`);
    if (!scopes.has(item.scope)) throw new Error(`items[${index}].scope is invalid`);
    if (!categories.has(item.category)) throw new Error(`items[${index}].category is invalid`);
    if (!impacts.has(item.impact)) throw new Error(`items[${index}].impact is invalid`);
    if (!sentiments.has(item.sentiment)) throw new Error(`items[${index}].sentiment is invalid`);
    requireNonEmptyString(item.headline, `items[${index}].headline`);
    requireNonEmptyString(item.why_it_matters, `items[${index}].why_it_matters`);
    requireNonEmptyString(item.source, `items[${index}].source`);
    validateDate(item.published_at_utc, `items[${index}].published_at_utc`);
    requireNonEmptyString(item.url, `items[${index}].url`);
    if (!item.url.startsWith("https://")) throw new Error(`items[${index}].url must use HTTPS`);
  }
}

export async function validateMarketNewsFile(filePath) {
  const document = JSON.parse(await readFile(filePath, "utf8"));
  validateMarketNews(document);
  return document;
}

if (process.argv[1] === new URL(import.meta.url).pathname) {
  const filePath = process.argv[2];
  if (!filePath) throw new Error("Usage: node validate-market-news.mjs <market_news.json>");
  await validateMarketNewsFile(filePath);
  console.log(`[market-news] Valid document: ${filePath}`);
}