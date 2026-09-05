<script setup lang="ts">
import type { MarketNewsDocument, MarketNewsItem } from "@/services/api";

defineProps<{
  document: MarketNewsDocument | null;
  unavailable: boolean;
}>();

const categoryLabels: Record<MarketNewsItem["category"], string> = {
  IndiaPolicyRegulation: "Policy / Regulation",
  MonetaryMacro: "Monetary / Macro",
  OilEnergy: "Oil / Energy",
  FinancialSystem: "Financial System",
  NaturalDisaster: "Natural Disaster",
  Geopolitical: "Geopolitical",
  Other: "Other",
};

function categoryLabel(category: MarketNewsItem["category"]) {
  return categoryLabels[category];
}

function formatPublishedAt(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString(undefined, { month: "short", day: "numeric", year: "numeric", hour: "numeric", minute: "2-digit" });
}

function impactClass(impact: MarketNewsItem["impact"]) {
  return impact === "High"
    ? "border-amber-300 bg-amber-50 text-amber-800 dark:border-amber-500/40 dark:bg-amber-500/10 dark:text-amber-300"
    : impact === "Medium"
      ? "border-sky-300 bg-sky-50 text-sky-800 dark:border-sky-500/40 dark:bg-sky-500/10 dark:text-sky-300"
      : "border-gray-300 bg-gray-50 text-gray-700 dark:border-gray-700 dark:bg-gray-800 dark:text-gray-300";
}
</script>

<template>
  <section class="market-news-section shrink-0 rounded-xl border border-gray-200 bg-white p-3 shadow-sm dark:border-gray-800 dark:bg-gray-900">
    <div class="flex items-start justify-between gap-3">
      <div>
        <div class="flex items-center gap-2">
          <h2 class="text-sm font-semibold text-gray-900 dark:text-gray-100">Market-Moving News</h2>
          <span class="text-xs text-gray-500 dark:text-gray-400">External Context</span>
        </div>
      </div>
      <span
        class="cursor-help text-xs text-gray-400 dark:text-gray-500"
        title="Official-source events ranked for potential relevance to the next Indian trading session. External context only; not used in SmartMoney scoring."
        aria-label="Market-moving news information"
      >Info</span>
    </div>

    <p v-if="unavailable" class="mt-3 text-sm text-gray-500 dark:text-gray-400">External news context unavailable.</p>
    <p v-else-if="document && document.items.length === 0" class="mt-3 text-sm text-gray-500 dark:text-gray-400">No material market-moving events identified in the current lookback window.</p>

    <div v-else-if="document" class="mt-3 divide-y divide-gray-100 dark:divide-gray-800">
      <article v-for="item in document.items" :key="`${item.rank}-${item.url}`" class="py-3 first:pt-0 last:pb-0">
        <div class="flex flex-wrap items-center gap-1.5 text-[11px] font-semibold uppercase tracking-wide">
          <span :class="['rounded-full border px-1.5 py-0.5', impactClass(item.impact)]">{{ item.impact }}</span>
          <span class="text-gray-500 dark:text-gray-400">{{ item.scope }}</span>
          <span class="text-gray-400 dark:text-gray-500">{{ categoryLabel(item.category) }}</span>
          <span class="ml-auto normal-case font-normal text-gray-400 dark:text-gray-500">{{ item.sentiment }}</span>
        </div>
        <a :href="item.url" target="_blank" rel="noopener noreferrer" class="mt-1.5 block text-sm font-semibold leading-snug text-gray-900 hover:text-indigo-700 dark:text-gray-100 dark:hover:text-indigo-300">
          {{ item.headline }}
        </a>
        <p class="mt-1 text-sm leading-snug text-gray-600 dark:text-gray-300">{{ item.why_it_matters }}</p>
        <div class="mt-2 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-gray-500 dark:text-gray-400">
          <span>{{ item.source }}</span>
          <span aria-hidden="true">·</span>
          <time :datetime="item.published_at_utc">{{ formatPublishedAt(item.published_at_utc) }}</time>
          <a :href="item.url" target="_blank" rel="noopener noreferrer" class="ml-auto font-medium text-indigo-600 hover:text-indigo-800 dark:text-indigo-400 dark:hover:text-indigo-300" :aria-label="`Open ${item.headline} in a new tab`">Open ↗</a>
        </div>
      </article>
    </div>
  </section>
</template>