<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { RecentRow } from '../../lib/aggregateRecentUpdates'
import { formatDateTime } from '../../lib/formatDateTime'

defineProps<{ rows: RecentRow[] }>()
defineEmits<{ (e: 'select', row: RecentRow): void }>()
const { t } = useI18n()

function formatUpdated(iso: string | null): string {
  return formatDateTime(iso)
}
</script>

<template>
  <table class="recent">
    <thead>
      <tr>
        <th>{{ t('dashboard.recent.colTitle') }}</th>
        <th>{{ t('dashboard.recent.colCollection') }}</th>
        <th>{{ t('dashboard.recent.colUpdated') }}</th>
      </tr>
    </thead>
    <tbody>
      <tr v-if="rows.length === 0">
        <td class="recent__empty" colspan="3">{{ t('dashboard.recent.empty') }}</td>
      </tr>
      <tr
        v-for="row in rows"
        :key="`${row.collection}:${row.id}`"
        data-test="recent-row"
        class="recent__row"
        @click="$emit('select', row)"
      >
        <td class="recent__title">{{ row.title }}</td>
        <td>{{ row.collectionLabel }}</td>
        <td class="recent__updated">{{ formatUpdated(row.updatedAt) }}</td>
      </tr>
    </tbody>
  </table>
</template>

<style scoped>
.recent {
  width: 100%;
  border-collapse: collapse;
}
.recent th,
.recent td {
  text-align: left;
  padding: 10px 12px;
  border-bottom: 1px solid var(--border);
}
.recent th {
  color: var(--legacy-muted);
  font-size: 0.8rem;
  font-weight: 600;
}
.recent__row {
  cursor: pointer;
}
.recent__row:hover {
  background: var(--bg);
}
.recent__title {
  color: var(--legacy-accent);
  font-weight: 550;
}
.recent__updated {
  font-family: var(--mono);
  color: var(--legacy-muted);
  white-space: nowrap;
}
.recent__empty {
  color: var(--legacy-muted);
  text-align: center;
  padding: 24px 12px;
}
</style>
