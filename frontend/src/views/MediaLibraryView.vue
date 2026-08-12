<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { FolderPlus, Upload, Trash2, Undo2, ChevronRight, LayoutGrid, List } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import DataTablePagination from '@/components/data/DataTablePagination.vue'
import PageHeader from '../components/common/PageHeader.vue'
import ListToolbar from '../components/common/ListToolbar.vue'
import MediaGrid from '../components/media/MediaGrid.vue'
import MediaFileList from '../components/media/MediaFileList.vue'
import MediaUploadDialog from '../components/media/MediaUploadDialog.vue'
import MediaDetailDialog from '../components/media/MediaDetailDialog.vue'
import MediaFolderCards from '../components/media/MediaFolderCards.vue'
import MediaFolderNameDialog from '../components/media/MediaFolderNameDialog.vue'
import FileThumbnail, { type FileRow } from '../components/media/FileThumbnail.vue'
import { itemsApi } from '../api/itemsApi'
import { filesApi } from '../api/filesApi'
import { useAuthStore } from '../stores/authStore'
import { useLanguageStore } from '../stores/languageStore'
import { debounce } from '../lib/debounce'
import { createLatestWins } from '../lib/latestWins'
import { mediaTypeFilter, mediaFolderFilter, mediaSort, type MediaType, type MediaSort } from '../lib/mediaQuery'
import { toFileRows } from '../lib/toFileRow'
import { toFolderRows, childFolders, folderPath, type FolderRow } from '../lib/folderTree'
import { purgeConfirm } from '../lib/deleteAction'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import ConfirmDialog from 'primevue/confirmdialog'
import { ApiError } from '../api/apiClient'

const { t } = useI18n()
const auth = useAuthStore()
const langStore = useLanguageStore()
const toast = useToast()
const confirm = useConfirm()

const files = ref<FileRow[]>([])
const total = ref(0)
const loading = ref(false)
const error = ref('')
const page = ref(0)
const perPage = ref(24)
const search = ref('')
const type = ref<MediaType>('all')
const sort = ref<MediaSort>('newest')
const view = ref<'grid' | 'list'>('grid')
const selected = ref<FileRow | null>(null)
const uploadOpen = ref(false)
const mode = ref<'active' | 'trash'>('active')

const canWrite = computed(() => auth.canWrite('file'))
const canDelete = computed(() => auth.canDelete('file'))
// Trash toggle mirrors CollectionListView's Active/Trash ToggleGroup: only meaningful to
// a user who can actually restore/purge, so gate it on canDelete rather than always showing it.
const showTrashSwitch = computed(() => canDelete.value)

// Folder navigation state (Drive-style).
const folders = ref<FolderRow[]>([])
const currentFolderId = ref<string | null>(null)
const createOpen = ref(false)
const renameTarget = ref<FolderRow | null>(null)

const canManageFolders = computed(() => auth.canWrite('mediafolder'))
const canDeleteFolders = computed(() => auth.canDelete('mediafolder'))
const searchActive = computed(() => search.value.trim().length > 0)
// The trash is a flat recycle bin across all folders (mirrors CollectionListView's trash mode
// which also drops per-collection scoping) -- folder cards/breadcrumbs don't apply there.
const visibleFolders = computed(() =>
  searchActive.value || mode.value === 'trash' ? [] : childFolders(folders.value, currentFolderId.value))
const breadcrumb = computed(() => folderPath(folders.value, currentFolderId.value))

const typeOptions = computed(() => [
  { label: t('media.typeAll'), value: 'all' as const },
  { label: t('media.typeImage'), value: 'image' as const },
  { label: t('media.typeVideo'), value: 'video' as const },
])
const sortOptions = computed(() => [
  { label: t('media.sortNewest'), value: 'newest' as const },
  { label: t('media.sortName'), value: 'name' as const },
])
const viewOptions = computed(() => [
  { label: t('media.viewGrid'), value: 'grid' as const, icon: LayoutGrid },
  { label: t('media.viewList'), value: 'list' as const, icon: List },
])

const mediaLoad = createLatestWins()

