<script setup lang="ts">
type ParticipantRow = {
  key: string;
  name: string;
  bias: number;
  label: string;
  hasData: boolean;
};

defineProps<{
  rows: ParticipantRow[];
  formatScore: (value: number) => string;
  toneClass: (value: number) => string;
}>();

function participantBarClass(value: number) {
  if (value > 0) return "bg-green-600";
  if (value < 0) return "bg-red-600";
  return "bg-gray-500";
}

function participantBarWidth(value: number) {
  const width = Math.min(100, Math.max(8, Math.abs(value)));
  return `${width}%`;
}
</script>

<template>
  <div class="mb-1.5 flex items-center justify-between">
    <h2 class="text-base font-semibold">FII / DII / PRO / Retail</h2>
    <p class="text-xs text-gray-500 dark:text-gray-400">Standard participant view</p>
  </div>

  <div>
    <table class="min-w-full table-fixed text-sm">
      <colgroup>
        <col class="w-[26%]" />
        <col class="w-[18%]" />
        <col class="w-[28%]" />
        <col class="w-[28%]" />
      </colgroup>
      <thead class="text-left text-xs uppercase tracking-wider text-gray-500 dark:text-gray-400">
        <tr class="border-b border-gray-200 dark:border-gray-800">
          <th class="py-1 pr-3">Participant</th>
          <th class="py-1 pr-3 text-right">Bias</th>
          <th class="py-1 pr-3">Influence</th>
          <th class="py-1 pr-0">Label</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="participant in rows"
          :key="participant.key"
          class="border-b border-gray-100 last:border-b-0 dark:border-gray-800"
        >
          <td class="py-1 pr-3 font-medium text-gray-900 dark:text-gray-100">{{ participant.name }}</td>
          <td :class="['py-1 pr-3 text-right font-semibold', toneClass(participant.bias)]">
            {{ participant.hasData ? formatScore(participant.bias) : '-' }}
          </td>
          <td class="py-1 pr-3">
            <div class="h-2 w-full max-w-28 rounded-full bg-gray-200 dark:bg-gray-800">
              <div
                :class="['h-2 rounded-full', participantBarClass(participant.bias)]"
                :style="{ width: participant.hasData ? participantBarWidth(participant.bias) : '0%' }"
              ></div>
            </div>
          </td>
          <td
            :class="[
              'py-1 pr-0',
              participant.hasData ? 'text-gray-700 dark:text-gray-300' : 'text-gray-500 dark:text-gray-400'
            ]"
          >
            {{ participant.label }}
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
@media (min-width: 1024px) and (max-height: 760px) {
  .text-xs {
    line-height: 1.1;
  }
}
</style>
