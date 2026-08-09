<script setup lang="ts">
import { computed } from "vue";
import type { MarketHistoryPoint } from "@/services/api";

const props = defineProps<{
  history: MarketHistoryPoint[];
  delta: number | null;
  deltaClass: string;
  formatScore: (value: number) => string;
}>();

const historyRange = computed(() => {
  if (props.history.length === 0) return "No history data";
  return `${props.history[0]?.date} → ${props.history[props.history.length - 1]?.date}`;
});

const points = computed(() => {
  if (props.history.length === 0) return "";
  const ys = props.history.map((point) => point.final_score);

  const minY = Math.min(...ys);
  const maxY = Math.max(...ys);
  const rangeY = Math.max(1e-6, maxY - minY);

  const width = 100;
  const height = 36;
  const pad = 2;

  const xScale = (index: number) =>
    pad + (index * (width - pad * 2)) / Math.max(1, ys.length - 1);

  const yScale = (value: number) =>
    pad + (height - pad * 2) * (1 - (value - minY) / rangeY);

  return ys
    .map((value, index) => `${xScale(index).toFixed(2)},${yScale(value).toFixed(2)}`)
    .join(" ");
});
</script>

<template>
  <div class="mb-2 flex flex-wrap items-center justify-between gap-2">
    <h2 class="text-base font-semibold">30-Day Trend</h2>
    <p :class="['text-sm font-semibold', deltaClass]">
      Change: {{ delta !== null ? formatScore(delta) : "-" }}
    </p>
  </div>
  <div class="min-h-[100px] rounded-lg border border-gray-200 bg-white p-1.5 dark:border-gray-800 dark:bg-gray-900 lg:min-h-0 lg:flex-1">
    <svg
      viewBox="0 0 100 36"
      preserveAspectRatio="none"
      class="history-chart-plot h-full min-h-[72px] w-full text-gray-900 dark:text-gray-200"
    >
      <polyline :points="points" fill="none" stroke="currentColor" stroke-width="1.6" />
    </svg>
  </div>
  <p class="mt-1 text-xs text-gray-500 dark:text-gray-400">{{ historyRange }}</p>
</template>

<style scoped>
.history-chart-plot {
  min-height: var(--dash-chart-min);
}

@media (min-width: 1024px) and (max-height: 760px) {
  .text-xs {
    line-height: 1.1;
  }
}
</style>
