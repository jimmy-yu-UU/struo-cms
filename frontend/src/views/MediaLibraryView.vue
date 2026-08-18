<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { FolderPlus, Upload, Trash2, Undo2, ChevronRight, LayoutGrid, List } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import { ContextMenu, ContextMenuContent, ContextMenuItem, ContextMenuTrigger } from '@/components/ui/context-menu'
import DataTablePagination from '@/components/data/DataTablePagination.vue'
import PageHeader from '../components/common/PageHeader.vue'
import ListToolbar from '../components/common/ListToolbar.vue'
import MediaGrid from '../components/media/MediaGrid.vue'
import MediaFileList from '../components/media/MediaFileList.vue'
import MediaUploadDialog from '../components/media/MediaUploadDialog.vue'
import MediaDetailDialog from '../components/media/MediaDetailDialog.vue'
import MediaFolderCards from '../components/media/MediaFolderCards.vue'
import MediaFolderNameDialog from '../components/media/MediaFolderNameDialog.vue'
import MediaMoveDialog from '../components/media/MediaMoveDialog.vue'
import MediaSelectionToolbar from '../components/media/MediaSelectionToolbar.vue'
import type { FileRow } from '../components/media/FileThumbnail.vue'
import { itemsApi } from '../api/itemsApi'
import { filesApi } from '../api/filesApi'
import { useAuthStore } from '../stores/authStore'
import { useLanguageStore } from '../stores/languageStore'
import { debounce } from '../lib/debounce'
import { createLatestWins } from '../lib/latestWins'
import { mediaTypeFilter, mediaFolderFilter, mediaSort, type MediaType, type MediaSort } from '../lib/mediaQuery'
import { toFileRows } from '../lib/toFileRow'
import { toFolderRows, childFolders, folderPath, type FolderRow } from '../lib/folderTree'
import { purgeConfirm, deleteConfirm } from '../lib/deleteAction'
import { useToast } from '@/composables/useToast'
import { useConfirm } from '@/composables/useConfirm'
import { ApiError } from '../api/apiClient'
import { performMove } from '../lib/mediaMoveActions'
import { isNoOpMove, type MovePayload } from '../lib/mediaMove'
import { readDragPayload } from '../lib/mediaDnd'
import { toggleSelection, removeFromSelection } from '../lib/mediaSelection'

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
// Move-to dialog: opened via onRequestMove (the context menu's "Move to…" entry) and, later, the
// Task 9 selection toolbar -- both just set movePayload and flip moveDialogOpen.
const moveDialogOpen = ref(false)
const movePayload = ref<MovePayload>({ files: [], folders: [] })

