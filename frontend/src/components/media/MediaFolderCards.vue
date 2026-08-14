<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { Folder, Pencil, Trash2 } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import type { FolderRow } from '../../lib/folderTree'
import { setDragPayload, isMediaDrag, readDragPayload } from '../../lib/mediaDnd'
import type { MovePayload } from '../../lib/mediaMove'

const props = defineProps<{ folders: FolderRow[]; canManage: boolean; canMove?: boolean }>()
const emit = defineEmits<{
  (e: 'open', id: string): void
  (e: 'rename', folder: FolderRow): void
  (e: 'remove', folder: FolderRow): void
  (e: 'dropOn', targetFolderId: string, payload: MovePayload): void
}>()

const droppingId = ref<string | null>(null)

// The `draggable` attribute alone does not gate this: a text-selection drag started anywhere
// inside a non-draggable card can still bubble a dragstart up to this handler. Refuse here too,
// so the permission check cannot be bypassed that way.
function onDragStart(ev: DragEvent, f: FolderRow): void {
  if (!props.canMove) return
  setDragPayload(ev, { files: [], folders: [f.id] })
}
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
// bubbled child event. A card -> own-child crossing (icon/name) targets the CARD itself with
// relatedTarget still inside it -- the highlight must survive that. A child -> outside crossing
// fires AT the child and bubbles up, with relatedTarget outside the card -- that one must clear
// it. Neither `.self` nor "did this fire on a child" can express that distinction; only checking
// whether relatedTarget is still contained in the card can.
function onDragLeave(ev: DragEvent): void {
  const next = ev.relatedTarget
  if (next instanceof Node && (ev.currentTarget as Node).contains(next)) return
  droppingId.value = null
}
// A drag can end without ever reaching a drop (Esc, or a drop somewhere that isn't a registered
// target) -- nothing else resets the highlight in that case, and it would sit stale on a card the
// pointer has long left. The drag may have started on a DIFFERENT component's element entirely
// (a MediaGrid file tile), so this listens at the document level rather than on this card's own
// elements, and clears regardless of where the drag began.
function clearDropping(): void { droppingId.value = null }
onMounted(() => document.addEventListener('dragend', clearDropping))
onUnmounted(() => document.removeEventListener('dragend', clearDropping))
</script>

<template>
  <div v-if="folders.length" class="folder-grid">
    <div v-for="f in folders" :key="f.id" class="folder-card rounded-xl border border-border hover:border-primary" role="button" tabindex="0"
         :draggable="canMove ? 'true' : undefined"
         :data-dropping="droppingId === f.id ? 'true' : undefined"
         @click="emit('open', f.id)" @keydown.enter.self="emit('open', f.id)"
         @dragstart="onDragStart($event, f)"
         @dragover.prevent="onDragOver($event, f.id)"
         @dragleave="onDragLeave"
         @drop.prevent="onDrop($event, f.id)">
      <Folder class="folder-card__icon size-4 shrink-0 text-primary" aria-hidden="true" />
      <span class="folder-card__name">{{ f.name }}</span>
      <span v-if="canManage" class="folder-card__actions flex">
        <Button type="button" variant="ghost" size="icon-sm" :aria-label="$t('media.folderRename')"
                @click.stop="emit('rename', f)">
          <Pencil aria-hidden="true" />
        </Button>
        <Button type="button" variant="ghost" size="icon-sm"
                class="text-destructive hover:text-destructive"
                :aria-label="$t('media.folderDelete')"
                @click.stop="emit('remove', f)">
          <Trash2 aria-hidden="true" />
        </Button>
      </span>
    </div>
  </div>
</template>

<style scoped>
.folder-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 12px; margin-bottom: 16px; }
.folder-card {
  display: flex; align-items: center; gap: 10px; padding: 10px 12px; cursor: pointer;
  background: var(--surface);
  transition: border-color var(--speed, .15s), box-shadow var(--speed, .15s);
}
.folder-card:hover { box-shadow: var(--shadow-1); }
.folder-card__name { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: .9rem; }
.folder-card[data-dropping='true'] { outline: 2px solid var(--primary); outline-offset: -2px; }
</style>
