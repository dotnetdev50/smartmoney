<script setup lang="ts">
import type { ParticipantActivityRow } from "@/services/api";

defineProps<{
  activity?: ParticipantActivityRow[] | null;
  signalDate: string;
}>();

function fmtPct(v: number | null | undefined): string {
  if (v == null) return "—";
  return (v >= 0 ? "+" : "") + v.toFixed(2) + "%";
}

function oiChangeClass(value: number) {
  if (value > 0) return "text-green-700 dark:text-green-400";
  if (value < 0) return "text-red-700 dark:text-red-400";
  return "text-gray-600 dark:text-gray-300";
}

function fmtOiChange(n: number): string {
  const abs = Math.abs(n);
  const sign = n >= 0 ? "+" : "-";
  if (abs >= 1000) return `${sign}${(abs / 1000).toFixed(1)}K`;
  return `${sign}${abs.toFixed(0)}`;
}
</script>

<template>
  <div class="mb-1.5 flex shrink-0 items-center justify-between">
    <h2 class="text-base font-semibold">Participants Activity</h2>
    <p class="text-xs text-gray-500 dark:text-gray-400">{{ signalDate }}</p>
  </div>

  <template v-if="activity?.length">
    <div class="grid min-h-0 flex-1 grid-cols-2 gap-x-4 overflow-y-auto">
      <!-- Left panel: CHANGES (NET OI) -->
      <div>
        <p class="mb-1 text-xs font-semibold uppercase tracking-wider text-gray-500 dark:text-gray-400">Changes (Net OI)</p>
        <ul class="space-y-1">
          <li
            v-for="row in activity"
            :key="`left-${row.participant}-${row.instrument}`"
            class="flex items-baseline justify-between gap-1"
          >
            <span class="text-xs text-gray-600 dark:text-gray-300">{{ row.participant }} — {{ row.instrument }}</span>
            <span :class="['text-xs font-semibold tabular-nums', oiChangeClass(row.net_oi_change)]">
              {{ fmtOiChange(row.net_oi_change) }}
            </span>
          </li>
        </ul>
      </div>
      <!-- Right panel: VS YESTERDAY (%) -->
      <div>
        <p class="mb-1 text-xs font-semibold uppercase tracking-wider text-gray-500 dark:text-gray-400">vs Yesterday (%)</p>
        <ul class="space-y-1">
          <li
            v-for="row in activity"
            :key="`right-${row.participant}-${row.instrument}`"
            class="flex items-baseline justify-between gap-1"
          >
            <span class="text-xs text-gray-600 dark:text-gray-300">{{ row.participant }} — {{ row.instrument }} Δ</span>
            <span
              v-if="row.vs_yesterday_pct != null"
              :class="['text-xs font-semibold tabular-nums', oiChangeClass(row.vs_yesterday_pct)]"
            >
              {{ fmtPct(row.vs_yesterday_pct) }}
            </span>
            <span v-else class="text-xs text-gray-400 dark:text-gray-500">—</span>
          </li>
        </ul>
      </div>
    </div>
  </template>
  <p v-else class="text-xs text-gray-500 dark:text-gray-400">Participant activity data unavailable.</p>
</template>
