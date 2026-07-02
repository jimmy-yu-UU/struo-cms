<!-- frontend/src/views/CollectionListView.vue -->
<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import InputText from 'primevue/inputtext'
import Button from 'primevue/button'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { itemsApi } from '../api/itemsApi'
import { selectListColumns } from '../lib/selectListColumns'
import { formatCell } from '../lib/formatCell'
import type { FieldMeta } from '../types/schema'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const schema = useSchemaStore()

const name = computed(() => route.params.name as string)
const meta = computed(() => schema.get(name.value))
const canRead = computed(() => auth.canRead(name.value))
const canWrite = computed(() => auth.canWrite(name.value))
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

async function loadItems(): Promise<void> {
  if (!meta.value || !canRead.value) return
  loading.value = true
  error.value = ''
  try {
    const sort = sortField.value
      ? sortOrder.value === -1 ? `-${sortField.value}` : sortField.value
      : undefined
    const res = await itemsApi.list(name.value, {
      page: page.value,
      rows: perPage.value,
      sort,
      search: search.value || undefined,
    })
    rows.value = res.data
    total.value = res.total
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to load items.'
    rows.value = []
    total.value = 0
  } finally {
    loading.value = false
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
  const rid = e.data.id
  if (rid != null) router.push({ name: 'collection-item', params: { name: name.value, id: String(rid) } })
}

function onNew(): void {
  router.push({ name: 'collection-create', params: { name: name.value } })
}

watch(name, () => {
  page.value = 0
  sortField.value = undefined
  sortOrder.value = undefined
  search.value = ''
  loadItems()
})

onMounted(loadItems)

defineExpose({ loadItems, onPage, onSort, onSearchInput, onRowClick, onNew, canWrite, rows, total, loading, error })
</script>

<template>
  <section class="collection-list">
    <template v-if="!meta">
      <p class="notice">Collection not found.</p>
    </template>
    <template v-else-if="!canRead">
      <p class="notice">You don't have access to this collection.</p>
    </template>
    <template v-else>
      <header class="list-header">
        <h2>{{ meta.label }}</h2>
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
            {{ formatCell(data[col.field], fieldOf(col.field)!) }}
          </template>
        </Column>
        <template #empty>No records.</template>
      </DataTable>
    </template>
  </section>
</template>