async function load(): Promise<void> {
  const token = mediaLoad.next()
  loading.value = true
  error.value = ''
  try {
    await langStore.load()
    const res = await itemsApi.list('file', {
      page: page.value,
      rows: perPage.value,
      sort: mediaSort(sort.value),
      search: search.value || undefined,
      filter: { ...(mediaTypeFilter(type.value) ?? {}),
                ...(searchActive.value || mode.value === 'trash' ? {} : mediaFolderFilter(currentFolderId.value)) },
      locale: langStore.defaultCode || undefined,
      ...(mode.value === 'trash' ? { deleted: 'only' } : {}),
    })
    if (!mediaLoad.isCurrent(token)) return
    files.value = toFileRows(res.data)
    total.value = res.total
  } catch (e) {
    if (!mediaLoad.isCurrent(token)) return
    error.value = e instanceof Error ? e.message : t('media.loadFailed')
    files.value = []
    total.value = 0
  } finally {
    if (mediaLoad.isCurrent(token)) loading.value = false
  }
}

function reload(): void {
  page.value = 0
  load()
}

const debouncedSearch = debounce(() => { page.value = 0; load() }, 300)
function onSearchInput(value: string): void {
  search.value = value
  debouncedSearch()
}
function setMode(m: 'active' | 'trash'): void { mode.value = m; reload() }
function onModeToggle(value: unknown): void {
  // reka's single-type ToggleGroup emits undefined when the pressed item is clicked again
  // (deselect) -- the trash switch has no "neither" state, so ignore that.
  if (value === 'active' || value === 'trash') setMode(value)
}
function onViewToggle(value: unknown): void {
  if (value === 'grid' || value === 'list') view.value = value
}
function onType(value: MediaType): void { type.value = value; reload() }
function onSort(value: MediaSort): void { sort.value = value; reload() }
function onPageChange(nextPage: number): void {
  page.value = nextPage
  load()
}
function onPageSizeChange(nextSize: number): void {
  // An offset computed against the OLD page size is meaningless against the new one (page 2 at
  // 24/page starts at row 48; at 96/page that is off the end of the result set) -- always return
  // to the first page when the size changes.
  page.value = 0
  perPage.value = nextSize
  load()
}
function openDetail(id: string): void {
  selected.value = files.value.find((f) => f.id === id) ?? null
}
// Preserve the user's page position on delete instead of always resetting to page 0.
// Refresh at the current page first; only if the new total no longer covers that page (e.g.
// the deleted item was the last one on the last page) do we clamp down to the new last valid
// page and reload -- never below page 0.
async function loadClampingToLastValidPage(): Promise<void> {
  await load()
  const lastValidPage = Math.max(0, Math.ceil(total.value / perPage.value) - 1)
  if (page.value > lastValidPage) {
    page.value = lastValidPage
    await load()
  }
}
function onDeleted(): void { selected.value = null; loadClampingToLastValidPage() }

async function onRestore(id: string): Promise<void> {
  try {
    await filesApi.restore(id)
    await loadClampingToLastValidPage()
  } catch (e) {
    toast.add({ severity: 'error', summary: e instanceof Error ? e.message : t('media.deleteFailed'), life: 3500 })
  }
}

function onPurge(id: string): void {
  confirm.require({
    ...purgeConfirm(t),
    group: 'media-file',
    accept: async () => {
      try {
        await filesApi.remove(id, { purge: true })
        await loadClampingToLastValidPage()
      } catch (e) {
        toast.add({ severity: 'error', summary: e instanceof Error ? e.message : t('media.deleteFailed'), life: 3500 })
      }
    },
  })
}

async function loadFolders(): Promise<void> {
  try {
    const res = await itemsApi.list('mediafolder', { page: 0, rows: 500, sort: 'name', deep: ['parent'] })
    folders.value = toFolderRows(res.data)
  } catch {
    // Folder loading is a progressive enhancement on top of the flat file list: if it fails,
    // degrade to a flat list rather than blocking file browsing entirely.
    folders.value = []
    toast.add({ severity: 'warn', summary: t('media.folderLoadFailed'), life: 3500 })
  }
}

function enterFolder(id: string): void { currentFolderId.value = id; page.value = 0; load() }
function goToBreadcrumb(id: string | null): void { currentFolderId.value = id; page.value = 0; load() }

async function onCreateFolder(name: string): Promise<void> {
  try {
    await itemsApi.create('mediafolder', { name, parentId: currentFolderId.value })
    await loadFolders()
  } catch (e) {
    toast.add({ severity: 'error', summary: e instanceof Error ? e.message : t('media.folderSaveFailed'), life: 3500 })
  }
}

