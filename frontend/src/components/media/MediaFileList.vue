<script setup lang="ts">
import { ref, useSlots, onMounted, onUnmounted } from 'vue'
import { Folder, Pencil, Trash2 } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import FileThumbnail, { type FileRow } from './FileThumbnail.vue'
import MediaContextMenu from './MediaContextMenu.vue'
import { formatFileSize } from '../../lib/formatFileSize'
import { formatDateTime } from '../../lib/formatDateTime'
import type { FolderRow } from '../../lib/folderTree'
import { setDragPayload, isMediaDrag, readDragPayload } from '../../lib/mediaDnd'
import type { MovePayload } from '../../lib/mediaMove'

const props = withDefaults(defineProps<{
  files: FileRow[]
  folders?: FolderRow[]
  canManageFolders?: boolean
  canMoveFiles?: boolean
  canMoveFolders?: boolean
  // Right-click menu grants, kept separate from canManageFolders (which gates the existing
  // always-together rename+delete icon buttons) so the menu can gate Rename/Delete independently.
  canRenameFolders?: boolean
  canDeleteFolders?: boolean
  canDeleteFiles?: boolean
  // Trash rows already carry restore/purge as slot actions -- the file-row menu must not
  // duplicate that. Folder rows never appear in trash mode (the view always passes an empty
  // folders array there), so this only needs to gate the file-row menu.
  trashMode?: boolean
}>(), { folders: () => [], canManageFolders: false, canMoveFiles: false, canMoveFolders: false })

const emit = defineEmits<{
  (e: 'open', id: string): void
  (e: 'openFolder', id: string): void
  (e: 'renameFolder', folder: FolderRow): void
  (e: 'removeFolder', folder: FolderRow): void
  (e: 'dropOn', targetFolderId: string, payload: MovePayload): void
  (e: 'requestMove', payload: MovePayload): void
  (e: 'remove', id: string): void
}>()

// Folder rows are both drag sources (of the folder itself) and drop targets (for files/folders
// dropped onto them); file rows are drag sources only -- a file is never a drop target, mirroring
// MediaGrid. This mirrors MediaFolderCards' drag/drop plumbing onto <tr>s instead of cards.
const droppingId = ref<string | null>(null)

// `draggable` alone does not gate this: FileThumbnail renders an <img>, which is draggable by
// default in every browser, and its dragstart bubbles up to this row handler regardless of the
// row's own attribute. Refuse here too, so the permission check cannot be bypassed that way.
function onFileDragStart(ev: DragEvent, f: FileRow): void {
  if (!props.canMoveFiles) return
  setDragPayload(ev, { files: [f.id], folders: [] })
}
// Same bypass risk for folder rows (a bubbled drag from inside the row).
function onFolderDragStart(ev: DragEvent, f: FolderRow): void {
  if (!props.canMoveFolders) return
  setDragPayload(ev, { files: [], folders: [f.id] })
}
// dragover fires continuously while a drag hovers -- only check the cheap `isMediaDrag`, never
// canMoveFolder (it rebuilds an internal Map per call). Validation happens at drop time inside
// performMove, in the parent view.
function onDragOver(ev: DragEvent, id: string): void {
  if (!isMediaDrag(ev)) return
  droppingId.value = id
}
function onDrop(ev: DragEvent, id: string): void {
  droppingId.value = null
  const payload = readDragPayload(ev)
  if (payload) emit('dropOn', id, payload)
}
// dragleave follows the mouseout model: it fires on every boundary crossing, not only as a
// bubbled child event. A row -> own-child crossing (an inner <td>) targets the ROW itself
// (target === currentTarget) with relatedTarget still inside it -- the highlight must survive
// that. A child -> outside crossing fires AT the child and bubbles up, with relatedTarget outside
// the row -- that one must clear it. Neither `.self` nor "did this fire on a child" can express
// that distinction; only checking whether relatedTarget is still contained in the row can.
function onDragLeave(ev: DragEvent): void {
  const next = ev.relatedTarget
  if (next instanceof Node && (ev.currentTarget as Node).contains(next)) return
  droppingId.value = null
}
// A drag can end without ever reaching a drop (Esc, or a drop somewhere that isn't a registered
// target) -- nothing else resets the highlight in that case. The drag may have started on a
// DIFFERENT component's element entirely (a MediaGrid file tile or a MediaFolderCards card), so
// this listens at the document level rather than on this row's own elements, and clears
// regardless of where the drag began.
function clearDropping(): void { droppingId.value = null }
onMounted(() => document.addEventListener('dragend', clearDropping))
onUnmounted(() => document.removeEventListener('dragend', clearDropping))

const slots = useSlots()
// The actions column exists when the caller supplied its own #actions slot (trash mode's
// restore/purge) OR when there are folder rows to show manage buttons for. Both the header
// <th> and every body <td> call this SAME function so the column count can never diverge
// between <thead> and <tbody>.
//
// This MUST stay a plain function, not a computed. `useSlots()` returns `instance.slots`, a
// plain object Vue mutates in place (via `updateSlots()`) rather than a reactive source --
// reading `slots.actions` inside a `computed` cannot be tracked, so the computed would only
// re-evaluate when one of its OTHER, genuinely-reactive deps (`props.folders`/
// `props.canManageFolders`) changes. This component survives an active<->trash mode toggle
// (it's only `v-else`'d on `view`, not on `mode`), so a `computed` version would go stale the
// moment the parent ever hands it a stable `folders` array identity across that toggle --
// the restore/purge actions would silently vanish with no change to the column count. Calling
// a plain function on every render reads `slots.actions` fresh every time and has no such trap.
function showActionsColumn(): boolean {
  return !!slots.actions || (props.folders.length > 0 && props.canManageFolders)
}

