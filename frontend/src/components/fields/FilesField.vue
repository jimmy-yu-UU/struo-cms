<script setup lang="ts">
import { ref, watch, onMounted, onBeforeUnmount } from 'vue'
import Dialog from 'primevue/dialog'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import OrderList from 'primevue/orderlist'
import MediaGrid from '../media/MediaGrid.vue'
import FileThumbnail, { type FileRow } from '../media/FileThumbnail.vue'
import { itemsApi } from '../../api/itemsApi'
import { useLanguageStore } from '../../stores/languageStore'
import { debounce } from '../../lib/debounce'
import { createLatestWins } from '../../lib/latestWins'
import type { FieldMeta } from '../../types/schema'

defineOptions({ name: 'FilesField' })

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string[]): void }>()

const langStore = useLanguageStore()

type Row = FileRow & { missing?: boolean }

function toIds(v: unknown): string[] {
  return Array.isArray(v) ? v.map(String) : []
}

const rows = ref<Row[]>([])
const dialogOpen = ref(false)
const options = ref<FileRow[]>([])
const search = ref('')
const loadError = ref('')

function currentIds(): string[] {
  return rows.value.map((r) => r.id)
}

async function resolve(ids: string[]): Promise<void> {
  if (ids.length === 0) { rows.value = []; return }
  let found: FileRow[] = []
  try {
    const res = await itemsApi.list('file', {
      page: 0,
      rows: ids.length,
      filter: { id: { op: '_in', value: ids.join(',') } },
      locale: langStore.defaultCode || undefined,
    })
    found = res.data as unknown as FileRow[]
  } catch {
    found = []
  }
  const byId = new Map(found.map((f) => [f.id, f]))
  // Preserve model order; a missing id becomes a raw-id fallback row.
  rows.value = ids.map(
    (id) => byId.get(id) ?? ({ id, fileName: id, contentType: '', size: 0, missing: true } as Row),
  )
}

// Re-resolve only when the incoming model differs from our current order, so our own
// emits (reorder/add/remove) don't clobber the working rows.
watch(
  () => props.modelValue,
  (v) => {
    const incoming = toIds(v)
    if (JSON.stringify(incoming) !== JSON.stringify(currentIds())) resolve(incoming)
  },
)
onMounted(() => resolve(toIds(props.modelValue)))

function commit(next: Row[]): void {
  rows.value = next
  emit('update:modelValue', next.map((r) => r.id))
}
function onReorder(next: Row[]): void {
  commit([...next])
}
function removeAt(i: number): void {
  commit(rows.value.filter((_, idx) => idx !== i))
}

async function openDialog(): Promise<void> {
  dialogOpen.value = true
  await loadOptions()
}
const optionsLoad = createLatestWins()
async function loadOptions(): Promise<void> {
  const token = optionsLoad.next()
  loadError.value = ''
  try {
    const res = await itemsApi.list('file', {
      page: 0,
      rows: 50,
      search: search.value || undefined,
      locale: langStore.defaultCode || undefined,
    })
    if (!optionsLoad.isCurrent(token)) return
    options.value = res.data as unknown as FileRow[]
  } catch (e) {
    if (!optionsLoad.isCurrent(token)) return
    loadError.value = e instanceof Error ? e.message : 'Failed to load files.'
  }
}
function toggle(id: string): void {
  const existing = rows.value.find((r) => r.id === id)
  if (existing) {
    commit(rows.value.filter((r) => r.id !== id))
    return
  }
  const file = options.value.find((f) => f.id === id)
  if (file) commit([...rows.value, file]) // append -> new files go last
}

// Debounce only search-driven reloads; openDialog's direct loadOptions() stays immediate.
const debouncedLoad = debounce(loadOptions, 300)
watch(search, debouncedLoad)
onBeforeUnmount(() => debouncedLoad.cancel())
defineExpose({ openDialog, toggle, removeAt, onReorder, currentIds, resolve, search })
</script>

<template>
  <div class="files-field">
    <OrderList
      v-if="rows.length"
      :model-value="rows"
      data-key="id"
      :disabled="disabled"
      @update:model-value="(v: Row[]) => onReorder(v)"
    >
      <template #item="{ item, index }">
        <div class="files-row">
          <FileThumbnail v-if="!item.missing" :file="item" />
          <span class="files-row__name">{{ item.missing ? item.id : item.fileName }}</span>
          <Button class="files-remove" icon="pi pi-times" text :disabled="disabled" @click="removeAt(index)" />
        </div>
      </template>
    </OrderList>
    <p v-else class="files-field__empty">No files selected</p>

    <Button class="files-add" label="Select files" size="small" :disabled="disabled" @click="openDialog" />

    <Dialog v-model:visible="dialogOpen" modal header="Select files" :style="{ width: '60rem' }">
      <p v-if="loadError" class="error" role="alert">{{ loadError }}</p>
      <InputText v-model="search" placeholder="Search files…" class="files-field__search" />
      <MediaGrid :files="options" multiple :selected-ids="currentIds()" @toggle="toggle" />
      <template #footer>
        <Button label="Done" @click="dialogOpen = false" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.files-field {
  display: flex;
  flex-direction: column;
  gap: 12px;
  align-items: flex-start;
}
.files-row {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
}
.files-row :deep(.file-thumb) {
  width: 48px;
  height: 48px;
  flex: none;
}
.files-row__name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.files-field__empty {
  color: var(--text);
  font-style: italic;
}
.files-field__search {
  display: block;
  margin: 8px 0 12px;
  width: 100%;
}
</style>
