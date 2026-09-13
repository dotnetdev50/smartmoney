<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { api, type LayoffsSummary, type MarketTodayResponse, type PreciousMetalsSummary } from "@/services/api";

const props = defineProps<{
  today: MarketTodayResponse;
  historyDelta: number | null;
  historyDeltaClass: string;
  historyCount: number;
  scoreColorClass: string;
  regimeBadgeClass: string;
  formatScore: (value: number) => string;
  layoffs?: LayoffsSummary | null;
}>();

const trendLabel = computed(() => {
  if (props.historyDelta === null) return "Flat";
  if (props.historyDelta > 0) return "Improving";
  if (props.historyDelta < 0) return "Weakening";
  return "Flat";
});

const numberFormatter = new Intl.NumberFormat();

const layoffsEmployeesFormatted = computed(() =>
  props.layoffs ? numberFormatter.format(props.layoffs.employees_laid_off) : "Unavailable",
);

const layoffsCompaniesFormatted = computed(() =>
  props.layoffs ? numberFormatter.format(props.layoffs.companies_with_layoffs) : null,
);

const usdFormatter = new Intl.NumberFormat("en-US", {
  style: "currency",
  currency: "USD",
  maximumFractionDigits: 2,
});

const preciousMetals = ref<PreciousMetalsSummary | null>(null);
let preciousMetalsRequestVersion = 0;

function formatDate(value?: string | null) {
  if (!value) return "-";
  const parsed = new Date(`${value}T00:00:00Z`);
  if (Number.isNaN(parsed.getTime())) return value;
  return parsed.toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

function formatPrice(value?: number | null) {
  if (value == null) return "Unavailable";
  return usdFormatter.format(value);
}

async function loadPreciousMetals() {
  const version = ++preciousMetalsRequestVersion;

  try {
    const summary = await api.preciousMetalsSummary();
    if (version !== preciousMetalsRequestVersion) return;
    preciousMetals.value = summary;
  } catch {
    if (version !== preciousMetalsRequestVersion) return;
    preciousMetals.value = null;
  }
}

watch(
  () => props.today,
  () => {
    void loadPreciousMetals();
  },
  { immediate: true },
);
</script>

<template>
  <section class="dashboard-kpi-grid grid shrink-0 grid-cols-2 gap-2 lg:grid-cols-6">
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
      title="Reported global tech layoffs tracked by layoffs.fyi. External context only; not used in SmartMoney scoring."
    >
      <p class="text-xs font-medium uppercase tracking-wider text-gray-500 dark:text-gray-400">Tech Layoffs YTD</p>
      <p class="mt-1 text-2xl font-semibold leading-none text-gray-900 dark:text-gray-100">
        {{ layoffsEmployeesFormatted }}
      </p>
      <p class="mt-1 text-xs text-gray-500 dark:text-gray-400">
        <template v-if="layoffs">
          {{ layoffs.year }} · {{ layoffsCompaniesFormatted }} companies ·
          <a
            :href="layoffs.source_url"
            target="_blank"
            rel="noopener noreferrer nofollow"
            class="underline hover:text-gray-700 dark:hover:text-gray-300"
          >layoffs.fyi</a>
        </template>
        <template v-else>External data</template>
      </p>
    </article>

    <article
      class="dashboard-card rounded-xl border border-gray-200 bg-white p-2.5 shadow-sm dark:border-gray-800 dark:bg-gray-900 lg:min-h-[92px]"
      title="Gold daily price from an external source. External context only; not used in SmartMoney scoring."
    >
      <p class="text-xs font-medium uppercase tracking-wider text-gray-500 dark:text-gray-400">Gold Price</p>
      <p class="mt-1 text-2xl font-semibold leading-none text-gray-900 dark:text-gray-100">
        {{ formatPrice(preciousMetals?.gold?.price_usd) }}
      </p>
      <p class="mt-1 text-xs text-gray-500 dark:text-gray-400">
        <template v-if="preciousMetals?.gold">
          {{ formatDate(preciousMetals.gold.as_of_date) }} ·
          <a
            :href="preciousMetals.source_url"
            target="_blank"
            rel="noopener noreferrer nofollow"
            class="underline hover:text-gray-700 dark:hover:text-gray-300"
          >{{ preciousMetals.source }}</a>
        </template>
        <template v-else>External data</template>
      </p>
    </article>

    <article
      class="dashboard-card rounded-xl border border-gray-200 bg-white p-2.5 shadow-sm dark:border-gray-800 dark:bg-gray-900 lg:min-h-[92px]"
      title="Silver daily price from an external source. External context only; not used in SmartMoney scoring."
    >
      <p class="text-xs font-medium uppercase tracking-wider text-gray-500 dark:text-gray-400">Silver Price</p>
      <p class="mt-1 text-2xl font-semibold leading-none text-gray-900 dark:text-gray-100">
        {{ formatPrice(preciousMetals?.silver?.price_usd) }}
      </p>
      <p class="mt-1 text-xs text-gray-500 dark:text-gray-400">
        <template v-if="preciousMetals?.silver">
          {{ formatDate(preciousMetals.silver.as_of_date) }} ·
          <a
            :href="preciousMetals.source_url"
            target="_blank"
            rel="noopener noreferrer nofollow"
            class="underline hover:text-gray-700 dark:hover:text-gray-300"
          >{{ preciousMetals.source }}</a>
        </template>
        <template v-else>External data</template>
      </p>
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
