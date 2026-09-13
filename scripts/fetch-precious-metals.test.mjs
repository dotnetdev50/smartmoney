import assert from "node:assert/strict";
import test from "node:test";

import { fetchPreciousMetals, fetchQuote, parseSourceQuote } from "./fetch-precious-metals.mjs";

test("parseSourceQuote picks the configured metal price from the source payload", () => {
  const quote = parseSourceQuote(
    {
      date: "2026-09-06T15:30:00Z",
      items: [{ curr: "USD", xauPrice: 3350.12, xagPrice: 38.42 }],
    },
    { symbol: "XAU", name: "Gold", key: "gold", series_ids: ["xauPrice"] },
  );

  assert.equal(quote.price_usd, 3350.12);
  assert.equal(quote.as_of_date, "2026-09-06");
  assert.equal(quote.series_id, "xauPrice");
});

test("fetchQuote reads silver from GoldPrice.org payload", async () => {
  const originalFetch = global.fetch;

  global.fetch = async () => {
    return {
      ok: true,
      status: 200,
      json: async () => ({
        date: "2026-09-08T15:30:00Z",
        items: [{ curr: "USD", xauPrice: 3348.77, xagPrice: 38.42 }],
      }),
    };
  };

  try {
    const quote = await fetchQuote({
      key: "silver",
      symbol: "XAG",
      name: "Silver",
      series_ids: ["xagPrice"],
    });

    assert.equal(quote.price_usd, 38.42);
    assert.equal(quote.series_id, "xagPrice");
  } finally {
    global.fetch = originalFetch;
  }
});

test("fetchQuote surfaces source HTTP failures", async () => {
  const originalFetch = global.fetch;

  global.fetch = async () => ({ ok: false, status: 503, json: async () => ({}) });

  try {
    await assert.rejects(
      () =>
        fetchQuote({
          key: "silver",
          symbol: "XAG",
          name: "Silver",
          series_ids: ["xagPrice"],
        }),
      /xagPrice request failed with HTTP 503/,
    );
  } finally {
    global.fetch = originalFetch;
  }
});

test("fetchPreciousMetals writes whatever metal data is available", async () => {
  const originalFetch = global.fetch;

  global.fetch = async () => {
    return {
      ok: true,
      status: 200,
      json: async () => ({
        date: "2026-09-08T15:30:00Z",
        items: [{ curr: "USD", xagPrice: 38.11 }],
      }),
    };
  };

  try {
    const { summary, errors } = await fetchPreciousMetals();
    assert.equal(summary.gold, null);
    assert.equal(summary.silver?.price_usd, 38.11);
    assert.equal(summary.silver?.price_usd, 38.11);
    assert.equal(summary.silver?.series_id, "xagPrice");
    assert.match(errors[0], /xauPrice was missing from source payload/);
  } finally {
    global.fetch = originalFetch;
  }
});
