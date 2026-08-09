<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import DashboardKpiGrid from "@/components/dashboard/DashboardKpiGrid.vue";
import ExplanationCard from "@/components/dashboard/ExplanationCard.vue";
import HeadlineSignalCard from "@/components/dashboard/HeadlineSignalCard.vue";
import HistoryChart from "@/components/dashboard/HistoryChart.vue";
import MarketStructureSection from "@/components/dashboard/MarketStructureSection.vue";
import ParticipantActivityCard from "@/components/dashboard/ParticipantActivityCard.vue";
import ParticipantGrid from "@/components/dashboard/ParticipantGrid.vue";
import QuickFactsSection from "@/components/dashboard/QuickFactsSection.vue";
import { api, type MarketHistoryPoint, type MarketTodayResponse } from "@/services/api";

const loading = ref(true);
const error = ref<string | null>(null);
const isDark = ref(document.documentElement.classList.contains("dark"));

const today = ref<MarketTodayResponse | null>(null);
const history = ref<MarketHistoryPoint[]>([]);

function formatDate(value?: string | null) {
  if (!value) return "-";
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return value;
  return parsed.toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

const signalDate = computed(() => formatDate(today.value?.date));

// JSON mode doesn’t necessarily provide asOfDate; we fall back to date
const asOfDate = computed(() => formatDate(today.value?.date));

const historyDelta = computed(() => {
  if (history.value.length < 2) return null;
  const firstPoint = history.value[0];
  const lastPoint = history.value[history.value.length - 1];
  if (!firstPoint || !lastPoint) return null;
  return lastPoint.final_score - firstPoint.final_score;
});

const scoreColorClass = computed(() => {
  if (!today.value) return "text-gray-900 dark:text-gray-100";
  const v = today.value.final_score;
  if (v >= 40) return "text-green-700 dark:text-green-400";
  if (v <= -40) return "text-red-700 dark:text-red-400";
  return "text-gray-900 dark:text-gray-100";
});

const regimeBadgeClass = computed(() => {
  if (!today.value)
    return "bg-gray-100 text-gray-700 border-gray-200 dark:bg-gray-800 dark:text-gray-200 dark:border-gray-700";
  return today.value.regime === "SHOCK"
    ? "bg-orange-100 text-orange-800 border-orange-200 dark:bg-orange-500/15 dark:text-orange-300 dark:border-orange-500/30"
    : "bg-gray-100 text-gray-700 border-gray-200 dark:bg-gray-800 dark:text-gray-200 dark:border-gray-700";
});

type ParticipantKey = "FII" | "DII" | "PRO" | "RETAIL";

type ParticipantRow = {
  key: ParticipantKey;
  name: string;
  bias: number;
  label: string;
  hasData: boolean;
};

const participantOrder: ParticipantKey[] = ["FII", "DII", "PRO", "RETAIL"];

function participantDisplayName(key: ParticipantKey) {
  return key === "RETAIL" ? "Retail" : key;
}

function normalizeParticipantName(name: string): ParticipantKey | null {
  const normalized = name.trim().toUpperCase();
  if (normalized.includes("FII")) return "FII";
  if (normalized.includes("DII")) return "DII";
  if (normalized === "PRO" || normalized.includes("PROP") || normalized.includes("PRO")) return "PRO";
  if (normalized.includes("RETAIL") || normalized.includes("CLIENT") || normalized.includes("INDIVIDUAL")) return "RETAIL";
  return null;
}

function fallbackLabelFromBias(bias: number) {
  const abs = Math.abs(bias);
  if (abs >= 2.0) return bias > 0 ? "Strong Bullish" : "Strong Bearish";
  if (abs >= 1.0) return bias > 0 ? "Bullish" : "Bearish";
  if (abs >= 0.4) return bias > 0 ? "Mild Bullish" : "Mild Bearish";
  return "Neutral";
}

const participantRows = computed<ParticipantRow[]>(() => {
  const rows = new Map<ParticipantKey, ParticipantRow>();

  if (today.value) {
    for (const participant of today.value.participants ?? []) {
      const key = normalizeParticipantName(participant.name);
      if (!key || rows.has(key)) continue;

      const bias = participant.bias ?? 0;
      const label = participant.label ?? fallbackLabelFromBias(bias);

      rows.set(key, {
        key,
        name: participantDisplayName(key),
        bias,
        label,
        hasData: true,
      });
    }
  }

  return participantOrder.map((key) =>
    rows.get(key) ?? {
      key,
      name: participantDisplayName(key),
      bias: 0,
      label: "-",
      hasData: false,
    },
  );
});

const topParticipant = computed(() => {
  const available = participantRows.value.filter((row) => row.hasData);
  if (available.length === 0) return null;
  return [...available].sort((a, b) => Math.abs(b.bias) - Math.abs(a.bias))[0] ?? null;
});

const historyDeltaClass = computed(() => {
  const delta = historyDelta.value;
  if (delta === null) return "text-gray-600 dark:text-gray-300";
  if (delta > 0) return "text-green-700 dark:text-green-400";
  if (delta < 0) return "text-red-700 dark:text-red-400";
  return "text-gray-600 dark:text-gray-300";
});

function fmtScore(n: number) {
  return (Math.round(n * 10) / 10).toFixed(1);
}

function participantToneClass(value: number) {
  if (value > 0) return "text-green-700 dark:text-green-400";
  if (value < 0) return "text-red-700 dark:text-red-400";
  return "text-gray-700 dark:text-gray-200";
}

async function load() {
  loading.value = true;
  error.value = null;

  try {
    const [t, h] = await Promise.all([api.marketToday(), api.marketHistory()]);
    today.value = t;
    history.value = h;
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : "Failed to load data";
  } finally {
    loading.value = false;
  }
}

function setTheme(dark: boolean) {
  document.documentElement.classList.toggle("dark", dark);
  localStorage.setItem("theme", dark ? "dark" : "light");
  isDark.value = dark;
}

function toggleTheme() {
  setTheme(!isDark.value);
}

onMounted(load);
</script>

<template>
  <div
    class="dashboard-shell flex min-h-dvh items-stretch justify-center overflow-y-auto bg-gray-100 text-gray-900 dark:bg-gray-950 dark:text-gray-100 lg:h-dvh lg:overflow-hidden"
  >
    <div class="dashboard-inner flex w-full max-w-[1280px] flex-col gap-2 px-3 py-2 sm:px-4 sm:py-3 lg:h-full">
      <header class="flex shrink-0 flex-col gap-1.5 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <p class="text-xs font-semibold uppercase tracking-wider text-indigo-600 dark:text-indigo-400">Dashboard</p>
          <h1 class="text-xl font-bold tracking-tight sm:text-2xl">Smart Money - NIFTY Bias</h1>
          <p class="text-xs text-gray-600 dark:text-gray-300 sm:text-sm" v-if="today">
            As Of Date: <span class="font-semibold text-gray-800 dark:text-gray-100">{{ asOfDate }}</span>
            <span class="mx-2 text-gray-300 dark:text-gray-600">•</span>
            Signal Date: <span class="font-semibold text-gray-800 dark:text-gray-100">{{ signalDate }}</span>
          </p>
        </div>

        <div class="flex items-center gap-2">
          <button
            class="inline-flex h-10 min-w-[112px] items-center justify-center rounded-lg border border-gray-300 bg-white px-4 text-sm font-medium text-gray-700 hover:bg-gray-100 dark:border-gray-700 dark:bg-gray-900 dark:text-gray-100 dark:hover:bg-gray-800"
            @click="toggleTheme"
            type="button"
          >
            {{ isDark ? "Light mode" : "Dark mode" }}
          </button>
          <button
            class="inline-flex h-10 min-w-[112px] items-center justify-center rounded-lg border border-gray-300 bg-white px-4 text-sm font-medium text-gray-700 hover:bg-gray-100 disabled:cursor-not-allowed disabled:opacity-60 dark:border-gray-700 dark:bg-gray-900 dark:text-gray-100 dark:hover:bg-gray-800"
            @click="load"
            :disabled="loading"
          >
            Refresh
          </button>
        </div>
      </header>

      <div
        v-if="error"
        class="shrink-0 rounded-xl border border-red-200 bg-red-50 p-2 text-sm text-red-800 dark:border-red-500/30 dark:bg-red-500/15 dark:text-red-200"
      >
        {{ error }}
      </div>

      <DashboardKpiGrid
        v-if="!loading && today"
        :today="today"
        :history-delta="historyDelta"
        :history-delta-class="historyDeltaClass"
        :history-count="history.length"
        :as-of-date="asOfDate"
        :score-color-class="scoreColorClass"
        :regime-badge-class="regimeBadgeClass"
        :format-score="fmtScore"
      />

      <div v-if="loading" class="space-y-2">
        <div class="h-48 animate-pulse rounded-2xl border border-gray-200 bg-white dark:border-gray-800 dark:bg-gray-900"></div>
        <div class="grid gap-2 md:grid-cols-2">
          <div class="h-60 animate-pulse rounded-2xl border border-gray-200 bg-white dark:border-gray-800 dark:bg-gray-900"></div>
          <div class="h-60 animate-pulse rounded-2xl border border-gray-200 bg-white dark:border-gray-800 dark:bg-gray-900"></div>
        </div>
      </div>

      <template v-else>
        <div
          v-if="today"
          class="dashboard-main-grid grid gap-2 lg:min-h-0 lg:flex-1 lg:grid-rows-[minmax(0,0.86fr)_minmax(0,0.8fr)_minmax(0,1.12fr)]"
        >
          <section class="grid gap-2 lg:min-h-0 lg:grid-cols-12">
            <article
              class="overflow-hidden rounded-xl border border-gray-200 bg-white p-2.5 shadow-sm dark:border-gray-800 dark:bg-gray-900 lg:col-span-7 lg:h-full lg:min-h-0"
            >
              <HeadlineSignalCard
                :final-score="today.final_score"
                :strength="today.strength"
                :bias-label="today.bias_Label"
                :regime="today.regime"
                :shock-score="today.shock_score"
                :score-color-class="scoreColorClass"
                :regime-badge-class="regimeBadgeClass"
                :format-score="fmtScore"
              />
            </article>

            <article
              class="flex flex-col overflow-hidden rounded-xl border border-gray-200 bg-white p-2.5 shadow-sm dark:border-gray-800 dark:bg-gray-900 lg:col-span-5 lg:h-full lg:min-h-0"
            >
              <ExplanationCard :explanation="today.explanation" />
            </article>
          </section>

          <section class="grid gap-2 lg:min-h-0 lg:grid-cols-12">
            <article
              class="flex flex-col overflow-hidden rounded-xl border border-gray-200 bg-white p-2.5 shadow-sm dark:border-gray-800 dark:bg-gray-900 lg:col-span-7 lg:h-full lg:min-h-0"
            >
              <HistoryChart
                :history="history"
                :delta="historyDelta"
                :delta-class="historyDeltaClass"
                :format-score="fmtScore"
              />
            </article>

            <article
              class="overflow-hidden rounded-xl border border-gray-200 bg-white p-2.5 shadow-sm dark:border-gray-800 dark:bg-gray-900 lg:col-span-5 lg:h-full lg:min-h-0"
            >
              <MarketStructureSection
                :smart-bias="today.smart_bias"
                :retail-bias="today.retail_bias"
                :dii-bias="today.dii_bias"
                :smart-retail-divergence="today.smart_retail_divergence"
                :smart-retail-state="today.smart_retail_state"
              />

              <div class="my-2 border-t border-gray-200 dark:border-gray-800"></div>

              <QuickFactsSection
                :today="today"
                :top-participant="topParticipant"
                :signal-date="signalDate"
                :format-score="fmtScore"
                :tone-class="participantToneClass"
              />
            </article>
          </section>

          <section class="grid gap-2 lg:min-h-0 lg:grid-cols-12">
            <div class="overflow-hidden rounded-xl border border-gray-200 bg-white p-2.5 shadow-sm dark:border-gray-800 dark:bg-gray-900 lg:col-span-7 lg:h-full lg:min-h-0">
              <ParticipantGrid
                :rows="participantRows"
                :format-score="fmtScore"
                :tone-class="participantToneClass"
              />
            </div>

            <div class="flex flex-col overflow-hidden rounded-xl border border-gray-200 bg-white p-2.5 shadow-sm dark:border-gray-800 dark:bg-gray-900 lg:col-span-5 lg:h-full lg:min-h-0">
              <ParticipantActivityCard :activity="today.participant_activity" :signal-date="signalDate" />
            </div>
          </section>
        </div>

        <div
          v-else
          class="rounded-2xl border border-gray-200 bg-white p-6 text-sm text-gray-600 shadow-sm dark:border-gray-800 dark:bg-gray-900 dark:text-gray-300"
        >
          No market data available.
        </div>
      </template>
    </div>
  </div>
</template>

<style scoped>
.dashboard-shell {
  --dash-gap: clamp(0.45rem, 0.65vw, 0.75rem);
  --dash-pad: clamp(0.5rem, 0.7vw, 0.9rem);
  --dash-kpi-min: clamp(86px, 11.5vh, 110px);
  --dash-headline-size: clamp(2rem, 3.5vh, 2.6rem);
  --dash-kpi-size: clamp(1.55rem, 2.5vh, 2rem);
  --dash-chart-min: clamp(64px, 10vh, 92px);
  --dash-expl-max: clamp(88px, 14vh, 128px);
}

.dashboard-shell .dashboard-main-grid {
  gap: var(--dash-gap);
}

@media (min-width: 1024px) and (max-height: 860px) {
  .dashboard-shell {
    --dash-gap: clamp(0.4rem, 0.55vw, 0.65rem);
    --dash-pad: clamp(0.42rem, 0.55vw, 0.72rem);
    --dash-kpi-min: clamp(78px, 10.5vh, 96px);
    --dash-headline-size: clamp(1.8rem, 3.15vh, 2.2rem);
    --dash-kpi-size: clamp(1.35rem, 2.2vh, 1.75rem);
    --dash-chart-min: clamp(56px, 8.6vh, 76px);
    --dash-expl-max: clamp(76px, 11.5vh, 104px);
  }

  .dashboard-main-grid {
    grid-template-rows: minmax(0, 0.84fr) minmax(0, 0.78fr) minmax(0, 1.1fr);
  }
}

@media (min-width: 1024px) and (max-height: 760px) {
  .dashboard-main-grid {
    grid-template-rows: minmax(0, 0.8fr) minmax(0, 0.74fr) minmax(0, 1.08fr);
  }

  .dashboard-shell .text-xs {
    line-height: 1.1;
  }
}

/* Tall screens (≥960px height): switch to scrollable layout with capped card heights */
@media (min-width: 1024px) and (min-height: 960px) {
  .dashboard-shell {
    height: auto;
    overflow-y: auto;
    align-items: flex-start;
  }

  .dashboard-inner {
    height: auto;
  }

  .dashboard-main-grid {
    flex: none;
    grid-template-rows: minmax(160px, 220px) minmax(195px, 255px) minmax(170px, 210px);
  }
}

/* Extra-wide screens (≥1536px): expand content max-width */
@media (min-width: 1536px) {
  .dashboard-inner {
    max-width: 1440px;
  }
}
</style>
