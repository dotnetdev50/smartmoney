//Api calls are now mocked by static JSON files in the public/data directory. 
// The types below are not currently used, but could be helpful for future development 
// when we switch back to real API calls.
export type ParticipantDto = {
  name: string;
  bias: number;
  label?: string; // optional for JSON mode
};

export type ParticipantActivityRowDto = {
  name: string;
  futures_net: number;
  calls_net: number;
  puts_net: number;
  futures_pct?: number | null;
  calls_pct?: number | null;
  puts_pct?: number | null;
};

export type ParticipantActivityRow = {
  participant: string;   // "FII" | "DII"
  instrument: string;    // "Futures" | "Calls" | "Puts"
  net_oi_change: number;
  vs_yesterday_pct?: number | null;
};

export type NarrativeContribution = {
  name: string;
  contribution: number;
};

export type NarrativeCounts = {
  positive: number;
  negative: number;
  zero: number;
};

export type MarketNarrativeDecomposition = {
  participant_contributions: NarrativeContribution[];
  main_participant_driver?: string | null;
  indicator_contributions: NarrativeContribution[];
  main_indicator_driver?: string | null;
  participant_counts: NarrativeCounts;
  indicator_counts: NarrativeCounts;
  participant_concentration: number;
  participant_alignment: string;
  indicator_alignment: string;
  dii_smart_relationship: string;
  smart_bias: number;
  retail_bias: number;
  dii_bias: number;
  smart_retail_divergence: number;
  smart_dii_divergence: number;
  smart_retail_state: string;
};

export type MarketTodayResponse = {
  index: string;
  date: string;
  final_score: number;
  regime: string;
  shock_score?: number;
  participants: ParticipantDto[];
  explanation?: string;

  // PCR and VIX (added for PCR feature)
  pcr?: number | null;
  vix?: number | null;

  // Extended PCR fields from NSE PR file
  pcr_volume?: number | null;
  banknifty_pcr?: number | null;
  banknifty_pcr_volume?: number | null;

  // Participant activity: FII/DII Futures/Calls/Puts net OI changes
  participant_activity?: ParticipantActivityRow[] | null;

  // Smart vs Retail diagnostics (Phase 4)
  smart_bias?: number | null;
  retail_bias?: number | null;
  dii_bias?: number | null;
  smart_retail_divergence?: number | null;
  smart_dii_divergence?: number | null;
  smart_retail_state?: string | null;

  // Deterministic narrative diagnostics (Phase 5)
  decomposition?: MarketNarrativeDecomposition | null;

  // Optional if you keep the old API contract too
  final_Score?: number;
  bias_Label?: string;
  strength?: string;
  final_score_raw?: number;
};

export type MarketHistoryPoint = {
  date: string;
  final_score: number;
  regime: string;
};

// External-context only: reported tech layoffs from layoffs.fyi.
// Does not participate in SmartMoney scoring, regime, or AI interpretation.
export type LayoffsSummary = {
  year: number;
  employees_laid_off: number;
  companies_with_layoffs: number;
  layoff_events: number;
  retrieved_at_utc: string;
  source: string;
  source_url: string;
};

export type NewsScope = "India" | "Global";
export type NewsImpact = "High" | "Medium" | "Low";
export type NewsSentiment = "Positive" | "Negative" | "Mixed" | "Neutral";
export type NewsCategory = "Geopolitical" | "OilEnergy" | "MonetaryMacro" | "IndiaPolicyRegulation" | "FinancialSystem" | "NaturalDisaster" | "Other";

export type MarketNewsItem = {
  rank: number;
  scope: NewsScope;
  category: NewsCategory;
  impact: NewsImpact;
  sentiment: NewsSentiment;
  headline: string;
  why_it_matters: string;
  source: string;
  published_at_utc: string;
  url: string;
};

export type MarketNewsDocument = {
  generated_at_utc: string;
  lookback_hours: number;
  items: MarketNewsItem[];
};

const JSON_BASE = import.meta.env.VITE_JSON_BASE_URL ?? "/data";

async function jsonGet<T>(file: string): Promise<T> {
  const res = await fetch(`${JSON_BASE}/${file}`);
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return res.json() as Promise<T>;
}

export const api = {
  marketToday: () => jsonGet<MarketTodayResponse>("market_today.json"),
  marketHistory: () => jsonGet<MarketHistoryPoint[]>("market_history_30.json"),
  layoffsSummary: () => jsonGet<LayoffsSummary>("layoffs_summary.json"),
  marketNews: () => jsonGet<MarketNewsDocument>("market_news.json"),
};

// export type ParticipantDto = {
//   name: string;
//   bias: number;
//   label: string;
// };

// export type MarketTodayResponse = {
//   index: string;
//   date: string;
//   asOfDate?: string;
//   dateasof?: string;
//   final_Score: number;
//   bias_Label: string;
//   strength: string;
//   regime: string;
//   shock_Score: number;
//   participants: ParticipantDto[];
//   explanation: string;
// };

// export type MarketHistoryPoint = {
//   date: string;
//   final_score: number;
//   regime: string;
// };

// const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "";

// async function httpGet<T>(path: string): Promise<T> {
//   const res = await fetch(`${BASE_URL}${path}`);
//   if (!res.ok) {
//     const txt = await res.text();
//     throw new Error(txt || `HTTP ${res.status}`);
//   }
//   return res.json() as Promise<T>;
// }

// export const api = {
//   marketToday: () => httpGet<MarketTodayResponse>("/api/market/today"),
//   marketHistory: (days = 30) => httpGet<MarketHistoryPoint[]>(`/api/market/history?days=${days}`),
// };
