<script setup lang="ts">
import { ref, watch, onMounted } from 'vue'
import Dialog from 'primevue/dialog'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import MediaGrid from '../media/MediaGrid.vue'
import FileThumbnail, { type FileRow } from '../media/FileThumbnail.vue'
import { itemsApi } from '../../api/itemsApi'
import { useLanguageStore } from '../../stores/languageStore'

defineOptions({ name: 'FilePicker' })

const props = defineProps<{ modelValue: string | null; image?: boolean; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string | null): void }>()

const langStore = useLanguageStore()
const current = ref<FileRow | null>(null)
const missingId = ref<string | null>(null)

const dialogOpen = ref(false)
const files = ref<FileRow[]>([])
const search = ref('')
const loadError = ref('')

async function resolveCurrent(): Promise<void> {
  current.value = null
  missingId.value = null
  if (!props.modelValue) return
  try {
    const row = await itemsApi.get('file', props.modelValue, { locale: langStore.defaultCode })
    current.value = row as unknown as FileRow
  } catch {
    missingId.value = props.modelValue // deleted / inaccessible -> show id
  }
}

async function loadOptions(): Promise<void> {
  loadError.value = ''
  try {
    const res = await itemsApi.list('file', {
      page: 0, rows: 50, search: search.value || undefined, locale: langStore.defaultCode || undefined,
    })
    files.value = res.data as unknown as FileRow[]
  } catch (e) {
    loadError.value = e instanceof Error ? e.message : 'Failed to load files.'
  }
}

async function openDialog(): Promise<void> {
  dialogOpen.value = true
  await loadOptions()
}

function onSelect(id: string): void {
  emit('update:modelValue', id)
  dialogOpen.value = false
}

function clear(): void {
  emit('update:modelValue', null)
}

watch(() => props.modelValue, resolveCurrent)
watch(search, loadOptions)
onMounted(resolveCurrent)
defineExpose({ openDialog, onSelect, clear, resolveCurrent, loadOptions })
</script>

<template>
  <div class="file-picker">
    <div v-if="current" class="file-picker__current">
      <FileThumbnail v-if="image" :file="current" />
      <span>{{ current.fileName }}</span>
    </div>
    <span v-else-if="missingId" class="file-picker__missing">{{ missingId }}</span>
    <span v-else class="file-picker__empty">No file selected</span>

    <div class="file-picker__actions">
      <Button label="Select" size="small" :disabled="disabled" @click="openDialog" />
      <Button v-if="modelValue" label="Clear" size="small" text :disabled="disabled" @click="clear" />
    </div>

    <Dialog v-model:visible="dialogOpen" modal header="Select a file" :style="{ width: '60rem' }">
      <p v-if="loadError" class="error" role="alert">{{ loadError }}</p>
      <InputText v-model="search" placeholder="Search files…" class="file-picker__search" />
      <MediaGrid :files="files" selectable :selected-id="modelValue" @select="onSelect" />
    </Dialog>
  </div>
</template>

<style scoped>
.file-picker {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.file-picker__current {
  display: flex;
  align-items: center;
  gap: 8px;
}

.file-picker__current :deep(.file-thumb) {
  width: 48px;
  height: 48px;
  flex: none;
}

.file-picker__missing,
.file-picker__empty {
  color: var(--text);
  font-style: italic;
}

.file-picker__actions {
  display: flex;
  gap: 8px;
}

.file-picker__search {
  display: block;
  margin: 8px 0 12px;
  width: 100%;
}
</style>