function dims(f: FileRow): string {
  return f.width && f.height ? `${f.width}×${f.height}` : '—'
}
function uploaded(f: FileRow): string {
  return formatDateTime(f.createdAt ?? null)
}
</script>

<template>
  <table class="media-list">
    <thead>
      <tr>
        <th class="media-list__thumb-col text-muted-foreground" aria-hidden="true"></th>
        <th class="text-muted-foreground">{{ $t('media.colName') }}</th>
        <th class="text-muted-foreground">{{ $t('media.colType') }}</th>
        <th class="text-muted-foreground">{{ $t('media.colSize') }}</th>
        <th class="text-muted-foreground">{{ $t('media.colDimensions') }}</th>
        <th class="text-muted-foreground">{{ $t('media.colUploaded') }}</th>
        <th v-if="showActionsColumn()" class="media-list__actions-col text-muted-foreground"></th>
      </tr>
    </thead>
    <tbody>
      <MediaContextMenu
        v-for="d in folders" :key="`folder-${d.id}`"
        kind="folder" :can-move="canMoveFolders" :can-delete="canDeleteFolders" :can-rename="canRenameFolders"
        @open="emit('openFolder', d.id)"
        @rename="emit('renameFolder', d)"
        @move="emit('requestMove', { files: [], folders: [d.id] })"
        @remove="emit('removeFolder', d)"
      >
        <tr class="media-list__row"
            :draggable="canMoveFolders ? 'true' : undefined"
            :data-dropping="droppingId === d.id ? 'true' : undefined"
            @click="emit('openFolder', d.id)"
            @contextmenu.stop
            @dragstart="onFolderDragStart($event, d)"
            @dragover.prevent="onDragOver($event, d.id)"
            @dragleave="onDragLeave"
            @drop.prevent="onDrop($event, d.id)">
          <td class="media-list__thumb">
            <Folder class="size-5 text-primary" aria-hidden="true" />
          </td>
          <td class="media-list__name">
            <button
              type="button"
              class="media-list__open focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-primary"
              @click.stop="emit('openFolder', d.id)"
            >
              {{ d.name }}
            </button>
          </td>
          <td>{{ $t('media.colTypeFolder') }}</td>
          <td>—</td>
          <td>—</td>
          <td>—</td>
          <td v-if="showActionsColumn()" class="media-list__actions">
            <template v-if="canManageFolders">
              <Button type="button" variant="ghost" size="icon-sm" :aria-label="$t('media.folderRename')"
                      @click.stop="emit('renameFolder', d)">
                <Pencil aria-hidden="true" />
              </Button>
              <Button type="button" variant="ghost" size="icon-sm" class="text-destructive hover:text-destructive"
                      :aria-label="$t('media.folderDelete')" @click.stop="emit('removeFolder', d)">
                <Trash2 aria-hidden="true" />
              </Button>
            </template>
          </td>
        </tr>
      </MediaContextMenu>
      <!--
        A table row is not a button, so it must not claim `role="button"` (that gave
        screen readers contradictory roles). Table semantics stay intact on the <tr>; the real,
        natively keyboard-activatable <button> in the name cell (mirrors MediaGrid's tile
        buttons) is the accessible primary action. The row keeps its own @click purely as a
        mouse convenience so clicking anywhere in the row still opens the item, same as before.
      -->
      <MediaContextMenu
        v-for="f in files" :key="f.id"
        kind="file" :can-move="canMoveFiles" :can-delete="canDeleteFiles" :disabled="trashMode"
        @open="emit('open', f.id)"
        @move="emit('requestMove', { files: [f.id], folders: [] })"
        @remove="emit('remove', f.id)"
      >
        <tr
          class="media-list__row"
          :draggable="canMoveFiles ? 'true' : undefined"
          @click="emit('open', f.id)"
          @contextmenu.stop
          @dragstart="onFileDragStart($event, f)"
        >
          <td class="media-list__thumb"><FileThumbnail :file="f" size="sm" /></td>
          <td class="media-list__name">
            <button
              type="button"
              class="media-list__open focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-primary"
              @click.stop="emit('open', f.id)"
            >
              {{ f.fileName }}
            </button>
          </td>
          <td>{{ f.contentType }}</td>
          <td>{{ formatFileSize(f.size) }}</td>
          <td>{{ dims(f) }}</td>
          <td>{{ uploaded(f) }}</td>
          <td v-if="showActionsColumn()" class="media-list__actions">
            <slot name="actions" :file="f" />
          </td>
        </tr>
      </MediaContextMenu>
    </tbody>
  </table>
</template>

<style scoped>
.media-list {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9rem;
}
.media-list th {
  text-align: left;
  padding: 8px 12px;
  font-weight: 600;
  border-bottom: 1px solid var(--border);
}
.media-list__row {
  cursor: pointer;
  transition: background var(--speed, .15s);
}
.media-list__row:hover { background: var(--surface-2); }
.media-list td {
  padding: 8px 12px;
  border-bottom: 1px solid var(--border);
  color: var(--fg);
  vertical-align: middle;
}
.media-list__thumb-col { width: 64px; }
.media-list__thumb { width: 56px; }
.media-list__name { font-weight: 500; padding: 0; }
.media-list__open {
  display: block;
  width: 100%;
  padding: 8px 12px;
  margin: 0;
  border: 0;
  background: none;
  font: inherit;
  font-weight: inherit;
  color: inherit;
  text-align: left;
  cursor: pointer;
}
.media-list__actions-col { width: 6rem; }
.media-list__actions { display: flex; gap: 4px; justify-content: flex-end; }
.media-list__row[data-dropping='true'] td { box-shadow: inset 0 0 0 1px var(--primary); }
</style>