// Task 9 batch selection: one MovePayload shared (as a prop) across all four media surfaces
// (MediaGrid, MediaFolderCards, MediaFileList's two row kinds). Invariant: a selection must not
// outlive the listing it was built against, so every handler below that reloads `files.value`
// also calls clearSelection() on its way in (see :165-168 for the reasoning at one such call
// site). Drop that invariant and the failure mode is a SILENT SUCCESS on files, not a
// canMoveFolder guard bypass: the toolbar keeps reporting "N selected" for ids the user can no
// longer see, and a later batch move would act on them with no error at all. (loadFolders() only
// runs on mount and after create/rename/delete/move -- never from a navigation trigger -- so the
// analogous folder-side risk through canMoveFolder's own "source absent from folders" refusal is
// not reachable from these call sites.)
const selection = ref<MovePayload>({ files: [], folders: [] })
function clearSelection(): void { selection.value = { files: [], folders: [] } }
// The real guard against building a selection while browsing the trash (a batch move on
// soft-deleted items is nonsensical, and MediaMoveDialog is mounted unconditionally regardless of
// mode) -- not merely relying on `setMode` already clearing the selection on every switch.
function onToggleSelect(kind: 'file' | 'folder', id: string): void {
  if (mode.value !== 'active') return
  selection.value = toggleSelection(selection.value, kind, id)
}

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
  clearSelection()
  search.value = value
  debouncedSearch()
}
function setMode(m: 'active' | 'trash'): void { clearSelection(); mode.value = m; reload() }
function onModeToggle(value: unknown): void {
  // reka's single-type ToggleGroup emits undefined when the pressed item is clicked again
  // (deselect) -- the trash switch has no "neither" state, so ignore that.
  if (value === 'active' || value === 'trash') setMode(value)
}
function onViewToggle(value: unknown): void {
  if (value === 'grid' || value === 'list') view.value = value
}
// Review fix (Finding 1): these two reload `files.value` exactly like search/page/mode already
// do, so a stale selection surviving one of them is not just staleness -- it is a SILENT SUCCESS
// hazard: the toolbar would keep reading "N selected" after a filter change hides all of them,
// and Move to… would then move items the user can no longer see with no error at all.
function onType(value: MediaType): void { clearSelection(); type.value = value; reload() }
function onSort(value: MediaSort): void { clearSelection(); sort.value = value; reload() }
function onPageChange(nextPage: number): void {
  clearSelection()
  page.value = nextPage
  load()
}
function onPageSizeChange(nextSize: number): void {
  // An offset computed against the OLD page size is meaningless against the new one (page 2 at
  // 24/page starts at row 48; at 96/page that is off the end of the result set) -- always return
  // to the first page when the size changes.
  clearSelection()
  page.value = 0
  perPage.value = nextSize
  load()
}
// Trash rows are soft-deleted files: the old bespoke trash table had no click-to-open path at
// all, and opening MediaDetailDialog on a deleted row would let a user press Save or Delete on
// it, which is nonsensical for something already in the trash. Now that trash mode renders
// through the same MediaGrid/MediaFileList as the active list -- whose whole tile/row IS
// clickable and both bind @open unconditionally -- that protection has to live here instead of
// there being no handler wired up. Do not "simplify" this guard away: it looks redundant next to
// an unconditional @open, but removing it re-opens (literally) the trash-item hazard.
function openDetail(id: string): void {
  if (mode.value === 'trash') return
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
// Review fix (Finding 3): removes just the ONE affected id from `selection` rather than clearing
// it wholesale -- the user's other selected items are still perfectly valid. This matters because
// a ghost id (one deleted/purged/removed out from under an active selection) makes performMove
// throw AFTER its sibling writes have already settled (mediaMoveActions.ts's Promise.allSettled +
// "first rejection wins" re-throw), so a later batch move on a selection containing that ghost id
// would report "Move failed" even though every OTHER file in the same batch was actually moved --
// and, without this, the batch would stay stuck failing forever since nothing ever removed it.
function onDeleted(): void {
  if (selected.value) selection.value = removeFromSelection(selection.value, 'file', selected.value.id)
  selected.value = null
  loadClampingToLastValidPage()
}

async function onRestore(id: string): Promise<void> {
  try {
    await filesApi.restore(id)
    selection.value = removeFromSelection(selection.value, 'file', id)
    await loadClampingToLastValidPage()
  } catch (e) {
    toast.add({ severity: 'error', summary: e instanceof Error ? e.message : t('media.deleteFailed'), life: 3500 })
  }
}

async function onPurge(id: string): Promise<void> {
  if (!(await confirm.require({ ...purgeConfirm(t), severity: 'danger' }))) return
  try {
    await filesApi.remove(id, { purge: true })
    selection.value = removeFromSelection(selection.value, 'file', id)
    await loadClampingToLastValidPage()
  } catch (e) {
    toast.add({ severity: 'error', summary: e instanceof Error ? e.message : t('media.deleteFailed'), life: 3500 })
  }
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

function enterFolder(id: string): void { clearSelection(); currentFolderId.value = id; page.value = 0; load() }
function goToBreadcrumb(id: string | null): void { clearSelection(); currentFolderId.value = id; page.value = 0; load() }

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

async function onRemoveFolder(folder: FolderRow): Promise<void> {
  const accepted = await confirm.require({
    header: t('media.folderDelete'),
    message: t('media.folderDeleteConfirm', { name: folder.name }),
    severity: 'danger',
  })
  if (!accepted) return
  try {
    await itemsApi.remove('mediafolder', folder.id)
    selection.value = removeFromSelection(selection.value, 'folder', folder.id)
    if (currentFolderId.value === folder.id) currentFolderId.value = folder.parentId
    await loadFolders()
    await load()
  } catch (e) {
    if (e instanceof ApiError && e.status === 409)
      toast.add({ severity: 'warn', summary: t('media.folderNotEmpty'), life: 4000 })
    else
      toast.add({ severity: 'error', summary: e instanceof Error ? e.message : t('media.folderSaveFailed'), life: 3500 })
  }
}

// Drag and drop: files onto folder cards/rows, folders onto folder cards/rows, either onto a
// breadcrumb segment. Grid tiles, folder cards, and MediaFileList's folder/file rows are each
// gated on canWrite/canManageFolders in their own template; this handler itself stays
// permission-blind and lets performMove's own writes fail/succeed through the normal
// RBAC-enforced API instead of duplicating the check here.
async function onDropOn(targetFolderId: string | null, payload: MovePayload): Promise<void> {
  if (!payload.files.length && !payload.folders.length) return
  // Everything visible outside search mode lives in currentFolderId, so a drop onto that same
  // folder is a no-op there. Drags have no target to reach this with while searching
  // (visibleFolders is [] and the breadcrumb <nav> is v-if-ed on !searchActive), but the context
  // menu's "Move to..." and the move dialog are NOT gated on searchActive, and search drops the
  // folder filter entirely -- so a listed search result can genuinely live in any folder,
  // including the one currentFolderId happens to point at. Skip the no-op guard in that case
  // rather than silently discarding a real move.
  // Normalise falsy parent ids to null before comparing -- folderTree.ts's toFolderRows can in
  // principle produce parentId: '' from an M2O relation whose id happens to be an empty string,
  // and isNoOpMove's === check would otherwise treat '' and null as different roots.
  const target = targetFolderId || null
  if (!searchActive.value && isNoOpMove(currentFolderId.value || null, target)) return
  try {
    const { moved, skipped } = await performMove(payload, target, folders.value)
    if (skipped > 0)
      toast.add({ severity: 'warn', summary: t('media.moveSkippedCycle', { n: skipped }), life: 4000 })
    if (moved > 0)
      toast.add({ severity: 'success', summary: t('media.moved', { n: moved }), life: 2500 })
  } catch (e) {
    toast.add({ severity: 'error', summary: e instanceof Error ? e.message : t('media.moveFailed'), life: 3500 })
  } finally {
    await loadFolders()
    await loadClampingToLastValidPage()
  }
}

// Routes the move-to dialog's choice through the same perform/toast/reload path as a drag-drop,
// rather than duplicating any of that here.
//
// Review fix (Finding 2): clearing lives HERE, not inside onDropOn's own finally. onDropOn also
// serves plain drags and context-menu moves that never touch `selection` at all, and clearing an
// unrelated in-progress selection there would be a surprising side effect of an unconnected move.
// onMoveSubmit is specifically the move-DIALOG path -- both the toolbar's batch move and a
// single-item context-menu move funnel through it -- and after either one, the current
// `selection`'s ids may no longer be in the listing once the dialog's move settles and reloads.
function onMoveSubmit(targetFolderId: string | null): void {
  clearSelection()
  void onDropOn(targetFolderId, movePayload.value)
}

// The context menu's "Move to…" entry (Task 8) is the first real UI trigger for the move dialog
// Task 7 only wired internally -- both single-file and single-folder payloads land here.
function onRequestMove(payload: MovePayload): void {
  movePayload.value = payload
  moveDialogOpen.value = true
}

// Single-file delete from the context menu. Files had no per-item delete path outside
// MediaDetailDialog before this task -- mirrors that dialog's own onDelete (soft-delete confirm,
// then filesApi.remove, matching MediaDetailDialog.vue) rather than duplicating its logic.
async function onRemoveFile(id: string): Promise<void> {
  if (!(await confirm.require(deleteConfirm(t, 'soft')))) return
  try {
    await filesApi.remove(id)
    selection.value = removeFromSelection(selection.value, 'file', id)
    // The detail dialog may be open on the very file just deleted (e.g. deleted from the grid
    // while its own dialog is still up in another interaction) -- close it so Save/Delete can't
    // be pressed on a file that no longer exists.
    if (selected.value?.id === id) selected.value = null
    await loadClampingToLastValidPage()
  } catch (e) {
    toast.add({ severity: 'error', summary: e instanceof Error ? e.message : t('media.deleteFailed'), life: 3500 })
  }
}

const crumbDropping = ref<string | null>(null)

function onCrumbDrop(ev: DragEvent, targetFolderId: string | null): void {
  crumbDropping.value = null
  const payload = readDragPayload(ev)
  if (payload) void onDropOn(targetFolderId, payload)
}
// A drag can end without ever reaching a drop (Esc, or a drop somewhere that isn't a registered
// target) -- nothing else resets the breadcrumb highlight in that case. Crumb buttons are never
// drag sources themselves (they only accept drops), and the drag may have started on a MediaGrid
// tile or a MediaFolderCards card -- listen at the document level so it clears regardless of
// where the drag began, same reasoning as MediaFolderCards' own dropping highlight.
function clearCrumbDropping(): void { crumbDropping.value = null }

onMounted(() => {
  loadFolders()
  load()
  document.addEventListener('dragend', clearCrumbDropping)
})
onUnmounted(() => {
  debouncedSearch.cancel()
  document.removeEventListener('dragend', clearCrumbDropping)
})

defineExpose({ load, reload, onType, onSort, onPageChange, onPageSizeChange, onSearchInput, openDetail, onDeleted,
  files, total, loading, error, canWrite, canDelete, selected,
  folders, currentFolderId, visibleFolders, breadcrumb, enterFolder,
  goToBreadcrumb, onCreateFolder, onRenameFolder, onRemoveFolder, createOpen, renameTarget,
  mode, setMode, onModeToggle, onViewToggle, view, showTrashSwitch, onRestore, onPurge,
  onDropOn, moveDialogOpen, movePayload, onMoveSubmit, onRequestMove, onRemoveFile,
  selection, clearSelection, onToggleSelect })
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

    <!-- Task 9's selection toolbar: a second trigger onto the same moveDialogOpen/movePayload
         state the context menu's "Move to…" entry (onRequestMove) already opens -- see that
         handler's own comment. `mode === 'active'` is belt-and-braces here (the selection is
         already guaranteed empty while browsing the trash by onToggleSelect's own refusal and by
         setMode always clearing on the way in), matching this codebase's established
         double-gating convention for permission-sensitive UI. -->
    <MediaSelectionToolbar
      v-if="mode === 'active'"
      :selection="selection" :can-move-files="canWrite" :can-move-folders="canManageFolders"
      @request-move="onRequestMove" @clear="clearSelection"
    />

    <p
      v-if="mode === 'trash'"
      role="status"
      class="trash-banner mb-3 flex items-center gap-2 rounded-md border border-warning bg-warning/10 px-3.5 py-2.5 text-sm"
    >
      <Trash2 class="size-4" aria-hidden="true" />
      {{ t('collectionList.trashNotice') }}
    </p>

    <nav v-if="mode === 'active' && !searchActive && (breadcrumb.length || folders.length)" class="media-crumb" :aria-label="t('media.title')">
      <!-- At the root, navigating "to the root" is a no-op that looks broken -- render it as
           inert text instead (the same treatment given below to the current folder's own
           crumb), and it stops being a drop target too: dropping onto the root while already
           viewing the root is a no-op the move path rejects anyway. -->
      <button v-if="currentFolderId !== null" type="button" class="media-crumb__link text-primary rounded-md"
              :data-dropping="crumbDropping === 'root' ? 'true' : undefined"
              @click="goToBreadcrumb(null)"
              @dragover.prevent="crumbDropping = 'root'"
              @dragleave="crumbDropping = null"
              @drop.prevent="onCrumbDrop($event, null)">{{ t('media.breadcrumbRoot') }}</button>
      <span v-else class="media-crumb__current">{{ t('media.breadcrumbRoot') }}</span>
      <template v-for="c in breadcrumb" :key="c.id">
        <ChevronRight class="media-crumb__sep size-3 text-muted-foreground" aria-hidden="true" />
        <!-- The current folder's crumb renders as a <span>, not a button, and is deliberately
             not a drop target -- dropping onto the folder you are already in is the no-op
             onDropOn already rejects. -->
        <button v-if="c.id !== currentFolderId" type="button" class="media-crumb__link text-primary rounded-md"
                :data-dropping="crumbDropping === c.id ? 'true' : undefined"
                @click="goToBreadcrumb(c.id)"
                @dragover.prevent="crumbDropping = c.id"
                @dragleave="crumbDropping = null"
                @drop.prevent="onCrumbDrop($event, c.id)">{{ c.name }}</button>
        <span v-else class="media-crumb__current">{{ c.name }}</span>
      </template>
    </nav>

    <p v-if="error" class="error" role="alert">{{ error }}</p>

    <!--
      Right-click empty space (Task 8): New folder / Upload, gated the same as the header's own
      buttons above (not additionally restricted by mode/search -- matching those buttons, which
      also aren't). Each item-level context menu (MediaFolderCards/MediaGrid/MediaFileList) stops
      contextmenu propagation on its own tile/card/row specifically so a right-click ON an item
      does not ALSO bubble up and open this one underneath it.

      The card outline below is deliberately drawn on this exact element -- the ContextMenuTrigger
      itself, not a wrapper around it -- because it exists to show the user where the right-click
      menu above actually works. If the styling ever moved onto a different element than the
      trigger, the outline would start claiming an area where right-click does nothing; see this
      view's own test for the assertion that pins the two together. min-h-80 (not just padding)
      matters here too: a folder with few or no items would otherwise leave almost no empty
      surface to right-click.
    -->
    <ContextMenu>
      <ContextMenuTrigger as="div" class="media-body rounded-xl border border-border bg-muted p-4 min-h-80">
        <MediaFolderCards v-if="mode === 'active' && view === 'grid'" :folders="visibleFolders"
                          :can-manage="canManageFolders || canDeleteFolders" :can-move="canManageFolders"
                          :can-rename="canManageFolders" :can-delete="canDeleteFolders"
                          :selection="selection"
                          @open="enterFolder" @rename="renameTarget = $event" @remove="onRemoveFolder"
                          @drop-on="onDropOn" @request-move="onRequestMove" @toggle-select="onToggleSelect" />

        <!-- canMove is not additionally gated on mode !== 'trash': trashed tiles being draggable is
             harmless because trash mode renders neither MediaFolderCards nor the breadcrumb nav
             (both v-if-ed on mode === 'active'), so there is never a drop target to receive one --
             same reasoning as why search mode needs no special-casing here either. -->
        <MediaGrid
          v-if="view === 'grid'" :files="files" :can-move="canWrite" :can-delete="canDelete"
          :trash-mode="mode === 'trash'" :selection="selection"
          @open="openDetail" @request-move="onRequestMove" @remove="onRemoveFile"
          @toggle-select="onToggleSelect"
        >
          <template v-if="mode === 'trash'" #actions="{ file }">
            <Button
              type="button" variant="ghost" size="icon-sm"
              :title="t('collectionList.restore')" :aria-label="t('collectionList.restore')"
              @click.stop="onRestore(file.id)"
            >
              <Undo2 aria-hidden="true" />
            </Button>
            <Button
              type="button" variant="ghost" size="icon-sm"
              class="text-destructive hover:text-destructive"
              :title="t('collectionList.purge')" :aria-label="t('collectionList.purge')"
              @click.stop="onPurge(file.id)"
            >
              <Trash2 aria-hidden="true" />
            </Button>
          </template>
        </MediaGrid>
        <MediaFileList
          v-else
          :files="files"
          :folders="mode === 'active' ? visibleFolders : []"
          :can-manage-folders="canManageFolders || canDeleteFolders"
          :can-move-files="canWrite"
          :can-move-folders="canManageFolders"
          :can-rename-folders="canManageFolders"
          :can-delete-folders="canDeleteFolders"
          :can-delete-files="canDelete"
          :trash-mode="mode === 'trash'"
          :selection="selection"
          @open="openDetail"
          @open-folder="enterFolder"
          @rename-folder="renameTarget = $event"
          @remove-folder="onRemoveFolder"
          @drop-on="onDropOn"
          @request-move="onRequestMove"
          @remove="onRemoveFile"
          @toggle-select="onToggleSelect"
        >
          <template v-if="mode === 'trash'" #actions="{ file }">
            <Button
              type="button" variant="ghost" size="icon-sm"
              :title="t('collectionList.restore')" :aria-label="t('collectionList.restore')"
              @click.stop="onRestore(file.id)"
            >
              <Undo2 aria-hidden="true" />
            </Button>
            <Button
              type="button" variant="ghost" size="icon-sm"
              class="text-destructive hover:text-destructive"
              :title="t('collectionList.purge')" :aria-label="t('collectionList.purge')"
              @click.stop="onPurge(file.id)"
            >
              <Trash2 aria-hidden="true" />
            </Button>
          </template>
        </MediaFileList>
        <!--
          `visibleFolders` already forces [] for search/trash (see its own definition above), so
          this condition needs no view-specific branch: list view now renders those same folders as
          rows inside MediaFileList (not MediaFolderCards), and this check must stay just as blind
          to which of the two components is rendering them as it already is to grid vs. list for
          files.
        -->
        <p v-if="!loading && !files.length && !visibleFolders.length" class="empty text-muted-foreground">{{ t(mode === 'trash' ? 'collectionList.emptyTrash' : 'media.empty') }}</p>
      </ContextMenuTrigger>
      <ContextMenuContent>
        <ContextMenuItem v-if="canManageFolders" data-test="menu-new-folder" @select="createOpen = true">
          {{ t('media.menuNewFolder') }}
        </ContextMenuItem>
        <ContextMenuItem v-if="canWrite" data-test="menu-upload" @select="uploadOpen = true">
          {{ t('media.menuUpload') }}
        </ContextMenuItem>
      </ContextMenuContent>
    </ContextMenu>

    <DataTablePagination
      v-if="total > 0"
      :page="page"
      :page-size="perPage"
      :total="total"
      :page-size-options="[24, 48, 96]"
      @update:page="onPageChange"
      @update:page-size="onPageSizeChange"
    />

    <MediaUploadDialog v-model:visible="uploadOpen" :folder-id="searchActive ? null : currentFolderId" @done="() => { clearSelection(); reload() }" />
    <MediaDetailDialog :file="selected" :can-write="canWrite" :can-delete="canDelete"
                       @close="selected = null" @saved="load" @deleted="onDeleted" />

    <MediaFolderNameDialog v-model:visible="createOpen" :header="t('media.folderNew')" @submit="onCreateFolder" />
    <MediaFolderNameDialog :visible="renameTarget !== null" :header="t('media.folderRename')"
                           :initial-name="renameTarget?.name" @update:visible="(v: boolean) => { if (!v) renameTarget = null }"
                           @submit="onRenameFolder" />
    <!-- Reachable via the context menu's "Move to…" entry (onRequestMove); Task 9's selection
         toolbar will be a second trigger onto the same moveDialogOpen/movePayload state. -->
    <MediaMoveDialog v-model:visible="moveDialogOpen" :folders="folders" :payload="movePayload" @submit="onMoveSubmit" />
  </section>
</template>

<style scoped>
.media-library { display: block; }
.error { color: var(--danger); font-size: 0.9rem; margin: 0 0 12px; }
.empty { text-align: center; padding: 40px 0; }
.media-crumb { display: flex; align-items: center; gap: 4px; margin: 0 0 12px; flex-wrap: wrap; }
.media-crumb__link { border: 0; background: none; padding: 2px 4px; cursor: pointer; font: inherit; }
.media-crumb__link:hover { text-decoration: underline; }
.media-crumb__link[data-dropping='true'] { outline: 2px solid var(--primary); outline-offset: 1px; }
.media-crumb__current { color: var(--fg); font-weight: 600; padding: 2px 4px; }
</style>
