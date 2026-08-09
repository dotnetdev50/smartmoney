<script setup lang="ts">
import type { MarketTodayResponse } from "@/services/api";

type TopParticipant = {
  name: string;
  bias: number;
  label: string;
};

defineProps<{
  today: MarketTodayResponse;
  topParticipant: TopParticipant | null;
  signalDate: string;
  formatScore: (value: number) => string;
  toneClass: (value: number) => string;
}>();

function fmtPcr(value: number | null | undefined): string {
  if (value == null) return "—";
  return value.toFixed(2);
}

function fmtVix(value: number | null | undefined): string {
  if (value == null) return "—";
  return value.toFixed(2);
}

function pcrLabel(value: number | null | undefined): string {
  if (value == null) return "";
  if (value >= 1.3) return "Bullish";
  if (value >= 0.8) return "Neutral";
  return "Bearish";
}

function pcrLabelClass(value: number | null | undefined): string {
  if (value == null) return "text-gray-500 dark:text-gray-400";
  if (value >= 1.3) return "text-green-700 dark:text-green-400";
  if (value >= 0.8) return "text-gray-600 dark:text-gray-300";
  return "text-red-700 dark:text-red-400";
}
</script>

<template>
  <p class="text-xs font-semibold uppercase tracking-wider text-gray-500 dark:text-gray-400">Quick Facts</p>
  <dl class="mt-2 grid grid-cols-2 gap-x-6 gap-y-2">
    <div class="space-y-2">
      <div>
        <dt class="text-xs text-gray-500 dark:text-gray-400">Index</dt>
        <dd class="text-sm font-semibold leading-tight text-gray-900 dark:text-gray-100">{{ today.index }}</dd>
      </div>
      <div>
        <dt class="text-xs text-gray-500 dark:text-gray-400">Top Driver</dt>
        <dd class="text-sm font-semibold leading-tight text-gray-900 dark:text-gray-100">
          {{ topParticipant ? topParticipant.name : "-" }}
          <span
            v-if="topParticipant"
            :class="['ml-1 text-xs font-medium', toneClass(topParticipant.bias)]"
          >
            {{ formatScore(topParticipant.bias) }} ({{ topParticipant.label }})
          </span>
        </dd>
      </div>
      <div>
        <dt class="text-xs text-gray-500 dark:text-gray-400" title="Put-Call Ratio by Open Interest">PCR OI (NIFTY)</dt>
        <dd class="text-sm font-semibold leading-tight text-gray-900 dark:text-gray-100">
          <template v-if="today.pcr != null">
            {{ fmtPcr(today.pcr) }}
            <span :class="['ml-1 text-xs font-medium', pcrLabelClass(today.pcr)]">
              {{ pcrLabel(today.pcr) }}
            </span>
          </template>
          <span
            v-else
            class="inline-flex items-center gap-1 rounded-full border border-amber-300 bg-amber-50 px-1.5 py-0.5 text-xs font-medium text-amber-700 dark:border-amber-500/40 dark:bg-amber-500/10 dark:text-amber-400"
            title="PCR data not yet published by NSE for today"
          >
            Unavailable
          </span>
        </dd>
      </div>
      <div>
        <dt class="text-xs text-gray-500 dark:text-gray-400" title="Put-Call Ratio by traded volume (contracts)">PCR Vol (NIFTY)</dt>
        <dd class="text-sm font-semibold leading-tight text-gray-900 dark:text-gray-100">
          <template v-if="today.pcr_volume != null">
            {{ fmtPcr(today.pcr_volume) }}
            <span :class="['ml-1 text-xs font-medium', pcrLabelClass(today.pcr_volume)]">
              {{ pcrLabel(today.pcr_volume) }}
            </span>
          </template>
          <span
            v-else
            class="inline-flex items-center gap-1 rounded-full border border-amber-300 bg-amber-50 px-1.5 py-0.5 text-xs font-medium text-amber-700 dark:border-amber-500/40 dark:bg-amber-500/10 dark:text-amber-400"
            title="PCR Volume data not yet published by NSE for today"
          >
            Unavailable
          </span>
        </dd>
      </div>
    </div>

    <div class="space-y-2">
      <div>
        <dt class="text-xs text-gray-500 dark:text-gray-400">Data Date</dt>
        <dd class="text-sm font-semibold text-gray-900 dark:text-gray-100">{{ signalDate }}</dd>
      </div>
      <div>
        <dt class="text-xs text-gray-500 dark:text-gray-400">India VIX</dt>
        <dd class="text-sm font-semibold text-gray-900 dark:text-gray-100">
          <template v-if="today.vix != null">
            {{ fmtVix(today.vix) }}
          </template>
          <span
            v-else
            class="inline-flex items-center gap-1 rounded-full border border-amber-300 bg-amber-50 px-1.5 py-0.5 text-xs font-medium text-amber-700 dark:border-amber-500/40 dark:bg-amber-500/10 dark:text-amber-400"
            title="VIX data not yet published by NSE for today"
          >
            Unavailable
          </span>
        </dd>
      </div>
      <div>
        <dt class="text-xs text-gray-500 dark:text-gray-400" title="Put-Call Ratio by Open Interest">PCR OI (BANKNIFTY)</dt>
        <dd class="text-sm font-semibold leading-tight text-gray-900 dark:text-gray-100">
          <template v-if="today.banknifty_pcr != null">
            {{ fmtPcr(today.banknifty_pcr) }}
            <span :class="['ml-1 text-xs font-medium', pcrLabelClass(today.banknifty_pcr)]">
              {{ pcrLabel(today.banknifty_pcr) }}
            </span>
          </template>
          <span
            v-else
            class="inline-flex items-center gap-1 rounded-full border border-amber-300 bg-amber-50 px-1.5 py-0.5 text-xs font-medium text-amber-700 dark:border-amber-500/40 dark:bg-amber-500/10 dark:text-amber-400"
            title="BANKNIFTY PCR data not yet published by NSE for today"
          >
            Unavailable
          </span>
        </dd>
      </div>
      <div>
        <dt class="text-xs text-gray-500 dark:text-gray-400" title="Put-Call Ratio by traded volume (contracts)">PCR Vol (BANKNIFTY)</dt>
        <dd class="text-sm font-semibold leading-tight text-gray-900 dark:text-gray-100">
          <template v-if="today.banknifty_pcr_volume != null">
            {{ fmtPcr(today.banknifty_pcr_volume) }}
            <span :class="['ml-1 text-xs font-medium', pcrLabelClass(today.banknifty_pcr_volume)]">
              {{ pcrLabel(today.banknifty_pcr_volume) }}
            </span>
          </template>
          <span
            v-else
            class="inline-flex items-center gap-1 rounded-full border border-amber-300 bg-amber-50 px-1.5 py-0.5 text-xs font-medium text-amber-700 dark:border-amber-500/40 dark:bg-amber-500/10 dark:text-amber-400"
            title="BANKNIFTY PCR Volume data not yet published by NSE for today"
          >
            Unavailable
          </span>
        </dd>
      </div>
    </div>
  </dl>
</template>

<style scoped>
@media (min-width: 1024px) and (max-height: 760px) {
  .text-xs {
    line-height: 1.1;
  }
}
</style>
