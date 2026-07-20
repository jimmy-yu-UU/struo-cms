<!-- frontend/src/components/common/TableFooter.vue -->
<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{ first: number; rows: number; total: number }>()

const from = computed(() => (props.total === 0 ? 0 : props.first + 1))
const to = computed(() => Math.min(props.first + props.rows, props.total))
</script>

<template>
  <span class="table-footer caption">
    {{ $t('collectionList.range', { from, to, total }) }}
  </span>
</template>

<style scoped>
.table-footer {
  color: var(--muted);
  font-size: 0.875rem;
}
</style>