async function onRenameFolder(name: string): Promise<void> {
  const target = renameTarget.value
  renameTarget.value = null
  if (!target) return
  try {
    await itemsApi.update('mediafolder', target.id,
      target.version !== undefined ? { name, version: target.version } : { name })
    await loadFolders()
  } catch (e) {
    toast.add({ severity: 'error', summary: e instanceof Error ? e.message : t('media.folderSaveFailed'), life: 3500 })
  }
}

function onRemoveFolder(folder: FolderRow): void {
  confirm.require({
    group: 'media-folder',
    header: t('media.folderDelete'),
    message: t('media.folderDeleteConfirm', { name: folder.name }),
    acceptProps: { severity: 'danger' },
    accept: async () => {
      try {
        await itemsApi.remove('mediafolder', folder.id)
        if (currentFolderId.value === folder.id) currentFolderId.value = folder.parentId
        await loadFolders()
        await load()
      } catch (e) {
        if (e instanceof ApiError && e.status === 409)
          toast.add({ severity: 'warn', summary: t('media.folderNotEmpty'), life: 4000 })
        else
          toast.add({ severity: 'error', summary: e instanceof Error ? e.message : t('media.folderSaveFailed'), life: 3500 })
      }
    },
  })
}

onMounted(() => { loadFolders(); load() })
onUnmounted(() => debouncedSearch.cancel())

defineExpose({ load, reload, onType, onSort, onPageChange, onPageSizeChange, onSearchInput, openDetail, onDeleted,
  files, total, loading, error, canWrite, canDelete, selected,
  folders, currentFolderId, visibleFolders, breadcrumb, enterFolder,
  goToBreadcrumb, onCreateFolder, onRenameFolder, onRemoveFolder, createOpen, renameTarget,
  mode, setMode, onModeToggle, onViewToggle, view, showTrashSwitch, onRestore, onPurge })
</script>

