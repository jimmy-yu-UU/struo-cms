<!-- frontend/src/views/CollectionListView.vue -->
<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import SelectButton from 'primevue/selectbutton'
import ConfirmDialog from 'primevue/confirmdialog'
import { useConfirm } from 'primevue/useconfirm'
import PageHeader from '../components/common/PageHeader.vue'
import ListToolbar from '../components/common/ListToolbar.vue'
import TableFooter from '../components/common/TableFooter.vue'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { useLanguageStore } from '../stores/languageStore'
import { itemsApi } from '../api/itemsApi'
import { selectListColumns } from '../lib/selectListColumns'
import { formatCell } from '../lib/formatCell'
import { deleteKindFor, deleteConfirm, purgeConfirm } from '../lib/deleteAction'
import { createLatestWins } from '../lib/latestWins'
import { debounce } from '../lib/debounce'
import { pickTranslated, type TranslationMap } from '../lib/pickTranslated'
import type { FieldMeta } from '../types/schema'

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
const modeOptions = computed(() => [
  { label: t('collectionList.active'), value: 'active' as const },
  { label: t('collectionList.trash'), value: 'trash' as const },
])
const columns = computed(() => (meta.value ? selectListColumns(meta.value) : []))

const rows = ref<Record<string, unknown>[]>([])
const total = ref(0)
const loading = ref(false)
const error = ref('')
const page = ref(0)
const perPage = ref(25)
const sortField = ref<string | undefined>(undefined)
const sortOrder = ref<1 | -1 | undefined>(undefined)
const search = ref('')

function fieldOf(colField: string): FieldMeta | undefined {
  return meta.value?.fields.find((f) => f.name === colField)
}

function cellValue(row: Record<string, unknown>, field: FieldMeta): unknown {
  if (field.translatable) {
    return pickTranslated(row.translations as TranslationMap, langStore.defaultCode, field.name)
  }
  return row[field.name]
}

