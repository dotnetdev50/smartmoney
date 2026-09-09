import assert from "node:assert/strict";
import test from "node:test";

import { fetchPreciousMetals, fetchQuote, parseSeriesCsv } from "./fetch-precious-metals.mjs";

test("parseSeriesCsv picks the latest usable row", () => {
  const quote = parseSeriesCsv(
    "DATE,VALUE\n2026-09-05,.\n2026-09-06,3350.12\n",
    { symbol: "XAU", name: "Gold", key: "gold", series_ids: ["GOLDAMGBD228NLBM"] },
    "GOLDAMGBD228NLBM",
  );

  assert.equal(quote.price_usd, 3350.12);
  assert.equal(quote.as_of_date, "2026-09-06");
  assert.equal(quote.series_id, "GOLDAMGBD228NLBM");
});

test("fetchQuote falls back to the next configured series id when the first returns 404", async () => {
  const calls = [];
  const originalFetch = global.fetch;

  global.fetch = async (url) => {
    calls.push(String(url));
    if (String(url).includes("SLVRUSD")) {
      return { ok: false, status: 404, text: async () => "" };
    }

    return {
      ok: true,
      status: 200,
      text: async () => "DATE,VALUE\n2026-09-08,38.42\n",
    };
  };

  try {
    const quote = await fetchQuote({
      key: "silver",
      symbol: "XAG",
      name: "Silver",
      series_ids: ["SLVRUSD", "SLVPRUSD"],
    });

    assert.equal(quote.price_usd, 38.42);
    assert.equal(quote.series_id, "SLVPRUSD");
    assert.equal(calls.length, 2);
  } finally {
    global.fetch = originalFetch;
  }
});

test("fetchQuote falls back to the next configured gold series id when the first returns 404", async () => {
  const calls = [];
  const originalFetch = global.fetch;

  global.fetch = async (url) => {
    calls.push(String(url));
    if (String(url).includes("GOLDAMGBD228NLBM")) {
      return { ok: false, status: 404, text: async () => "" };
    }

    return {
      ok: true,
      status: 200,
      text: async () => "DATE,VALUE\n2026-09-08,3348.77\n",
    };
  };

  try {
    const quote = await fetchQuote({
      key: "gold",
      symbol: "XAU",
      name: "Gold",
      series_ids: ["GOLDAMGBD228NLBM", "GOLDPMGBD228NLBM"],
    });

    assert.equal(quote.price_usd, 3348.77);
    assert.equal(quote.series_id, "GOLDPMGBD228NLBM");
    assert.equal(calls.length, 2);
  } finally {
    global.fetch = originalFetch;
  }
});

test("fetchQuote falls back when the first configured series throws a network error", async () => {
  const originalFetch = global.fetch;
  let calls = 0;

  global.fetch = async (url) => {
    calls += 1;
    if (String(url).includes("SLVRUSD")) {
      throw new Error("network down");
    }

    return {
      ok: true,
      status: 200,
      text: async () => "DATE,VALUE\n2026-09-08,38.11\n",
    };
  };

  try {
    const quote = await fetchQuote({
      key: "silver",
      symbol: "XAG",
      name: "Silver",
      series_ids: ["SLVRUSD", "SLVPRUSD"],
    });

    assert.equal(quote.price_usd, 38.11);
    assert.equal(quote.series_id, "SLVPRUSD");
    assert.equal(calls, 2);
  } finally {
    global.fetch = originalFetch;
  }
});

test("fetchPreciousMetals writes whatever metal data is available", async () => {
  const originalFetch = global.fetch;

  global.fetch = async (url) => {
    if (String(url).includes("GOLDAMGBD228NLBM") || String(url).includes("GOLDPMGBD228NLBM")) {
      return { ok: false, status: 404, text: async () => "" };
    }

    return {
      ok: true,
      status: 200,
      text: async () => "DATE,VALUE\n2026-09-08,38.11\n",
    };
  };

  try {
    const { summary, errors } = await fetchPreciousMetals();

    assert.equal(summary.gold, null);
    assert.equal(summary.silver?.price_usd, 38.11);
    assert.equal(summary.silver?.series_id, "SLVRUSD");
    assert.match(errors[0], /GOLDAMGBD228NLBM/);
    assert.match(errors[0], /GOLDPMGBD228NLBM/);
  } finally {
    global.fetch = originalFetch;
  }
});
