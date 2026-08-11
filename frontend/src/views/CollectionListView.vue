<!-- frontend/src/views/CollectionListView.vue -->
<!-- No route-params watcher: AppShell's keyed <router-view> remounts this view on a collection
     switch (see AppShell.vue's :key="route.path" comment). -->
<script setup lang="ts">
import { ref, computed, onMounted, h } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { Plus, Pencil, Eye, Trash2, Undo2 } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import DataTable, { type DataTableColumn, type DataTableState, toSortParam } from '@/components/data/DataTable.vue'
import DataTablePagination from '@/components/data/DataTablePagination.vue'
import FilterBuilder, { type FilterField } from '@/components/data/FilterBuilder.vue'
import PageHeader from '../components/common/PageHeader.vue'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { useLanguageStore } from '../stores/languageStore'
import { useConfirm } from '@/composables/useConfirm'
import { itemsApi } from '../api/itemsApi'
import { selectListColumns, type ColumnDef as ListColumn } from '../lib/selectListColumns'
import { formatCell } from '../lib/formatCell'
import { deleteKindFor, deleteConfirm, purgeConfirm } from '../lib/deleteAction'
import { createLatestWins } from '../lib/latestWins'
import { pickTranslated, type TranslationMap } from '../lib/pickTranslated'
import { LANGUAGE_COLLECTION } from '../lib/frameworkCollections'
import type { FilterSpec } from '@/lib/buildListQuery'
import type { FieldMeta } from '../types/schema'

type Row = Record<string, unknown>

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const schema = useSchemaStore()
const langStore = useLanguageStore()
const confirm = useConfirm()
const { t } = useI18n()

const name = computed(() => route.params.name as string)
const meta = computed(() => schema.get(name.value))
const canRead = computed(() => auth.canRead(name.value))
const canWrite = computed(() => auth.canWrite(name.value))
const canDelete = computed(() => auth.canDelete(name.value))
const mode = ref<'active' | 'trash'>('active')
const showTrashSwitch = computed(() => !!meta.value?.softDelete && canDelete.value)
const columns = computed(() => (meta.value ? selectListColumns(meta.value) : []))

const rows = ref<Row[]>([])
const total = ref(0)
const loading = ref(false)
const error = ref('')
const tableState = ref<DataTableState>({ sort: [], page: 0, pageSize: 25 })
const filters = ref<FilterSpec>({})

function fieldOf(colField: string): FieldMeta | undefined {
  return meta.value?.fields.find((f) => f.name === colField)
}

function cellValue(row: Row, field: FieldMeta): unknown {
  if (field.translatable) {
    return pickTranslated(row.translations as TranslationMap, langStore.defaultCode, field.name)
  }
  return row[field.name]
}

function isSelectField(colField: string): boolean {
  return String(fieldOf(colField)?.interface ?? '').toLowerCase() === 'select'
}
function tagSeverity(v: unknown): 'success' | 'secondary' {
  const s = String(v ?? '').toLowerCase()
  if (s === 'published') return 'success'
  return 'secondary'
}
function formatDeletedAt(v: unknown): string {
  if (v == null || v === '') return ''
  const d = new Date(String(v))
  return Number.isNaN(d.getTime()) ? String(v) : d.toLocaleString()
}

// Fields FilterBuilder can offer: the display columns first, then every searchable field that did
// not make the ≤6-column display cut. selectListColumns filters by DISPLAY eligibility (a RichText
// body or a 7th-ranked slug never appears), while Searchable is what the backend's `search=`
// honours and what the manual tells collection authors governs free-text search — so without this
// second group those fields are unreachable from the list. Hidden fields are excluded because
// QueryValidator refuses to filter them at all. An enum (select) field surfaces its options so
// FilterBuilder renders a Select instead of a free-text Input for it.
const filterFields = computed<FilterField[]>(() => {
  const listed = columns.value.map((c: ListColumn) => {
    const field = fieldOf(c.field)
    const choices = (field?.options as { label: string; value: string }[] | undefined)
    return { name: c.field, label: c.header, options: isSelectField(c.field) ? choices : undefined }
  })
  const listedNames = new Set(listed.map((f) => f.name))
  const searchableOnly = (meta.value?.fields ?? [])
    .filter((f) => f.searchable && !f.hidden && !f.isSystem && !listedNames.has(f.name))
    .map((f) => ({ name: f.name, label: f.label, options: undefined }))
  return [...listed, ...searchableOnly]
})

function actionButtons(row: Row) {
  const buttons = []
  if (mode.value === 'active') {
    buttons.push(h(Button, {
      variant: 'ghost', size: 'icon', 'data-testid': canWrite.value ? 'row-edit' : 'row-view',
      title: canWrite.value ? t('collectionList.edit') : t('collectionList.view'),
      'aria-label': canWrite.value ? t('collectionList.edit') : t('collectionList.view'),
      onClick: () => onEdit(row),
    }, () => h(canWrite.value ? Pencil : Eye, { class: 'size-4' })))
    if (canDelete.value) {
      buttons.push(h(Button, {
        variant: 'ghost', size: 'icon', 'data-testid': 'row-delete',
        title: t('collectionList.delete'), 'aria-label': t('collectionList.delete'),
        onClick: () => onDelete(row),
      }, () => h(Trash2, { class: 'size-4 text-destructive' })))
    }
  } else if (canDelete.value) {
    buttons.push(h(Button, {
      variant: 'ghost', size: 'icon', 'data-testid': 'row-restore',
      title: t('collectionList.restore'), 'aria-label': t('collectionList.restore'),
      onClick: () => onRestore(row),
    }, () => h(Undo2, { class: 'size-4' })))
    buttons.push(h(Button, {
      variant: 'ghost', size: 'icon', 'data-testid': 'row-purge',
      title: t('collectionList.purge'), 'aria-label': t('collectionList.purge'),
      onClick: () => onPurge(row),
    }, () => h(Trash2, { class: 'size-4 text-destructive' })))
  }
  return buttons
}

// TanStack columns, rebuilt from selectListColumns' picks plus the trash-only deletedAt column
// and the row-actions column; formatCell/cellValue/tagSeverity supply each cell's rendered value
// via a `cell` render function.
const tableColumns = computed<DataTableColumn<Row>[]>(() => {
  const cols: DataTableColumn<Row>[] = columns.value.map((c: ListColumn) => ({
    id: c.field,
    accessorKey: c.field,
    header: c.header,
    meta: { sortable: c.sortable },
    cell: ({ row }) => {
      const field = fieldOf(c.field)
      if (!field) return ''
      const value = cellValue(row.original, field)
      const text = formatCell(value, field)
      if (isSelectField(c.field) && value != null && value !== '') {
        const success = tagSeverity(value) === 'success'
        return h(Badge, {
          variant: 'outline',
          class: success ? 'border-success/40 bg-success/10 text-success' : 'text-muted-foreground',
        }, () => text)
      }
      return text
    },
  }))

  if (mode.value === 'trash') {
    cols.push({
      id: 'deletedAt',
      accessorKey: 'deletedAt',
      header: t('collectionList.deletedAt'),
      cell: ({ row }) => h('span', { class: 'tabular-nums text-muted-foreground' }, formatDeletedAt(row.original.deletedAt)),
    })
  }

  if (canRead.value || canDelete.value) {
    cols.push({
      id: 'actions',
      header: '',
      cell: ({ row }) => h('div', { class: 'flex items-center gap-1' }, actionButtons(row.original)),
    })
  }

  return cols
})

// Latest-wins guard: a slow earlier load must not clobber a newer one's state.
const listLoad = createLatestWins()

async function loadItems(): Promise<void> {
  const token = listLoad.next()
  await schema.load() // dedup via store's `loaded` flag; retries on hard refresh/deep link where the
  // parent's schema.load() hasn't resolved yet when this view's onMounted(loadItems) fires
  if (!meta.value || !canRead.value) {
    // A bail still bumped the token above, so if a prior in-flight load turned the
    // spinner on, this call has orphaned it — its finally now sees a stale token and
    // will skip loading=false. Clear it here when we are the current load.
    if (listLoad.isCurrent(token)) loading.value = false
    return
  }
  loading.value = true
  error.value = ''
  try {
    const sort = toSortParam(tableState.value.sort)
    await langStore.load()
    const res = await itemsApi.list(name.value, {
      page: tableState.value.page,
      rows: tableState.value.pageSize,
      sort,
      filter: Object.keys(filters.value).length ? filters.value : undefined,
      locale: langStore.defaultCode || undefined,
      deleted: mode.value === 'trash' ? 'only' : undefined,
    })
    if (!listLoad.isCurrent(token)) return
    rows.value = res.data
    total.value = res.total
  } catch (e) {
    if (!listLoad.isCurrent(token)) return
    error.value = e instanceof Error ? e.message : t('common.loadFailed')
    rows.value = []
    total.value = 0
  } finally {
    // Only the current load may clear the spinner; a stale response must not
    // turn off the spinner of the newer request still in flight.
    if (listLoad.isCurrent(token)) loading.value = false
  }
}

function onTableState(next: DataTableState): void {
  tableState.value = next
  loadItems()
}
function onPageChange(page: number): void {
  tableState.value = { ...tableState.value, page }
  loadItems()
}
function onPageSizeChange(pageSize: number): void {
  // An offset computed against the OLD page size is meaningless against the new one (e.g. page 3
  // at 25/page starts at row 75; at 100/page that's off the end of the result set) -- always
  // return to the first page when the size changes.
  tableState.value = { ...tableState.value, page: 0, pageSize }
  loadItems()
}
function onFilterApply(spec: FilterSpec): void {
  filters.value = spec
  // A new filter invalidates the current offset.
  tableState.value = { ...tableState.value, page: 0 }
  loadItems()
}

function onEdit(row: Row): void {
  const rid = row.id
  if (rid != null) router.push({ name: 'collection-item', params: { name: name.value, id: String(rid) } })
}

function onNew(): void {
  router.push({ name: 'collection-create', params: { name: name.value } })
}

function setMode(m: 'active' | 'trash'): void {
  mode.value = m
  tableState.value = { ...tableState.value, page: 0 }
  loadItems()
}
function onModeToggle(value: unknown): void {
  // reka's single-type ToggleGroup emits undefined when the pressed item is clicked again
  // (deselect) -- the trash switch has no "neither" state, so ignore that.
  if (value === 'active' || value === 'trash') setMode(value)
}

function rowId(row: Row): string {
  return String(row.id)
}

async function runAction(fn: () => Promise<void>): Promise<void> {
  error.value = ''
  try {
    await fn()
    await loadItems()
    // Mutating a Language row changes which locale tabs every other form/list shows.
    if (name.value === LANGUAGE_COLLECTION) await langStore.reload()
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('common.actionFailed')
  }
}

async function onDelete(row: Row): Promise<void> {
  const kind = deleteKindFor(meta.value)
  const { header, message } = deleteConfirm(t, kind)
  if (await confirm.require({ header, message, severity: 'danger' })) {
    await runAction(() => itemsApi.remove(name.value, rowId(row)))
  }
}

async function onPurge(row: Row): Promise<void> {
  const { header, message } = purgeConfirm(t)
  if (await confirm.require({ header, message, severity: 'danger' })) {
    await runAction(() => itemsApi.remove(name.value, rowId(row), { purge: true }))
  }
}

async function onRestore(row: Row): Promise<void> {
  await runAction(async () => { await itemsApi.restore(name.value, rowId(row)) })
}

onMounted(loadItems)

defineExpose({ loadItems, onTableState, onPageChange, onPageSizeChange, onFilterApply, onEdit, onNew,
  canWrite, canDelete, mode, setMode, showTrashSwitch, onDelete, onRestore, onPurge, rows, total, loading,
  error, cellValue, tableState, filters })
</script>

<template>
  <section class="collection-list">
    <template v-if="!meta">
      <p class="notice">{{ t('collectionList.notFound') }}</p>
    </template>
    <template v-else-if="!canRead">
      <p class="notice">{{ t('collectionList.noAccess') }}</p>
    </template>
    <template v-else>
      <PageHeader :title="meta.label" :caption="t('collectionList.count', { n: total })">
        <template #actions>
          <Button v-if="canWrite" @click="onNew">
            <Plus class="size-4" aria-hidden="true" />
            {{ t('collectionList.new') }}
          </Button>
        </template>
      </PageHeader>

      <div class="mb-4 flex flex-wrap items-start justify-between gap-3">
        <FilterBuilder :key="name" :fields="filterFields" :applied="filters" @apply="onFilterApply" />
        <ToggleGroup
          v-if="showTrashSwitch"
          type="single"
          :model-value="mode"
          variant="outline"
          @update:model-value="onModeToggle"
        >
          <ToggleGroupItem value="active">{{ t('collectionList.active') }}</ToggleGroupItem>
          <ToggleGroupItem value="trash">{{ t('collectionList.trash') }}</ToggleGroupItem>
        </ToggleGroup>
      </div>

      <p
        v-if="mode === 'trash'"
        role="status"
        class="mb-3 flex items-center gap-2 rounded-md border border-warning bg-warning/10 px-3.5 py-2.5 text-sm"
      >
        <Trash2 class="size-4" aria-hidden="true" />
        {{ t('collectionList.trashNotice') }}
      </p>

      <p v-if="error" class="error" role="alert">{{ error }}</p>

      <DataTable
        :columns="tableColumns"
        :rows="rows"
        :total="total"
        :state="tableState"
        :loading="loading"
        :empty-message="t(mode === 'trash' ? 'collectionList.emptyTrash' : 'collectionList.empty')"
        @update:state="onTableState"
      />
      <DataTablePagination
        :page="tableState.page"
        :page-size="tableState.pageSize"
        :total="total"
        @update:page="onPageChange"
        @update:page-size="onPageSizeChange"
      />
    </template>
  </section>
</template>
