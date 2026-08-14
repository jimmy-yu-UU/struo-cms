<script setup lang="ts">
import { computed } from 'vue'
import FileThumbnail, { type FileRow } from './FileThumbnail.vue'
import MediaContextMenu from './MediaContextMenu.vue'
import { setDragPayload } from '../../lib/mediaDnd'
import type { MovePayload } from '../../lib/mediaMove'

const props = defineProps<{
  files: FileRow[]
  selectable?: boolean
  selectedId?: string | null
  multiple?: boolean
  selectedIds?: string[]
  canMove?: boolean
  canDelete?: boolean
  // Trash tiles already carry restore/purge as slot actions -- the right-click menu must not
  // duplicate that (or worse, offer Open, which MediaLibraryView deliberately refuses in trash
  // mode). true forwards straight to reka's own ContextMenuTrigger `disabled`, which also
  // restores the native browser menu.
  trashMode?: boolean
}>()
const emit = defineEmits<{
  (e: 'select', id: string): void
  (e: 'toggle', id: string): void
  (e: 'open', id: string): void
  (e: 'requestMove', payload: MovePayload): void
  (e: 'remove', id: string): void
}>()

function onClick(id: string): void {
  if (props.multiple) { emit('toggle', id); return }
  if (props.selectable) { emit('select', id); return }
  emit('open', id)
}
function isSelected(id: string): boolean {
  return props.multiple
    ? !!props.selectedIds?.includes(id)
    : props.selectable === true && props.selectedId === id
}
// Tiles are drag sources only -- a file is not a drop target, so there is no dragover/drop
// handling here (unlike MediaFolderCards).
//
// The `draggable` attribute on the wrapper is NOT sufficient to gate this on its own: a child
// <img> (FileThumbnail) is draggable by default in every browser, and its native dragstart
// bubbles up to this same handler regardless of the wrapper's own attribute. Refuse here too,
// so the permission check cannot be bypassed by dragging the thumbnail image itself.
function onDragStart(ev: DragEvent, f: FileRow): void {
  if (!props.canMove) return
  setDragPayload(ev, { files: [f.id], folders: [] })
}

// MediaGrid has three other callers besides MediaLibraryView -- FilePicker (selectable),
// FilesField (multiple) and RichTextInput (selectable) -- none of which listens for `open`, and
// "Open" is meaningless in picker mode anyway (a click there means select/toggle, not navigate).
// Rendering the context menu unconditionally in those dialogs would silently do nothing on
// select AND eat the native browser context menu (reka's own trigger calls
// event.preventDefault() as soon as it opens) -- so the menu only exists in plain browse mode.
const showContextMenu = computed(() => !props.selectable && !props.multiple)
</script>

<template>
  <div class="media-grid">
    <div v-for="f in files" :key="f.id" class="media-tile-wrap relative"
         :draggable="canMove ? 'true' : undefined"
         @dragstart="onDragStart($event, f)">
      <MediaContextMenu
        v-if="showContextMenu"
        kind="file" :can-move="canMove" :can-delete="canDelete" :disabled="trashMode"
        @open="emit('open', f.id)"
        @move="emit('requestMove', { files: [f.id], folders: [] })"
        @remove="emit('remove', f.id)"
      >
        <button
          type="button"
          class="media-tile w-full rounded-xl border border-border hover:border-primary"
          :class="{ 'is-selected outline-2 -outline-offset-1 outline-primary': isSelected(f.id) }"
          @click="onClick(f.id)"
          @contextmenu.stop
          @pointerdown.stop
        >
          <FileThumbnail :file="f" />
          <span class="media-tile__name">{{ f.fileName }}</span>
        </button>
      </MediaContextMenu>
      <button
        v-else
        type="button"
        class="media-tile w-full rounded-xl border border-border hover:border-primary"
        :class="{ 'is-selected outline-2 -outline-offset-1 outline-primary': isSelected(f.id) }"
        @click="onClick(f.id)"
      >
        <FileThumbnail :file="f" />
        <span class="media-tile__name">{{ f.fileName }}</span>
      </button>
      <div v-if="$slots.actions" class="media-tile__actions absolute right-1 top-1 flex gap-1">
        <slot name="actions" :file="f" />
      </div>
    </div>
  </div>
</template>

<style scoped>
.media-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(160px, 1fr));
  gap: 16px;
}
.media-tile {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 8px;
  background: var(--surface);
  cursor: pointer;
  font: inherit;
  color: inherit;
  text-align: left;
  overflow: hidden;
  transition: border-color var(--speed, .15s), box-shadow var(--speed, .15s);
}
.media-tile:hover { box-shadow: var(--shadow-1); }
.media-tile__name {
  font-size: 13px;
  color: var(--fg);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
