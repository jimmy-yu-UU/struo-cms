<script setup lang="ts">
import { computed } from 'vue'
import { ChevronLeft, ChevronRight } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

// Props in, events out, no table instance.
const props = withDefaults(defineProps<{
  page: number
  pageSize: number
  total: number
  /**
   * Override, don't hardcode: `props.pageSize` must be one of these or reka's Select renders with
   * no option selected. Confirmed empirically (mounting Select with a model-value no SelectItem
   * offers): SelectValue's option lookup (rootContext.optionsSet vs. modelValue) finds nothing, so
   * the trigger shows a blank label -- same as its `:placeholder` would, since neither is set here
   * -- and no SelectItem in the open list renders aria-selected/data-state=checked. Unlike a
   * native <select>'s selectedIndex === -1, there is no browser-chosen fallback item; it's just
   * silently blank.
   */
  pageSizeOptions?: number[]
  /** Hides the rows-per-page control. */
  showPageSizeSelector?: boolean
}>(), {
  pageSizeOptions: () => [10, 25, 50, 100],
  showPageSizeSelector: true,
})
const emit = defineEmits<{ 'update:page': [number]; 'update:pageSize': [number] }>()

const pageCount = computed(() => Math.max(1, Math.ceil(props.total / props.pageSize)))
// Humans count from 1; the backend offsets from 0. Reuse collectionList.range so the range
// wording is identical wherever a paged list renders.
const from = computed(() => (props.total === 0 ? 0 : props.page * props.pageSize + 1))
const to = computed(() => Math.min((props.page + 1) * props.pageSize, props.total))

const isFirst = computed(() => props.page <= 0)
const isLast = computed(() => props.page >= pageCount.value - 1)

// reka's SelectRoot models its value as a plain string -- both the `model-value` bound on <Select>
// and each SelectItem's own `value` are strings below -- never the underlying number; the pageSize
// contract everywhere else (DataTableState, buildListQuery) is a number, so the boundary
// conversion still happens once, here. `value: unknown`, not a typed payload: matches
// UiLanguageSwitcher's same-shaped handler (src/components/shell/UiLanguageSwitcher.vue), which
// takes the same `@update:model-value` from this vendored Select.
function onPageSizeChange(value: unknown): void {
  emit('update:pageSize', Number(value))
}
</script>

<template>
  <div class="flex flex-wrap items-center justify-between gap-4 pt-3">
    <div v-if="props.showPageSizeSelector" class="flex items-center gap-2">
      <span class="text-sm text-muted-foreground">
        {{ $t('common.rowsPerPage') }}
      </span>
      <Select :model-value="String(props.pageSize)" @update:model-value="onPageSizeChange">
        <SelectTrigger data-testid="page-size-select" class="w-20" :aria-label="$t('common.rowsPerPage')">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="size in props.pageSizeOptions" :key="size" :value="String(size)">
            {{ size }}
          </SelectItem>
        </SelectContent>
      </Select>
    </div>
    <span class="text-sm text-muted-foreground">
      {{ $t('collectionList.range', { from, to, total: props.total }) }}
    </span>
    <div class="flex items-center gap-1">
      <Button
        variant="outline" size="icon" data-testid="pagination-prev"
        :disabled="isFirst" :aria-label="$t('common.previous')"
        @click="emit('update:page', props.page - 1)"
      >
        <ChevronLeft class="size-4" aria-hidden="true" />
      </Button>
      <Button
        variant="outline" size="icon" data-testid="pagination-next"
        :disabled="isLast" :aria-label="$t('common.next')"
        @click="emit('update:page', props.page + 1)"
      >
        <ChevronRight class="size-4" aria-hidden="true" />
      </Button>
    </div>
  </div>
</template>