<template>
  <section class="media-library">
    <PageHeader :title="t('media.title')" :caption="t('media.count', { n: total })">
      <template #actions>
        <Button v-if="canManageFolders" type="button" variant="outline" @click="createOpen = true">
          <FolderPlus aria-hidden="true" />
          {{ t('media.folderNew') }}
        </Button>
        <Button v-if="canWrite" type="button" @click="uploadOpen = true">
          <Upload aria-hidden="true" />
          {{ t('media.upload') }}
        </Button>
      </template>
    </PageHeader>

    <ListToolbar :search-value="search" :search-placeholder="t('media.searchPlaceholder')" @search="onSearchInput">
      <template #filters>
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

        <Select :model-value="type" @update:model-value="(v) => onType(v as MediaType)">
          <SelectTrigger class="w-40" :aria-label="t('media.typeFilter')">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="o in typeOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
          </SelectContent>
        </Select>

        <Select :model-value="sort" @update:model-value="(v) => onSort(v as MediaSort)">
          <SelectTrigger class="w-40" :aria-label="t('media.sortFilter')">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="o in sortOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
          </SelectContent>
        </Select>

        <ToggleGroup
          type="single"
          :model-value="view"
          variant="outline"
          @update:model-value="onViewToggle"
        >
          <ToggleGroupItem v-for="o in viewOptions" :key="o.value" :value="o.value" :aria-label="o.label">
            <component :is="o.icon" class="size-4" aria-hidden="true" />
          </ToggleGroupItem>
        </ToggleGroup>
      </template>
    </ListToolbar>

    <p
      v-if="mode === 'trash'"
      role="status"
      class="trash-banner mb-3 flex items-center gap-2 rounded-md border border-warning bg-warning/10 px-3.5 py-2.5 text-sm"
    >
      <Trash2 class="size-4" aria-hidden="true" />
      {{ t('collectionList.trashNotice') }}
    </p>

    <nav v-if="mode === 'active' && !searchActive && (breadcrumb.length || folders.length)" class="media-crumb" :aria-label="t('media.title')">
      <button type="button" class="media-crumb__link" @click="goToBreadcrumb(null)">{{ t('media.breadcrumbRoot') }}</button>
      <template v-for="c in breadcrumb" :key="c.id">
        <ChevronRight class="media-crumb__sep size-3 text-muted-foreground" aria-hidden="true" />
        <button v-if="c.id !== currentFolderId" type="button" class="media-crumb__link" @click="goToBreadcrumb(c.id)">{{ c.name }}</button>
        <span v-else class="media-crumb__current">{{ c.name }}</span>
      </template>
    </nav>

    <p v-if="error" class="error" role="alert">{{ error }}</p>

    <MediaFolderCards v-if="mode === 'active'" :folders="visibleFolders" :can-manage="canManageFolders || canDeleteFolders"
                      @open="enterFolder" @rename="renameTarget = $event" @remove="onRemoveFolder" />

    <template v-if="mode === 'active'">
      <MediaGrid v-if="view === 'grid'" :files="files" @open="openDetail" />
      <MediaFileList v-else :files="files" @open="openDetail" />
    </template>
    <table v-else class="media-trash-list">
      <thead>
        <tr>
          <th class="media-trash-list__thumb-col" aria-hidden="true"></th>
          <th>{{ t('media.colName') }}</th>
          <th class="media-trash-list__actions-col"></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="f in files" :key="f.id">
          <td class="media-trash-list__thumb"><FileThumbnail :file="f" /></td>
          <td>{{ f.fileName }}</td>
          <td class="media-trash-list__actions">
            <Button
              type="button" variant="ghost" size="icon-sm"
              :title="t('collectionList.restore')" :aria-label="t('collectionList.restore')"
              @click="onRestore(f.id)"
            >
              <Undo2 aria-hidden="true" />
            </Button>
            <Button
              type="button" variant="ghost" size="icon-sm"
              class="text-destructive hover:text-destructive"
              :title="t('collectionList.purge')" :aria-label="t('collectionList.purge')"
              @click="onPurge(f.id)"
            >
              <Trash2 aria-hidden="true" />
            </Button>
          </td>
        </tr>
      </tbody>
    </table>
    <p v-if="!loading && !files.length && !visibleFolders.length" class="empty">{{ t(mode === 'trash' ? 'collectionList.emptyTrash' : 'media.empty') }}</p>

    <DataTablePagination
      v-if="total > 0"
      :page="page"
      :page-size="perPage"
      :total="total"
      :page-size-options="[24, 48, 96]"
      @update:page="onPageChange"
      @update:page-size="onPageSizeChange"
    />

    <MediaUploadDialog v-model:visible="uploadOpen" :folder-id="searchActive ? null : currentFolderId" @done="reload" />
    <MediaDetailDialog :file="selected" :can-write="canWrite" :can-delete="canDelete"
                       @close="selected = null" @saved="load" @deleted="onDeleted" />

    <MediaFolderNameDialog v-model:visible="createOpen" :header="t('media.folderNew')" @submit="onCreateFolder" />
    <MediaFolderNameDialog :visible="renameTarget !== null" :header="t('media.folderRename')"
                           :initial-name="renameTarget?.name" @update:visible="(v: boolean) => { if (!v) renameTarget = null }"
                           @submit="onRenameFolder" />
    <ConfirmDialog group="media-folder" />
    <ConfirmDialog group="media-file" />
  </section>
</template>

<style scoped>
.media-library { display: block; }
.error { color: var(--danger); font-size: 0.9rem; margin: 0 0 12px; }
.empty { color: var(--legacy-muted); text-align: center; padding: 40px 0; }
.media-crumb { display: flex; align-items: center; gap: 4px; margin: 0 0 12px; flex-wrap: wrap; }
.media-crumb__link { border: 0; background: none; padding: 2px 4px; cursor: pointer; color: var(--legacy-accent); font: inherit; border-radius: var(--legacy-radius, 8px); }
.media-crumb__link:hover { text-decoration: underline; }
.media-crumb__current { color: var(--fg); font-weight: 600; padding: 2px 4px; }
.media-trash-list { width: 100%; border-collapse: collapse; font-size: 0.9rem; }
.media-trash-list th {
  text-align: left; padding: 8px 12px; color: var(--legacy-muted); font-weight: 600;
  border-bottom: 1px solid var(--border);
}
.media-trash-list td { padding: 8px 12px; border-bottom: 1px solid var(--border); color: var(--fg); vertical-align: middle; }
.media-trash-list__thumb-col { width: 64px; }
.media-trash-list__thumb { width: 56px; }
.media-trash-list__thumb :deep(.file-thumb) { height: 44px; width: 56px; }
.media-trash-list__actions-col { width: 6rem; }
.media-trash-list__actions { display: flex; gap: 4px; justify-content: flex-end; }
</style>
