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
  <TableHead
    class="h-auto px-4 py-3 text-muted-foreground hover:bg-transparent"
    :aria-sort="props.sortable ? ariaSort : undefined"
  >
    <!--
      brand-spec table rule: header cells are 14px/weight-500, muted colour, no background fill.
      text-sm/font-medium already come from TableHead's own vendored defaults (font-medium is
      already weight 500) — h-auto/py-3/px-4 replace its fixed h-10/px-2 so header cells share the
      same 12px/16px padding as body cells and the columns line up; text-muted-foreground
      overrides its default text-foreground for the muted colour. hover:bg-transparent isn't the
      row's job here (the parent TableRow already carries that), just belt-and-suspenders against
      the vendored TableHead itself ever picking up a hover fill.
      NOTE: this comment lives INSIDE <TableHead>, not as a sibling before it -- a root-level
      comment sibling turns the template's root into a multi-node Fragment, and Vue then resolves
      this component's $el to that comment node instead of the <th>, which silently broke
      `wrapper.attributes('aria-sort')` in SortableHeader.test.ts (returned undefined) until this
      was moved inside.
    -->
    <template v-if="!props.sortable">{{ props.label }}</template>
    <button
      v-else
      type="button"
      class="inline-flex items-center gap-1 transition-colors duration-150 ease-[cubic-bezier(0.2,0,0,1)] motion-reduce:transition-none hover:text-foreground"
      :title="actionLabel"
      :aria-label="`${props.label} — ${actionLabel}`"
      @click="cycle"
    >
      <span>{{ props.label }}</span>
      <component :is="Icon" class="size-3.5" aria-hidden="true" />
    </button>
  </TableHead>
</template>
