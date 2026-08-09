<script setup lang="ts">
import { computed } from "vue";

const props = defineProps<{
  finalScore: number;
  strength?: string | null;
  biasLabel?: string | null;
  regime: string;
  shockScore?: number | null;
  scoreColorClass: string;
  regimeBadgeClass: string;
  formatScore: (value: number) => string;
}>();

const scoreBadgeClass = computed(() => {
  const value = props.finalScore;
  if (value >= 40)
    return "bg-green-100 text-green-800 border-green-200 dark:bg-green-500/15 dark:text-green-300 dark:border-green-500/30";
  if (value <= -40)
    return "bg-red-100 text-red-800 border-red-200 dark:bg-red-500/15 dark:text-red-300 dark:border-red-500/30";
  return "bg-gray-100 text-gray-700 border-gray-200 dark:bg-gray-800 dark:text-gray-200 dark:border-gray-700";
});

const shockBadgeClass = computed(() => {
  const value = props.shockScore ?? 0;
  if (value >= 25)
    return "bg-orange-100 text-orange-800 border-orange-200 dark:bg-orange-500/15 dark:text-orange-300 dark:border-orange-500/30";
  if (value >= 10)
    return "bg-amber-100 text-amber-800 border-amber-200 dark:bg-amber-500/15 dark:text-amber-300 dark:border-amber-500/30";
  return "bg-green-100 text-green-800 border-green-200 dark:bg-green-500/15 dark:text-green-300 dark:border-green-500/30";
});

const scoreMeterWidth = computed(() =>
  `${Math.min(50, Math.abs(props.finalScore) / 2)}%`,
);
</script>

<template>
  <p class="text-xs font-semibold uppercase tracking-wider text-gray-500 dark:text-gray-400">Headline Signal</p>
  <div class="mt-1.5 flex flex-wrap items-end gap-2">
    <div :class="['headline-score text-4xl font-bold leading-none', scoreColorClass]">
      {{ formatScore(finalScore) }}
    </div>
    <span :class="['inline-flex rounded-full border px-2.5 py-0.5 text-sm font-semibold', scoreBadgeClass]">
      {{ (strength ?? "—") }} {{ (biasLabel ?? "") }}
    </span>
  </div>
  <div class="mt-1.5 flex flex-wrap items-center gap-2">
    <span :class="['rounded-full border px-2 py-0.5 text-xs font-semibold', regimeBadgeClass]">
      Regime: {{ regime }}
    </span>
    <span :class="['rounded-full border px-2 py-0.5 text-xs font-semibold', shockBadgeClass]">
      Shock Score: {{ formatScore(shockScore ?? 0) }}
    </span>
  </div>

  <div class="mt-2">
    <div class="mb-1 flex justify-between text-xs font-medium text-gray-500 dark:text-gray-400">
      <span>Bearish</span>
      <span>Neutral</span>
      <span>Bullish</span>
    </div>
    <div class="relative h-2 rounded-full bg-gray-200 dark:bg-gray-800">
      <div class="absolute left-1/2 top-0 h-full w-px -translate-x-1/2 bg-gray-400 dark:bg-gray-600"></div>
      <div
        :class="[
          'absolute top-0 h-full rounded-full',
          finalScore >= 0 ? 'left-1/2 bg-green-600' : 'right-1/2 bg-red-600'
        ]"
        :style="{ width: scoreMeterWidth }"
      ></div>
    </div>
  </div>
</template>

<style scoped>
.headline-score {
  font-size: var(--dash-headline-size);
}

@media (min-width: 1024px) and (max-height: 760px) {
  .text-xs {
    line-height: 1.1;
  }
}
</style>
