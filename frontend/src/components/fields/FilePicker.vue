<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import { useI18n } from 'vue-i18n'
import Dialog from 'primevue/dialog'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import TreeSelect from 'primevue/treeselect'
import MediaGrid from '../media/MediaGrid.vue'
import FileThumbnail, { type FileRow } from '../media/FileThumbnail.vue'
import { itemsApi } from '../../api/itemsApi'
import { useLanguageStore } from '../../stores/languageStore'
import { debounce } from '../../lib/debounce'
import { createLatestWins } from '../../lib/latestWins'
import { toFileRows } from '../../lib/toFileRow'
import { buildRelationTree, type TreeNode } from '../../lib/buildRelationTree'
import { mediaFolderFilter } from '../../lib/mediaQuery'
import { toFolderRows, type FolderRow } from '../../lib/folderTree'

defineOptions({ name: 'FilePicker' })

const props = defineProps<{ modelValue: string | null; image?: boolean; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string | null): void }>()

const { t } = useI18n()
const langStore = useLanguageStore()
const current = ref<FileRow | null>(null)
const missingId = ref<string | null>(null)

const dialogOpen = ref(false)
const files = ref<FileRow[]>([])
const search = ref('')
const loadError = ref('')

const ALL = '__all'
const UNFILED = '__unfiled'
const folders = ref<FolderRow[]>([])
const folderSel = ref<string>(ALL)

const folderNodes = computed<TreeNode[]>(() => [
  { key: ALL, label: t('media.folderAll'), data: ALL, children: [] },
  { key: UNFILED, label: t('media.folderUncategorized'), data: UNFILED, children: [] },
  ...buildRelationTree(folders.value.map((f) => ({ id: f.id, label: f.name, parentId: f.parentId })), 'parentId'),
])
// TreeSelect single-selection binds { [key]: true } (same mapping as RelationPicker/MediaDetailDialog).
const folderValue = computed(() => ({ [folderSel.value]: true }))
function onFolderChange(selection: Record<string, boolean>): void {
  folderSel.value = Object.keys(selection)[0] ?? ALL
  void loadOptions()
}
function pickerFolderFilter(): ReturnType<typeof mediaFolderFilter> | undefined {
  if (folderSel.value === ALL) return undefined
  return folderSel.value === UNFILED ? mediaFolderFilter(null) : mediaFolderFilter(folderSel.value)
}

async function loadFolders(): Promise<void> {
  try {
    folders.value = toFolderRows((await itemsApi.list('mediafolder', { page: 0, rows: 500, sort: 'name', deep: ['parent'] })).data)
  } catch {
    // Folder loading is a progressive enhancement: degrade to no folder filter (TreeSelect
    // hidden via v-if) rather than blocking file browsing.
    folders.value = []
  }
}

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

const optionsLoad = createLatestWins()
async function loadOptions(): Promise<void> {
  const token = optionsLoad.next()
  loadError.value = ''
  try {
    const res = await itemsApi.list('file', {
      page: 0, rows: 50, search: search.value || undefined, locale: langStore.defaultCode || undefined,
      filter: pickerFolderFilter(),
    })
    if (!optionsLoad.isCurrent(token)) return
    files.value = toFileRows(res.data)
  } catch (e) {
    if (!optionsLoad.isCurrent(token)) return
    loadError.value = e instanceof Error ? e.message : t('fields.loadFilesFailed')
  }
}

async function openDialog(): Promise<void> {
  dialogOpen.value = true
  await Promise.all([loadFolders(), loadOptions()])
}

function onSelect(id: string): void {
  emit('update:modelValue', id)
  dialogOpen.value = false
}

function clear(): void {
  emit('update:modelValue', null)
}

watch(() => props.modelValue, resolveCurrent)
// Debounce only search-driven reloads; openDialog's direct loadOptions() stays immediate.
const debouncedLoad = debounce(loadOptions, 300)
watch(search, debouncedLoad)
onMounted(resolveCurrent)
onBeforeUnmount(() => debouncedLoad.cancel())
defineExpose({ openDialog, onSelect, clear, resolveCurrent, loadOptions, files, search, folderSel, onFolderChange, folders })
</script>

<template>
  <div class="file-picker">
    <div v-if="current" class="file-picker__current">
      <FileThumbnail v-if="image" :file="current" />
      <span>{{ current.fileName }}</span>
    </div>
    <span v-else-if="missingId" class="file-picker__missing">{{ missingId }}</span>
    <span v-else class="file-picker__empty">{{ t('fields.noFileSelected') }}</span>

    <div class="file-picker__actions">
      <Button :label="t('fields.selectFile')" severity="secondary" outlined size="small" :disabled="disabled" @click="openDialog" />
      <Button v-if="modelValue" :label="t('fields.clear')" size="small" text :disabled="disabled" @click="clear" />
    </div>

    <Dialog v-model:visible="dialogOpen" modal :header="t('fields.selectAFile')" :style="{ width: 'min(78vw, 1300px)' }" :breakpoints="{ '960px': '95vw' }">
      <p v-if="loadError" class="error" role="alert">{{ loadError }}</p>
      <TreeSelect
        v-if="folders.length"
        class="file-picker__folder"
        :model-value="folderValue"
        :options="folderNodes"
        selection-mode="single"
        @update:model-value="onFolderChange"
      />
      <InputText v-model="search" :placeholder="t('fields.searchFiles')" class="file-picker__search" />
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
  color: var(--muted);
  font-style: italic;
}

.file-picker__actions {
  display: flex;
  gap: 8px;
}

.file-picker__folder {
  display: block;
  margin: 8px 0 0;
  width: 100%;
}

.file-picker__search {
  display: block;
  margin: 8px 0 12px;
  width: 100%;
}
</style>
