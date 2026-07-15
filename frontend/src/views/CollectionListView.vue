<!-- frontend/src/views/CollectionListView.vue -->
<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import InputText from 'primevue/inputtext'
import Button from 'primevue/button'
import SelectButton from 'primevue/selectbutton'
import ConfirmDialog from 'primevue/confirmdialog'
import { useConfirm } from 'primevue/useconfirm'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { useLanguageStore } from '../stores/languageStore'
import { itemsApi } from '../api/itemsApi'
import { selectListColumns } from '../lib/selectListColumns'
import { formatCell } from '../lib/formatCell'
import { deleteKindFor, deleteConfirm, purgeConfirm } from '../lib/deleteAction'
import { createLatestWins } from '../lib/latestWins'
import type { FieldMeta } from '../types/schema'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const schema = useSchemaStore()
const langStore = useLanguageStore()
const confirm = useConfirm()

const name = computed(() => route.params.name as string)
const meta = computed(() => schema.get(name.value))
const canRead = computed(() => auth.canRead(name.value))
const canWrite = computed(() => auth.canWrite(name.value))
const canDelete = computed(() => auth.canDelete(name.value))
const mode = ref<'active' | 'trash'>('active')
const showTrashSwitch = computed(() => !!meta.value?.softDelete && canDelete.value)
const modeOptions = [
  { label: 'Active', value: 'active' as const },
  { label: 'Trash', value: 'trash' as const },
]
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
    const t = (row.translations as Record<string, Record<string, unknown>> | undefined)?.[langStore.defaultCode]
    return t?.[field.name]
  }
  return row[field.name]
}

// Latest-wins guard: a slow earlier load must not clobber a newer one's state.
const listLoad = createLatestWins()

async function loadItems(): Promise<void> {
  const token = listLoad.next()
  await schema.load() // dedup via store's `loaded` flag; retries on hard refresh/deep link where the
  // parent's schema.load() hasn't resolved yet when this view's onMounted(loadItems) fires
  if (!meta.value || !canRead.value) return
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
    error.value = e instanceof Error ? e.message : 'Failed to load items.'
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

let searchTimer: ReturnType<typeof setTimeout> | undefined
function onSearchInput(value: string): void {
  search.value = value
  clearTimeout(searchTimer)
  searchTimer = setTimeout(() => {
    page.value = 0
    loadItems()
  }, 300)
}

function onRowClick(e: { data: Record<string, unknown> }): void {
  if (mode.value === 'trash') return
  const rid = e.data.id
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
    error.value = e instanceof Error ? e.message : 'Action failed.'
  }
}

function onDelete(row: Record<string, unknown>): void {
  const kind = deleteKindFor(meta.value)
  const { header, message } = deleteConfirm(kind)
  confirm.require({ header, message, accept: () => runAction(() => itemsApi.remove(name.value, rowId(row))) })
}

function onPurge(row: Record<string, unknown>): void {
  const { header, message } = purgeConfirm()
  confirm.require({ header, message, accept: () => runAction(() => itemsApi.remove(name.value, rowId(row), { purge: true })) })
}

async function onRestore(row: Record<string, unknown>): Promise<void> {
  await runAction(async () => { await itemsApi.restore(name.value, rowId(row)) })
}

watch(name, () => {
  page.value = 0
  sortField.value = undefined
  sortOrder.value = undefined
  search.value = ''
  mode.value = 'active'
  loadItems()
})

onMounted(loadItems)

defineExpose({ loadItems, onPage, onSort, onSearchInput, onRowClick, onNew, canWrite, canDelete,
  mode, setMode, showTrashSwitch, onDelete, onRestore, onPurge, rows, total, loading, error, cellValue })
</script>

<template>
  <section class="collection-list">
    <ConfirmDialog />
    <template v-if="!meta">
      <p class="notice">Collection not found.</p>
    </template>
    <template v-else-if="!canRead">
      <p class="notice">You don't have access to this collection.</p>
    </template>
    <template v-else>
      <header class="list-header">
        <h2>{{ meta.label }}</h2>
        <SelectButton
          v-if="showTrashSwitch"
          :model-value="mode"
          :options="modeOptions"
          option-label="label"
          option-value="value"
          :allow-empty="false"
          @update:model-value="setMode($event)"
        />
        <InputText
          type="text"
          placeholder="Search"
          @input="onSearchInput(($event.target as HTMLInputElement).value)"
        />
        <Button v-if="canWrite" label="New" icon="pi pi-plus" @click="onNew" />
      </header>

      <p v-if="error" class="error" role="alert">{{ error }}</p>

      <DataTable
        :value="rows"
        lazy
        paginator
        :rows="perPage"
        :total-records="total"
        :loading="loading"
        @page="onPage"
        @sort="onSort"
        @row-click="onRowClick"
      >
        <Column
          v-for="col in columns"
          :key="col.field"
          :field="col.field"
          :header="col.header"
          :sortable="col.sortable"
        >
          <template #body="{ data }">
            {{ formatCell(cellValue(data, fieldOf(col.field)!), fieldOf(col.field)!) }}
          </template>
        </Column>
        <Column v-if="canDelete" header="" :style="{ width: '12rem' }">
          <template #body="{ data }">
            <template v-if="mode === 'active'">
              <Button label="Delete" severity="danger" text size="small" @click.stop="onDelete(data)" />
            </template>
            <template v-else>
              <Button label="Restore" text size="small" @click.stop="onRestore(data)" />
              <Button label="Delete permanently" severity="danger" text size="small" @click.stop="onPurge(data)" />
            </template>
          </template>
        </Column>
        <template #empty>No records.</template>
      </DataTable>
    </template>
  </section>
</template>
