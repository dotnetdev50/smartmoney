import assert from "node:assert/strict";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import test from "node:test";

import { parseAlphaVantageExchangeRate, parseAlphaVantageQuote, run } from "./fetch-precious-metals.mjs";

const gold = { key: "gold", symbol: "GOLD", quoteSymbol: "XAU", name: "Gold" };

function response(payload, { ok = true, status = 200 } = {}) {
  return { ok, status, json: async () => payload };
}

function validExistingSummary() {
  return {
    retrieved_at_utc: "2026-09-10T12:00:00.000Z",
    source: "Previous Provider",
    source_url: "https://example.test/metals",
    usd_inr_rate: 83.5,
    usd_inr_as_of: "2026-09-10",
    gold: {
      symbol: "XAU",
      name: "Gold",
      price_usd: 3500.25,
      price_inr_10g: 939000.25,
      unit: "USD/troy ounce",
      as_of_date: "2026-09-10",
      series_id: "legacy-gold",
      series_url: "https://example.test/gold",
    },
    silver: {
      symbol: "XAG",
      name: "Silver",
      price_usd: 42.5,
      price_inr_10g: 11405.5,
      unit: "USD/troy ounce",
      as_of_date: "2026-09-10",
      series_id: "legacy-silver",
      series_url: "https://example.test/silver",
    },
  };
}

