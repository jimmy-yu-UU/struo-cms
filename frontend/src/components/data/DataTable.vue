<script setup lang="ts" generic="TRow extends Record<string, unknown>">
import {
  FlexRender, useTable, tableFeatures, rowSortingFeature, rowPaginationFeature,
  createSortedRowModel, createPaginatedRowModel,
  type ColumnDef as TanstackColumnDef,
} from '@tanstack/vue-table'
import { Table, TableBody, TableCell, TableHeader, TableRow } from '@/components/ui/table'
import SortableHeader, { type SortEntry } from './SortableHeader.vue'

export type DataTableState = {
  sort: SortEntry[]
  /** 0-based, matching the backend's offset arithmetic in lib/buildListQuery.ts. */
  page: number
  pageSize: number
}

// Only the two features whose STATE we surface. Filtering is server-side and lives in
// FilterBuilder, which speaks the backend's flat filter[field][op]=value DSL directly —
// routing it through TanStack's ColumnFiltersState would buy nothing and cost a translation
// layer (see the spec deviation note at the top of this plan).
//
// sortedRowModel/paginatedRowModel ARE registered (unlike a read-only table) precisely so that
// manualSorting/manualPagination are load-bearing: table-core's row-model pipeline only
// consults `options.manualSorting` when a `sortedRowModel` factory exists at all — with no
// factory registered, dropping the flag would be silently inert instead of visibly wrong.
// Registering the factories AND setting manual=true is what makes "drop the flag" an actual,
// catchable regression rather than a no-op.
const features = tableFeatures({
  rowSortingFeature,
  rowPaginationFeature,
  sortedRowModel: createSortedRowModel(),
  paginatedRowModel: createPaginatedRowModel(),
})

const props = defineProps<{
  columns: TanstackColumnDef<typeof features, TRow, unknown>[]
  rows: TRow[]
  total: number
  state: DataTableState
  loading?: boolean
  emptyMessage?: string
}>()
const emit = defineEmits<{ 'update:state': [DataTableState] }>()

// EVERY manual* flag matters. Miss one and TanStack re-derives that dimension from the
// single page it was handed — the table animates, but shows the wrong rows.
const table = useTable({
  features,
  get data() { return props.rows },
  get columns() { return props.columns },
  manualSorting: true,
  manualPagination: true,
  get rowCount() { return props.total },
  state: {
    get sorting() { return props.state.sort },
    get pagination() { return { pageIndex: props.state.page, pageSize: props.state.pageSize } },
  },
})

function onSort(sort: SortEntry[]): void {
  // A changed sort invalidates the current offset: page 4 of the old ordering is
  // meaningless in the new one.
  emit('update:state', { ...props.state, sort, page: 0 })
}

function headerLabel(column: TanstackColumnDef<typeof features, TRow, unknown>): string {
  return typeof column.header === 'string' ? column.header : String(column.id ?? '')
}

function isSortable(column: TanstackColumnDef<typeof features, TRow, unknown>): boolean {
  return (column.meta as { sortable?: boolean } | undefined)?.sortable === true
}
</script>

<template>
  <div class="overflow-x-auto" :aria-busy="props.loading ? 'true' : 'false'">
    <Table>
      <TableHeader>
        <TableRow>
          <SortableHeader
            v-for="column in props.columns"
            :key="String(column.id)"
            :label="headerLabel(column)"
            :column-id="String(column.id)"
            :sortable="isSortable(column)"
            :sort="props.state.sort"
            @update:sort="onSort"
          />
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow v-if="props.rows.length === 0">
          <TableCell :colspan="props.columns.length" class="h-24 text-center text-muted-foreground">
            {{ props.emptyMessage }}
          </TableCell>
        </TableRow>
        <TableRow v-for="row in table.getRowModel().rows" :key="row.id">
          <!--
            getAllCells(), not getVisibleCells(): the latter belongs to columnVisibilityFeature,
            which isn't part of the `features` this table composes. We don't support hiding
            columns, so the core-provided getAllCells() is the correct (and only available) call.
          -->
          <TableCell v-for="cell in row.getAllCells()" :key="cell.id">
            <FlexRender :cell="cell" />
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>
  </div>
</template>
