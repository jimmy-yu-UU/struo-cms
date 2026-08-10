<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { ArrowUp, ArrowDown, ArrowUpDown } from '@lucide/vue'
import { TableHead } from '@/components/ui/table'

export type SortEntry = { id: string; desc: boolean }

const props = defineProps<{
  label: string
  columnId: string
  sortable: boolean
  sort: SortEntry[]
}>()
const emit = defineEmits<{ 'update:sort': [SortEntry[]] }>()

const active = computed(() => props.sort.find((s) => s.id === props.columnId))
const ariaSort = computed(() =>
  !active.value ? 'none' : active.value.desc ? 'descending' : 'ascending',
)
const Icon = computed(() => (!active.value ? ArrowUpDown : active.value.desc ? ArrowDown : ArrowUp))

const { t } = useI18n()
const actionLabel = computed(() =>
  !active.value ? t('common.sortAscending') : active.value.desc ? t('common.clearSort') : t('common.sortDescending'),
)

// Single-column sorting only: the backend's `sort` query param takes one field
// ("title" / "-title"), so a multi-sort UI would promise something it cannot deliver.
function cycle(): void {
  if (!active.value) emit('update:sort', [{ id: props.columnId, desc: false }])
  else if (!active.value.desc) emit('update:sort', [{ id: props.columnId, desc: true }])
  else emit('update:sort', [])
}
</script>

<template>
  <TableHead :aria-sort="props.sortable ? ariaSort : undefined">
    <template v-if="!props.sortable">{{ props.label }}</template>
    <button
      v-else
      type="button"
      class="inline-flex items-center gap-1 hover:text-foreground"
      :title="actionLabel"
      :aria-label="`${props.label} — ${actionLabel}`"
      @click="cycle"
    >
      <span>{{ props.label }}</span>
      <component :is="Icon" class="size-3.5" aria-hidden="true" />
    </button>
  </TableHead>
</template>