function isSelectField(colField: string): boolean {
  return String(fieldOf(colField)?.interface ?? '').toLowerCase() === 'select'
}
function tagSeverity(v: unknown): 'success' | 'warn' | 'secondary' {
  const s = String(v ?? '').toLowerCase()
  if (s === 'published') return 'success'
  if (s === 'archived') return 'warn'
  return 'secondary'
}
function formatDeletedAt(v: unknown): string {
  if (v == null || v === '') return ''
  const d = new Date(String(v))
  return Number.isNaN(d.getTime()) ? String(v) : d.toLocaleString()
}

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
    const sort = sortField.value
      ? sortOrder.value === -1 ? `-${sortField.value}` : sortField.value
      : undefined
    await langStore.load()
    const res = await itemsApi.list(name.value, {
      page: page.value,
      rows: perPage.value,
      sort,
      search: search.value || undefined,
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

function onPage(e: { page: number; rows: number }): void {
  page.value = e.page
  perPage.value = e.rows
  loadItems()
}

function onSort(e: { sortField?: string | ((item: unknown) => string) | null; sortOrder?: number | null }): void {
  sortField.value = typeof e.sortField === 'string' ? e.sortField : undefined
  sortOrder.value = (e.sortOrder as 1 | -1 | null | undefined) ?? undefined
  page.value = 0
  loadItems()
}

// Trailing-edge search debounce via the shared helper; cancelable on unmount / collection switch.
const debouncedSearch = debounce(() => {
  page.value = 0
  loadItems()
}, 300)
function onSearchInput(value: string): void {
  search.value = value
  debouncedSearch()
}

function onEdit(row: Record<string, unknown>): void {
  const rid = row.id
  if (rid != null) router.push({ name: 'collection-item', params: { name: name.value, id: String(rid) } })
}

function onNew(): void {
  router.push({ name: 'collection-create', params: { name: name.value } })
}

function setMode(m: 'active' | 'trash'): void {
  mode.value = m
  page.value = 0
  loadItems()
}

function rowId(row: Record<string, unknown>): string {
  return String(row.id)
}

async function runAction(fn: () => Promise<void>): Promise<void> {
  error.value = ''
  try {
    await fn()
    await loadItems()
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('common.actionFailed')
  }
}

function onDelete(row: Record<string, unknown>): void {
  const kind = deleteKindFor(meta.value)
  const { header, message } = deleteConfirm(t, kind)
  confirm.require({ header, message, accept: () => runAction(() => itemsApi.remove(name.value, rowId(row))) })
}

function onPurge(row: Record<string, unknown>): void {
  const { header, message } = purgeConfirm(t)
  confirm.require({ header, message, accept: () => runAction(() => itemsApi.remove(name.value, rowId(row), { purge: true })) })
}

async function onRestore(row: Record<string, unknown>): Promise<void> {
  await runAction(async () => { await itemsApi.restore(name.value, rowId(row)) })
}

watch(name, () => {
  debouncedSearch.cancel() // a pending search must not hit the collection we just switched away from
  page.value = 0
  sortField.value = undefined
  sortOrder.value = undefined
  search.value = ''
  mode.value = 'active'
  loadItems()
})

onMounted(loadItems)
onUnmounted(() => debouncedSearch.cancel())

defineExpose({ loadItems, onPage, onSort, onSearchInput, onEdit, onNew, canWrite, canDelete,
  mode, setMode, showTrashSwitch, onDelete, onRestore, onPurge, rows, total, loading, error, cellValue })
</script>

<template>
  <section class="collection-list">
    <ConfirmDialog />
    <template v-if="!meta">
      <p class="notice">{{ t('collectionList.notFound') }}</p>
    </template>
    <template v-else-if="!canRead">
      <p class="notice">{{ t('collectionList.noAccess') }}</p>
    </template>
    <template v-else>
      <PageHeader :title="meta.label" :caption="t('collectionList.count', { n: total })">
        <template #actions>
          <Button v-if="canWrite" :label="t('collectionList.new')" icon="pi pi-plus" @click="onNew" />
        </template>
      </PageHeader>

      <ListToolbar
        :search-value="search"
        :search-placeholder="t('collectionList.searchPlaceholder')"
        @search="onSearchInput"
      >
        <template #filters>
          <SelectButton
            v-if="showTrashSwitch"
            :model-value="mode"
            :options="modeOptions"
            option-label="label"
            option-value="value"
            :allow-empty="false"
            @update:model-value="setMode($event)"
          />
        </template>
      </ListToolbar>

      <p v-if="mode === 'trash'" class="trash-banner" role="status">
        <i class="pi pi-trash" aria-hidden="true" /> {{ t('collectionList.trashNotice') }}
      </p>

      <p v-if="error" class="error" role="alert">{{ error }}</p>

      <div class="table-scroll">
        <DataTable
          :value="rows"
          lazy
          paginator
          :rows="perPage"
          :total-records="total"
          :loading="loading"
          @page="onPage"
          @sort="onSort"
        >
          <Column
            v-for="col in columns"
            :key="col.field"
            :field="col.field"
            :header="col.header"
            :sortable="col.sortable"
          >
            <template #body="{ data }">
              <Tag
                v-if="isSelectField(col.field) && cellValue(data, fieldOf(col.field)!) != null && cellValue(data, fieldOf(col.field)!) !== ''"
                :value="formatCell(cellValue(data, fieldOf(col.field)!), fieldOf(col.field)!)"
                :severity="tagSeverity(cellValue(data, fieldOf(col.field)!))"
              />
              <span v-else>
                {{ formatCell(cellValue(data, fieldOf(col.field)!), fieldOf(col.field)!) }}
              </span>
            </template>
          </Column>
          <Column v-if="mode === 'trash'" field="deletedAt" :header="t('collectionList.deletedAt')">
            <template #body="{ data }">
              <span class="datetime">{{ formatDeletedAt(data.deletedAt) }}</span>
            </template>
          </Column>
          <Column v-if="canWrite || canDelete" header="" :style="{ width: '8rem' }">
            <template #body="{ data }">
              <template v-if="mode === 'active'">
                <Button v-if="canWrite" icon="pi pi-pencil" text rounded size="small"
                        :title="t('collectionList.edit')" :aria-label="t('collectionList.edit')"
                        @click="onEdit(data)" />
                <Button v-if="canDelete" icon="pi pi-trash" severity="danger" text rounded size="small"
                        :title="t('collectionList.delete')" :aria-label="t('collectionList.delete')"
                        @click="onDelete(data)" />
              </template>
              <template v-else>
                <Button v-if="canDelete" icon="pi pi-undo" text rounded size="small"
                        :title="t('collectionList.restore')" :aria-label="t('collectionList.restore')"
                        @click="onRestore(data)" />
                <Button v-if="canDelete" icon="pi pi-trash" severity="danger" text rounded size="small"
                        :title="t('collectionList.purge')" :aria-label="t('collectionList.purge')"
                        @click="onPurge(data)" />
              </template>
            </template>
          </Column>
          <template #empty>{{ t(mode === 'trash' ? 'collectionList.emptyTrash' : 'collectionList.empty') }}</template>
          <template #paginatorstart>
            <TableFooter :first="page * perPage" :rows="perPage" :total="total" />
          </template>
        </DataTable>
      </div>
    </template>
  </section>
</template>

<style scoped>
/* Wide tables scroll inside their own container; the page itself never scrolls sideways. */
.table-scroll { overflow-x: auto; }
.trash-banner {
  display: flex; align-items: center; gap: 8px; margin: 0 0 12px;
  padding: 10px 14px; border: 1px solid var(--warn, #d97706);
  background: color-mix(in srgb, var(--warn, #d97706) 10%, var(--surface));
  border-radius: var(--radius, 8px); color: var(--fg); font-size: .9rem;
}
.datetime { font-variant-numeric: tabular-nums; color: var(--muted); }
</style>
