<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import { itemsApi } from '../../api/itemsApi'
import { useSchemaStore } from '../../stores/schemaStore'
import { useLanguageStore } from '../../stores/languageStore'
import { resolveDisplayLabel } from '../../lib/resolveDisplayLabel'
import type { RelationMeta } from '../../types/schema'

const props = defineProps<{ relation: RelationMeta; parentId?: string }>()
const router = useRouter()
const schema = useSchemaStore()
const langStore = useLanguageStore()

type Row = { id: string; label: string }
const rows = ref<Row[]>([])
const total = ref(0)
const page = ref(0)
const perPage = ref(10)
const loading = ref(false)
const error = ref('')

const targetMeta = computed(() => schema.get(props.relation.targetCollection))

function camel(s: string): string {
  return s.length ? s[0].toLowerCase() + s.slice(1) : s
}

async function load(): Promise<void> {
  if (!props.parentId || !props.relation.foreignKey) return
  loading.value = true
  error.value = ''
  try {
    const res = await itemsApi.list(props.relation.targetCollection, {
      page: page.value,
      rows: perPage.value,
      locale: langStore.defaultCode || undefined,
      filter: { [camel(props.relation.foreignKey)]: { op: '_eq', value: props.parentId } },
    })
    const tm = targetMeta.value
    rows.value = res.data.map((r) => ({
      id: String(r.id),
      label: tm ? resolveDisplayLabel(r, props.relation, tm, langStore.defaultCode) : String(r.id),
    }))
    total.value = res.total
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to load related items.'
  } finally {
    loading.value = false
  }
}

function onPage(e: { page: number; rows: number }): void {
  page.value = e.page
  perPage.value = e.rows
  load()
}

function openItem(id: string): void {
  router.push({ name: 'collection-item', params: { name: props.relation.targetCollection, id } })
}

onMounted(load)
defineExpose({ load, onPage, rows, total, loading, error })
</script>

<template>
  <div class="related-list">
    <p v-if="!parentId" class="hint">Visible after saving.</p>
    <template v-else>
      <p v-if="error" class="error" role="alert">{{ error }}</p>
      <DataTable
        :value="rows"
        lazy
        paginator
        :rows="perPage"
        :total-records="total"
        :loading="loading"
        @page="onPage"
        @row-click="(e: { data: Row }) => openItem(e.data.id)"
      >
        <Column field="label" :header="relation.label" />
        <template #empty>No related items.</template>
      </DataTable>
    </template>
  </div>
</template>
