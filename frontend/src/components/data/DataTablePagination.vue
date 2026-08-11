<script setup lang="ts">
import { computed, useId } from 'vue'
import { ChevronLeft, ChevronRight } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { NativeSelect, NativeSelectOption } from '@/components/ui/native-select'

// Props in, events out, no table instance -- kept standalone deliberately so MediaLibraryView
// (a future consumer with no DataTable/TanStack instance at all) can reuse this unchanged.
const props = withDefaults(defineProps<{
  page: number
  pageSize: number
  total: number
  /**
   * A RelatedList embedded in an ItemForm will typically want a smaller default (5 or 10) than
   * CollectionListView's 10/25/50/100 -- override, don't hardcode. `props.pageSize` must be one
   * of these or the native <select> renders with no option selected (selectedIndex === -1).
   */
  pageSizeOptions?: number[]
  /** MediaLibraryView's grid and a compact RelatedList don't want the rows-per-page control. */
  showPageSizeSelector?: boolean
}>(), {
  pageSizeOptions: () => [10, 25, 50, 100],
  showPageSizeSelector: true,
})
const emit = defineEmits<{ 'update:page': [number]; 'update:pageSize': [number] }>()

const pageCount = computed(() => Math.max(1, Math.ceil(props.total / props.pageSize)))
// Humans count from 1; the backend offsets from 0. Reuse collectionList.range so the wording
// stays identical to the TableFooter this replaces.
const from = computed(() => (props.total === 0 ? 0 : props.page * props.pageSize + 1))
const to = computed(() => Math.min((props.page + 1) * props.pageSize, props.total))

const isFirst = computed(() => props.page <= 0)
const isLast = computed(() => props.page >= pageCount.value - 1)

// useId(), not a literal string: a future screen (RelatedList inside an ItemForm, say) can mount
// more than one DataTablePagination per page, and a duplicate id would break the label's `for`
// association for every instance after the first.
const pageSizeId = useId()

// A plain `@change`, not `@update:model-value`: NativeSelect's own `defineEmits<{
// "update:modelValue": AcceptableValue }>()` (src/components/ui/native-select/NativeSelect.vue,
// vendored/read-only) types that payload as a bare value rather than a tuple, which vue-tsc then
// widens the listener's inferred prop type to `() => any` (zero args) -- an
// `(value: unknown) => void` handler fails `pnpm build`'s type check against that. The vendored
// Calendar.vue component hits the exact same NativeSelect and works around it the same way: read
// the native DOM event instead of the v-model payload.
//
// Native <select> values are always strings (HTMLOptionElement.value coerces on read); the
// pageSize contract everywhere else (DataTableState, buildListQuery) is a number, so the
// boundary conversion happens once, here.
function onPageSizeChange(e: Event): void {
  emit('update:pageSize', Number((e.target as HTMLSelectElement).value))
}
</script>

<template>
  <div class="flex flex-wrap items-center justify-between gap-4 pt-3">
    <div v-if="props.showPageSizeSelector" class="flex items-center gap-2">
      <label :for="pageSizeId" class="text-sm text-muted-foreground">
        {{ $t('common.rowsPerPage') }}
      </label>
      <NativeSelect
        :id="pageSizeId"
        data-testid="page-size-select"
        :model-value="String(props.pageSize)"
        class="h-9 w-20"
        @change="onPageSizeChange"
      >
        <NativeSelectOption v-for="size in props.pageSizeOptions" :key="size" :value="String(size)">
          {{ size }}
        </NativeSelectOption>
      </NativeSelect>
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
