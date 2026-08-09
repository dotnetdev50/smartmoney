<script setup lang="ts">
import { computed } from "vue";
import type { MarketTodayResponse } from "@/services/api";

const props = defineProps<{
  today: MarketTodayResponse;
  historyDelta: number | null;
  historyDeltaClass: string;
  historyCount: number;
  asOfDate: string;
  scoreColorClass: string;
  regimeBadgeClass: string;
  formatScore: (value: number) => string;
}>();

const trendLabel = computed(() => {
  if (props.historyDelta === null) return "Flat";
  if (props.historyDelta > 0) return "Improving";
  if (props.historyDelta < 0) return "Weakening";
  return "Flat";
});
</script>

<template>
  <section class="dashboard-kpi-grid grid shrink-0 grid-cols-2 gap-2 lg:grid-cols-4">
    <article
      class="dashboard-card rounded-xl border border-gray-200 bg-white p-2.5 shadow-sm dark:border-gray-800 dark:bg-gray-900 lg:min-h-[92px]"
    >
      <p class="text-xs font-medium uppercase tracking-wider text-gray-500 dark:text-gray-400">Final Score</p>
      <p :class="['mt-1 text-2xl font-bold leading-none', scoreColorClass]">
        {{ formatScore(today.final_score) }}
      </p>
      <p class="mt-1 text-xs text-gray-500 dark:text-gray-400">
        {{ (today.strength ?? "—") }} {{ (today.bias_Label ?? "") }}
      </p>
    </article>

    <article
      class="dashboard-card rounded-xl border border-gray-200 bg-white p-2.5 shadow-sm dark:border-gray-800 dark:bg-gray-900 lg:min-h-[92px]"
    >
      <p class="text-xs font-medium uppercase tracking-wider text-gray-500 dark:text-gray-400">Regime</p>
      <p class="mt-1 text-2xl font-semibold leading-none text-gray-900 dark:text-gray-100">{{ today.regime }}</p>
      <span :class="['mt-1 inline-flex rounded-full border px-2 py-0.5 text-xs font-semibold', regimeBadgeClass]">
        Live Regime
      </span>
    </article>

    <article
      class="dashboard-card rounded-xl border border-gray-200 bg-white p-2.5 shadow-sm dark:border-gray-800 dark:bg-gray-900 lg:min-h-[92px]"
    >
      <p class="text-xs font-medium uppercase tracking-wider text-gray-500 dark:text-gray-400">30D Trend</p>
      <p :class="['mt-1 text-2xl font-semibold leading-none', historyDeltaClass]">{{ trendLabel }}</p>
      <p class="mt-1 text-xs text-gray-500 dark:text-gray-400">{{ historyCount }} points tracked</p>
    </article>

    <article
      class="dashboard-card rounded-xl border border-gray-200 bg-white p-2.5 shadow-sm dark:border-gray-800 dark:bg-gray-900 lg:min-h-[92px]"
    >
      <p class="text-xs font-medium uppercase tracking-wider text-gray-500 dark:text-gray-400">As Of Date</p>
      <p class="mt-1 text-2xl font-semibold leading-none text-gray-900 dark:text-gray-100">{{ asOfDate }}</p>
      <p class="mt-1 text-xs text-gray-500 dark:text-gray-400">Data publishing timestamp</p>
    </article>
  </section>
</template>

<style scoped>
.dashboard-kpi-grid {
  gap: var(--dash-gap);
}

.dashboard-card {
  min-height: var(--dash-kpi-min);
  padding: var(--dash-pad);
}

.text-2xl {
  font-size: var(--dash-kpi-size);
}

@media (min-width: 1024px) and (max-height: 760px) {
  .text-xs {
    line-height: 1.1;
  }
}
</style>
