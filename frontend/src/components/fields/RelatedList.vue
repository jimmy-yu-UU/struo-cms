<script setup lang="ts">
import { ref, computed, onMounted, h } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { itemsApi } from '../../api/itemsApi'
import { useSchemaStore } from '../../stores/schemaStore'
import { useLanguageStore } from '../../stores/languageStore'
import { resolveDisplayLabel } from '../../lib/resolveDisplayLabel'
import DataTable, { type DataTableColumn, type DataTableState } from '@/components/data/DataTable.vue'
import DataTablePagination from '@/components/data/DataTablePagination.vue'
import { Button } from '@/components/ui/button'
import type { RelationMeta } from '../../types/schema'

const props = defineProps<{ relation: RelationMeta; parentId?: string }>()
const router = useRouter()
const schema = useSchemaStore()
const langStore = useLanguageStore()
const { t } = useI18n()

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

function onPage(p: number): void {
  page.value = p
  load()
}

// DataTablePagination only fires update:page-size from its rows-per-page <select>, which
// :show-page-size-selector="false" below keeps unrendered — 10 is a deliberate fixed size for this
// secondary in-form list. This handler exists to satisfy DataTablePagination's event contract so
// re-enabling the selector later needs no wiring change here.
function onPageSize(size: number): void {
  page.value = 0
  perPage.value = size
  load()
}

function openItem(id: string): void {
  router.push({ name: 'collection-item', params: { name: props.relation.targetCollection, id } })
}

// Mirrors CollectionListView.vue's update:state handling, so a future sortable column stays a
// change confined to the columns computed below. DataTable only emits update:state when a header's
// sort cycles; this table's single column carries no meta.sortable, so SortableHeader renders a
// plain label and update:state never actually fires — the query load() builds has no sort param to
// receive it anyway.
const tableState = computed<DataTableState>(() => ({ sort: [], page: page.value, pageSize: perPage.value }))

function onTableState(next: DataTableState): void {
  onPage(next.page)
}

const columns = computed<DataTableColumn<Row>[]>(() => [{
  id: 'label',
  accessorKey: 'label',
  header: props.relation.label,
  // DataTable has no row-click event and no slots, so the cell itself carries navigation: a link
  // button gives the row a role and an accessible name that a plain text cell would not.
  cell: ({ row }) => h(Button, {
    type: 'button',
    variant: 'link',
    size: 'sm',
    'data-testid': 'related-row',
    onClick: () => openItem(row.original.id),
  }, () => row.original.label),
}])

onMounted(load)
defineExpose({ load, onPage, rows, total, loading, error })
</script>

<template>
  <div class="related-list">
    <p v-if="!parentId" class="hint">Visible after saving.</p>
    <template v-else>
      <p v-if="error" class="error" role="alert">{{ error }}</p>
      <DataTable
        :columns="columns"
        :rows="rows"
        :total="total"
        :state="tableState"
        :loading="loading"
        :empty-message="t('fields.noRelatedItems')"
        :show-column-toggle="false"
        @update:state="onTableState"
      />
      <DataTablePagination
        :page="page"
        :page-size="perPage"
        :total="total"
        :show-page-size-selector="false"
        @update:page="onPage"
        @update:page-size="onPageSize"
      />
    </template>
  </div>
</template>
