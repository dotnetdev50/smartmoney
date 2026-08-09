<script setup lang="ts">
defineProps<{
  explanation?: string | null;
}>();
</script>

<template>
  <h2 class="text-base font-semibold">Explanation</h2>
  <p class="explanation-content mt-1 max-h-[112px] overflow-hidden text-xs leading-5 text-gray-800 dark:text-gray-200 sm:text-sm">
    {{ explanation ?? "Explanation will appear here once enabled." }}
  </p>
  <p class="mt-1 text-xs text-gray-500 dark:text-gray-400">Rule-based explanation (V2). No AI.</p>
</template>

<style scoped>
.explanation-content {
  max-height: var(--dash-expl-max);
}
</style>