async function withTempOutput(callback) {
  const directory = await mkdtemp(path.join(os.tmpdir(), "smartmoney-metals-"));
  const outputPath = path.join(directory, "precious_metals.json");
  try {
    await callback(outputPath);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
}

test("parseAlphaVantageQuote preserves the frontend quote contract", () => {
  const quote = parseAlphaVantageQuote(
    { nominal: "XAUUSD", timestamp: "2026-09-12 15:30:00", price: "3651.42" },
    gold,
  );

  assert.deepEqual(quote, {
    symbol: "XAU",
    name: "Gold",
    price_usd: 3651.42,
    unit: "USD/troy ounce",
    as_of_date: "2026-09-12",
    series_id: "GOLD",
    series_url: "https://www.alphavantage.co/documentation/#gold-silver-spot",
  });
});

test("parseAlphaVantageQuote rejects provider errors, rate limits, and invalid prices", () => {
  assert.throws(() => parseAlphaVantageQuote({ "Error Message": "bad request" }, gold), /Error Message/);
  assert.throws(() => parseAlphaVantageQuote({ Note: "rate limit reached" }, gold), /Note/);
  assert.throws(() => parseAlphaVantageQuote({ Information: "premium endpoint" }, gold), /Information/);
  assert.throws(
    () => parseAlphaVantageQuote({ nominal: "XAUUSD", timestamp: "2026-09-12 15:30:00" }, gold),
    /numeric price/,
  );
  assert.throws(
    () => parseAlphaVantageQuote({ nominal: "XAUUSD", timestamp: "2026-09-12 15:30:00", price: "not-a-number" }, gold),
    /invalid price/,
  );
  assert.throws(
    () => parseAlphaVantageQuote({ nominal: "XAUUSD", timestamp: "2026-09-12 15:30:00", price: "0" }, gold),
    /invalid price/,
  );
  assert.throws(
    () => parseAlphaVantageQuote({ nominal: "XAUUSD", timestamp: "2026-09-12 15:30:00", price: "-1" }, gold),
    /invalid price/,
  );
});

test("parseAlphaVantageExchangeRate validates the USD/INR quote", () => {
  const payload = {
    "Realtime Currency Exchange Rate": {
      "1. From_Currency Code": "USD",
      "3. To_Currency Code": "INR",
      "5. Exchange Rate": "83.5",
      "6. Last Refreshed": "2026-09-12 15:30:00",
    },
  };

  assert.deepEqual(parseAlphaVantageExchangeRate(payload), { rate: 83.5, asOfDate: "2026-09-12" });
  assert.throws(
    () => parseAlphaVantageExchangeRate({ Information: "rate limited" }),
    /Information/,
  );
  payload["Realtime Currency Exchange Rate"]["5. Exchange Rate"] = "0";
  assert.throws(() => parseAlphaVantageExchangeRate(payload), /invalid exchange rate/);
});

test("run writes fresh gold and silver data with non-breaking metadata", async () => {
  await withTempOutput(async (outputPath) => {
    const requestedSymbols = [];
    const waits = [];
    const fetchImpl = async (url) => {
      assert.equal(url.searchParams.get("apikey"), "test-key");
      const apiFunction = url.searchParams.get("function");
      if (apiFunction === "CURRENCY_EXCHANGE_RATE") {
        assert.equal(url.searchParams.get("from_currency"), "USD");
        assert.equal(url.searchParams.get("to_currency"), "INR");
        requestedSymbols.push("USD/INR");
        return response({
          "Realtime Currency Exchange Rate": {
            "1. From_Currency Code": "USD",
            "3. To_Currency Code": "INR",
            "5. Exchange Rate": "83.5",
            "6. Last Refreshed": "2026-09-12 15:30:00",
          },
        });
      }

      assert.equal(apiFunction, "GOLD_SILVER_SPOT");
      const symbol = url.searchParams.get("symbol");
      requestedSymbols.push(symbol);
      return response({
        nominal: symbol === "GOLD" ? "XAUUSD" : "XAGUSD",
        timestamp: "2026-09-12 15:30:00",
        price: symbol === "GOLD" ? "3651.42" : "42.18",
      });
    };

    const result = await run({
      apiKey: "test-key",
      fetchImpl,
      outputPath,
      now: () => new Date("2026-09-13T08:00:00Z"),
      wait: async (milliseconds) => waits.push(milliseconds),
    });
    const written = JSON.parse(await readFile(outputPath, "utf8"));

    assert.deepEqual(requestedSymbols, ["GOLD", "SILVER", "USD/INR"]);
    assert.deepEqual(waits, [1100, 1100]);
    assert.deepEqual(written, result);
    assert.equal(written.provider, "Alpha Vantage");
    assert.equal(written.asOf, "2026-09-12");
    assert.equal(written.stale, false);
    assert.equal(written.usd_inr_rate, 83.5);
    assert.equal(written.usd_inr_as_of, "2026-09-12");
    assert.equal(written.gold.price_usd, 3651.42);
    assert.equal(written.gold.price_inr_10g, 98025.56);
    assert.equal(written.silver.price_usd, 42.18);
    assert.equal(written.silver.price_inr_10g, 1132.36);
  });
});

test("run rewrites complete last-known-good data as stale when the provider fails", async () => {
  await withTempOutput(async (outputPath) => {
    const existing = validExistingSummary();
    await writeFile(outputPath, JSON.stringify(existing), "utf8");

    const result = await run({
      apiKey: "test-key",
      fetchImpl: async () => response({ Note: "rate limit reached" }),
      outputPath,
    });
    const written = JSON.parse(await readFile(outputPath, "utf8"));

    assert.equal(result.stale, true);
    assert.equal(written.stale, true);
    assert.equal(written.provider, "Previous Provider");
    assert.equal(written.asOf, "2026-09-10");
    assert.equal(written.usd_inr_rate, existing.usd_inr_rate);
    assert.equal(written.usd_inr_as_of, existing.usd_inr_as_of);
    assert.equal(written.gold.price_usd, existing.gold.price_usd);
    assert.equal(written.gold.price_inr_10g, existing.gold.price_inr_10g);
    assert.equal(written.silver.price_usd, existing.silver.price_usd);
    assert.equal(written.silver.price_inr_10g, existing.silver.price_inr_10g);
  });
});

test("run fails without writing when no complete last-known-good data exists", async () => {
  await withTempOutput(async (outputPath) => {
    await assert.rejects(
      () => run({ apiKey: "test-key", fetchImpl: async () => response({ Information: "rate limited" }), outputPath }),
      /Fatal: provider refresh failed and no valid last-known-good data exists/,
    );
    await assert.rejects(() => readFile(outputPath, "utf8"), { code: "ENOENT" });
  });
});
