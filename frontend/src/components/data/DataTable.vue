<script lang="ts">
import {
  tableFeatures, rowSortingFeature, rowPaginationFeature,
  createSortedRowModel, createPaginatedRowModel,
  type ColumnDef as TanstackColumnDef,
} from '@tanstack/vue-table'
import type { SortEntry } from './SortableHeader.vue'

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
//
// Lives in this plain <script> block, not <script setup>, because it (and the DataTableColumn
// alias below it) must NOT reference the component's own `generic="TRow extends ...">` type
// parameter — vue-tsc's <script setup> macro processing cannot resolve an exported generic type
// alias that shadows the SFC's own generic name.
const features = tableFeatures({
  rowSortingFeature,
  rowPaginationFeature,
  sortedRowModel: createSortedRowModel(),
  paginatedRowModel: createPaginatedRowModel(),
})

// `features` above has no useful name a consumer could otherwise spell out, so this alias is
// what callers building a `columns` array (Task 14's CollectionListView, RelatedList,
// MediaLibraryView) actually declare against.
export type DataTableColumn<TRow extends Record<string, unknown>> = TanstackColumnDef<typeof features, TRow, unknown>

// The backend's `sort` query param (lib/buildListQuery.ts) takes one field, `'title'` for
// ascending or `'-title'` for descending — not TanStack's SortingState shape. Exported so
// consumers don't each re-derive this conversion when building the actual list request.
export function toSortParam(sort: SortEntry[]): string | undefined {
  const active = sort[0]
  return active ? (active.desc ? `-${active.id}` : active.id) : undefined
}
</script>

<script setup lang="ts" generic="TRow extends Record<string, unknown>">
import { FlexRender, useTable } from '@tanstack/vue-table'
import { Table, TableBody, TableCell, TableHeader, TableRow } from '@/components/ui/table'
import SortableHeader from './SortableHeader.vue'

export type DataTableState = {
  sort: SortEntry[]
  /** 0-based, matching the backend's offset arithmetic in lib/buildListQuery.ts. */
  page: number
  pageSize: number
}

const props = defineProps<{
  columns: DataTableColumn<TRow>[]
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

function headerLabel(columnDef: DataTableColumn<TRow>): string {
  return typeof columnDef.header === 'string' ? columnDef.header : String(columnDef.id ?? '')
}

function isSortable(columnDef: DataTableColumn<TRow>): boolean {
  return (columnDef.meta as { sortable?: boolean } | undefined)?.sortable === true
}
</script>

<template>
  <div class="overflow-x-auto" :aria-busy="props.loading ? 'true' : 'false'">
    <Table>
      <TableHeader>
        <TableRow>
          <!--
            Iterate table-resolved headers, not raw `props.columns`. TanStack derives a column id
            from `accessorKey` when the column def omits `id`, and cell rendering (below, via
            row.getAllCells()) already uses that resolved id. Reading `column.id` off the column
            DEF here instead would silently be `undefined` for any id-less column — a header click
            would then emit a bogus sort field, and `:key` would collide across every such column.
            Driving both header and body from the same table-resolved source makes them
            structurally unable to diverge.
          -->
          <SortableHeader
            v-for="header in table.getHeaderGroups()[0]?.headers ?? []"
            :key="header.column.id"
            :label="headerLabel(header.column.columnDef)"
            :column-id="header.column.id"
            :sortable="isSortable(header.column.columnDef)"
            :sort="props.state.sort"
            @update:sort="onSort"
          />
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow v-if="props.rows.length === 0">
          <TableCell :colspan="table.getAllLeafColumns().length" class="h-24 text-center text-muted-foreground">
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
