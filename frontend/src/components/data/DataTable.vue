<script lang="ts">
import {
  tableFeatures, rowSortingFeature, rowPaginationFeature, columnVisibilityFeature,
  createSortedRowModel, createPaginatedRowModel,
  type ColumnDef as TanstackColumnDef,
} from '@tanstack/vue-table'
import type { SortEntry } from './SortableHeader.vue'

// Three features. Filtering is server-side and lives in FilterBuilder, which speaks the
// backend's flat filter[field][op]=value DSL directly — routing it through TanStack's
// ColumnFiltersState would buy nothing and cost a translation layer (see the spec deviation
// note at the top of this plan).
//
// sortedRowModel/paginatedRowModel ARE registered (unlike a read-only table) precisely so that
// manualSorting/manualPagination are load-bearing: table-core's row-model pipeline only
// consults `options.manualSorting` when a `sortedRowModel` factory exists at all — with no
// factory registered, dropping the flag would be silently inert instead of visibly wrong.
// Registering the factories AND setting manual=true is what makes "drop the flag" an actual,
// catchable regression rather than a no-op.
//
// columnVisibilityFeature needs no row-model factory (hiding a column isn't a row-model stage),
// but it is NOT a no-op to omit — the same "registering matters" trap, one level down. Read
// straight from the vendored source (node_modules/@tanstack/table-core, table-core@9.1.2):
//   - constructTable.js: `table.initialState` is the fold of every registered feature's
//     `getInitialState`, and `table.atoms[key]` is only created for keys that appear in
//     `table.initialState`. columnVisibilityFeature.getInitialState seeds `columnVisibility: {}`
//     — miss the registration and `table.atoms.columnVisibility` never exists.
//   - columnVisibilityFeature.utils.js `column_getIsVisible`: "const columnVisibility =
//     column.table.atoms.columnVisibility?.get(); if (!columnVisibility) return true;" — with no
//     atom, EVERY column reads back visible, permanently, no matter what state you try to write.
//   - utils.js `setStateSlice`: "const onChange = instance.options[onChangeKey]; if (!onChange)
//     return;" — `onColumnVisibilityChange` only exists because
//     columnVisibilityFeature.getDefaultTableOptions supplies it at construction time
//     (constructTable.js folds `feature.getDefaultTableOptions?.(table)` over the registered
//     features list). Miss the registration and a hide request is silently swallowed here too —
//     the exact same "silent no-op" shape as the manualSorting/manualPagination trap above, not
//     a variant of it.
//   - assignColumnPrototype/assignRowPrototype (columnVisibilityFeature.js) are what actually put
//     `getIsVisible`/`toggleVisibility`/`getCanHide` on every column and `getVisibleCells` on
//     every row. Without registration these methods don't exist on the resolved objects at all —
//     calling them throws, rather than quietly doing nothing.
// Proven by sabotage: commenting `columnVisibilityFeature` out of this call and re-running
// DataTable.test.ts's "hides a column" test turns it red (columns stay visible; the template's
// `column.toggleVisibility` call throws) — see the task report for the exact before/after run.
const features = tableFeatures({
  rowSortingFeature,
  rowPaginationFeature,
  columnVisibilityFeature,
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
import { computed } from 'vue'
import { FlexRender, useTable } from '@tanstack/vue-table'
import { useI18n } from 'vue-i18n'
import { Columns3 } from '@lucide/vue'
import { Table, TableBody, TableCell, TableHeader, TableRow } from '@/components/ui/table'
import { Skeleton } from '@/components/ui/skeleton'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu, DropdownMenuCheckboxItem, DropdownMenuContent,
  DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
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

const { t } = useI18n()

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
  // Falls back to the column id when the header string is empty (e.g. CollectionListView's
  // actions column, header: '') — that string doubles as the label a consumer reads in the
  // column-visibility menu below, where a blank menu item would be useless.
  if (typeof columnDef.header === 'string' && columnDef.header !== '') return columnDef.header
  return String(columnDef.id ?? '')
}

function isSortable(columnDef: DataTableColumn<TRow>): boolean {
  return (columnDef.meta as { sortable?: boolean } | undefined)?.sortable === true
}

// Deliberately NOT part of DataTableState: unlike sort/page/pageSize, which the backend query
// needs, which columns are shown is a pure client-side render decision. Left as TanStack's own
// internal (uncontrolled) state — see the `features` comment above for why registering the
// feature is what makes that state exist at all.
const hideableColumns = computed(() => table.getAllLeafColumns().filter((c) => c.getCanHide()))

// Guards the "must not allow hiding every column" rule from a single per-column checkbox: a
// column may always be turned back ON, but turning the LAST visible one OFF is blocked. Reads
// getVisibleLeafColumns() fresh each render so it stays correct as other columns toggle.
function isLastVisible(column: ReturnType<typeof table.getAllLeafColumns>[number]): boolean {
  return column.getIsVisible() && table.getVisibleLeafColumns().length <= 1
}
</script>

<template>
  <div class="space-y-2">
    <div class="flex justify-end">
      <DropdownMenu>
        <DropdownMenuTrigger as-child>
          <Button variant="outline" size="sm" data-testid="column-visibility-trigger">
            <Columns3 class="size-4" aria-hidden="true" />
            {{ t('common.columns') }}
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuLabel>{{ t('common.columns') }}</DropdownMenuLabel>
          <DropdownMenuSeparator />
          <!--
            One checkbox item per hideable leaf column, driven off the table-resolved column
            objects (not props.columns) for the same reason the header loop below is: it's the
            table's own getIsVisible()/toggleVisibility() that stays in sync with what actually
            renders. `disabled` on the item -- not a confirm, not a toast -- is what enforces
            "must not allow hiding every column": the last visible column's checkbox simply can't
            be unchecked, so the table can never end up with zero visible columns.
          -->
          <DropdownMenuCheckboxItem
            v-for="column in hideableColumns"
            :key="column.id"
            :model-value="column.getIsVisible()"
            :disabled="isLastVisible(column)"
            @update:model-value="(v) => column.toggleVisibility(Boolean(v))"
          >
            {{ headerLabel(column.columnDef) }}
          </DropdownMenuCheckboxItem>
        </DropdownMenuContent>
      </DropdownMenu>
    </div>

    <div
      data-testid="data-table-container"
      class="relative overflow-x-auto rounded-md border shadow-sm dark:shadow-none"
      :aria-busy="props.loading ? 'true' : 'false'"
    >
      <!--
        The PrimeVue DataTable this replaced showed a spinner overlay for the same `loading` prop;
        aria-busy alone is invisible without assistive tech, so a sighted user pressing Search or
        changing page saw stale rows with no feedback until they swapped. A dimmed body plus a
        status overlay restores that affordance for every consumer, not just this one screen.
      -->
      <div
        v-if="props.loading"
        role="status"
        class="absolute inset-0 z-10 flex items-center justify-center bg-background/60"
      >
        <span class="sr-only">{{ t('common.loading') }}</span>
        <Skeleton class="h-9 w-9 rounded-full" />
      </div>
      <Table :class="props.loading ? 'opacity-50' : undefined">
        <TableHeader>
          <!-- brand-spec: header row carries no hover fill of its own (that's a body-row cue) -->
          <TableRow class="hover:bg-transparent">
            <!--
              Iterate table-resolved headers, not raw `props.columns`. TanStack derives a column id
              from `accessorKey` when the column def omits `id`, and cell rendering (below, via
              row.getVisibleCells()) already uses that resolved id. Reading `column.id` off the
              column DEF here instead would silently be `undefined` for any id-less column — a
              header click would then emit a bogus sort field, and `:key` would collide across
              every such column. Driving both header and body from the same table-resolved source
              makes them structurally unable to diverge. table.getHeaderGroups() already filters to
              visible columns on its own (buildHeaderGroups.js reads column_getIsVisible directly)
              — no extra filtering needed here.
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
          <TableRow v-if="props.rows.length === 0" class="hover:bg-transparent">
            <!--
              getVisibleLeafColumns(), not getAllLeafColumns(): once columns can hide, the colspan
              must shrink with them or the empty-state cell would visually overhang past the
              remaining header cells.
            -->
            <TableCell
              :colspan="table.getVisibleLeafColumns().length"
              class="h-24 px-4 py-3 text-center text-muted-foreground"
            >
              {{ props.emptyMessage }}
            </TableCell>
          </TableRow>
          <TableRow
            v-for="row in table.getRowModel().rows"
            :key="row.id"
            class="transition-colors duration-150 ease-[cubic-bezier(0.2,0,0,1)] motion-reduce:transition-none hover:bg-foreground/[0.04]"
          >
            <!--
              getVisibleCells(), not getAllCells(): now that columnVisibilityFeature is registered
              (see the `features` comment up top), getVisibleCells() exists and is what keeps a
              hidden column's cells out of the body in lockstep with its header being gone above —
              getAllCells() would render an orphan <td> with no matching <th>. An earlier round of
              this table found getVisibleCells() didn't exist at all (assignRowPrototype never ran)
              before the feature was registered; it's the same "registering matters" fact as the
              header comment, from the row side.
            -->
            <TableCell v-for="cell in row.getVisibleCells()" :key="cell.id" class="px-4 py-3">
              <FlexRender :cell="cell" />
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </div>
  </div>
</template>
