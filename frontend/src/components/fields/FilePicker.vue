<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import { useI18n } from 'vue-i18n'
import { Dialog, DialogScrollContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'
import TreeSelect from '@/components/form/TreeSelect.vue'
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
function onFolderChange(key: string | null): void {
  folderSel.value = key ?? ALL
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
      <FileThumbnail v-if="image" :file="current" class="file-picker__thumb" />
      <span>{{ current.fileName }}</span>
    </div>
    <span v-else-if="missingId" class="file-picker__missing italic text-muted-foreground">{{ missingId }}</span>
    <span v-else class="file-picker__empty italic text-muted-foreground">{{ t('fields.noFileSelected') }}</span>

    <div class="file-picker__actions">
      <Button type="button" variant="outline" size="sm" :disabled="disabled" @click="openDialog">{{ t('fields.selectFile') }}</Button>
      <Button v-if="modelValue" type="button" variant="ghost" size="sm" :disabled="disabled" @click="clear">{{ t('fields.clear') }}</Button>
    </div>

    <Dialog v-model:open="dialogOpen">
      <!--
        DialogScrollContent, not DialogContent: the file grid can run to several rows (openDialog
        requests up to 50 files), and reka's DialogRoot locks body scroll while open, so a
        fixed-position, viewport-centered box (plain DialogContent) leaves no scroll container for
        overflow at all — rows above and below the viewport become permanently unreachable.
        DialogScrollContent's overlay carries its own overflow-y-auto and holds the content box in
        normal flow (relative + vertical margin) instead of fixed-centered, so the overlay itself
        scrolls once the box is taller than the viewport.

        Its own width class is an unprefixed max-w-lg (no sm: modifier, unlike plain DialogContent),
        so the override below re-supplies no modifier either — tailwind-merge keys on (modifier
        set, class group), and a bare max-w-lg only loses to another bare max-w-* class.
        max-[960px]:max-w-[95vw] restores the old PrimeVue Dialog's `:breakpoints="{ '960px':
        '95vw' }"`, which widened the dialog on medium viewports rather than keeping the 78vw cap.
      -->
      <DialogScrollContent class="max-w-[min(78vw,1300px)] max-[960px]:max-w-[95vw]">
        <DialogHeader>
          <DialogTitle>{{ t('fields.selectAFile') }}</DialogTitle>
        </DialogHeader>
        <p v-if="loadError" class="error" role="alert">{{ loadError }}</p>
        <div v-if="folders.length" class="file-picker__folder">
          <TreeSelect
            :model-value="folderSel"
            :nodes="folderNodes"
            :label="t('media.folderField')"
            :placeholder="t('fields.selectAFolder')"
            @update:model-value="onFolderChange"
          />
        </div>
        <Input v-model="search" :placeholder="t('fields.searchFiles')" :aria-label="t('fields.searchFiles')" class="file-picker__search" />
        <MediaGrid :files="files" selectable :selected-id="modelValue" @select="onSelect" />
      </DialogScrollContent>
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

/*
 * FileThumbnail's own scoped .file-thumb rule and this one carry equal specificity (one class +
 * one scoped-id attribute each — the div is this component's own scope AND FileThumbnail's,
 * because a child's root node picks up both when the parent passes it a class), so which wins
 * would otherwise depend on which style block Vite happens to emit later in the bundle. Matching
 * both classes in one compound selector adds a second class to the specificity count, which wins
 * regardless of source order.
 */
.file-thumb.file-picker__thumb {
  width: 48px;
  height: 48px;
  flex: none;
}

.file-picker__actions {
  display: flex;
  gap: 8px;
}

/* No margin here: DialogScrollContent's content box is itself `grid gap-4`, which already spaces
   every direct child (DialogHeader, the error text, this folder filter, the search input,
   MediaGrid) — an added margin would stack on top of that gap instead of replacing it. */
</style>
