<script setup lang="ts">
defineProps<{
  smartBias?: number | null;
  retailBias?: number | null;
  diiBias?: number | null;
  smartRetailDivergence?: number | null;
  smartRetailState?: string | null;
}>();

function fmtStruct(value: number | null | undefined) {
  if (value == null) return "—";
  return (Math.round(value * 10) / 10).toFixed(1);
}

function participantToneClass(value: number) {
  if (value > 0) return "text-green-700 dark:text-green-400";
  if (value < 0) return "text-red-700 dark:text-red-400";
  return "text-gray-700 dark:text-gray-200";
}

function smartRetailStateLabel(state: string | null | undefined) {
  switch (state) {
    case "SmartBullRetailBear":
      return "Smart Bull / Retail Bear";
    case "SmartBearRetailBull":
      return "Smart Bear / Retail Bull";
    case "BothBull":
      return "Both Bullish";
    case "BothBear":
      return "Both Bearish";
    case "MixedNeutral":
      return "Mixed / Neutral";
    default:
      return "—";
  }
}
</script>

<template>
  <p class="text-xs font-semibold uppercase tracking-wider text-gray-500 dark:text-gray-400">Market Structure</p>
  <dl class="mt-2 grid grid-cols-2 gap-x-4 gap-y-2">
    <div>
      <dt class="text-xs text-gray-500 dark:text-gray-400">Smart Bias</dt>
      <dd :class="['text-sm font-semibold', participantToneClass(smartBias ?? 0)]">{{ fmtStruct(smartBias) }}</dd>
    </div>
    <div>
      <dt class="text-xs text-gray-500 dark:text-gray-400">Retail Bias</dt>
      <dd :class="['text-sm font-semibold', participantToneClass(retailBias ?? 0)]">{{ fmtStruct(retailBias) }}</dd>
    </div>
    <div>
      <dt class="text-xs text-gray-500 dark:text-gray-400">DII Bias</dt>
      <dd :class="['text-sm font-semibold', participantToneClass(diiBias ?? 0)]">{{ fmtStruct(diiBias) }}</dd>
    </div>
    <div>
      <dt class="text-xs text-gray-500 dark:text-gray-400">Smart vs Retail</dt>
      <dd :class="['text-sm font-semibold', participantToneClass(smartRetailDivergence ?? 0)]">
        {{ fmtStruct(smartRetailDivergence) }}
      </dd>
    </div>
  </dl>
  <div class="mt-2 rounded-lg border border-gray-200 bg-gray-50 px-2 py-1.5 dark:border-gray-800 dark:bg-gray-950/40">
    <p class="text-xs text-gray-500 dark:text-gray-400">Smart/Retail State</p>
    <p class="text-sm font-semibold text-gray-900 dark:text-gray-100">{{ smartRetailStateLabel(smartRetailState) }}</p>
  </div>
</template>

<style scoped>
@media (min-width: 1024px) and (max-height: 760px) {
  .text-xs {
    line-height: 1.1;
  }
}
</style>
