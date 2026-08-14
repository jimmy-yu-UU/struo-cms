<script setup lang="ts">
import FileThumbnail, { type FileRow } from './FileThumbnail.vue'
import { setDragPayload } from '../../lib/mediaDnd'

const props = defineProps<{
  files: FileRow[]
  selectable?: boolean
  selectedId?: string | null
  multiple?: boolean
  selectedIds?: string[]
  canMove?: boolean
}>()
const emit = defineEmits<{
  (e: 'select', id: string): void
  (e: 'toggle', id: string): void
  (e: 'open', id: string): void
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
function onDragStart(ev: DragEvent, f: FileRow): void {
  setDragPayload(ev, { files: [f.id], folders: [] })
}
</script>

<template>
  <div class="media-grid">
    <div v-for="f in files" :key="f.id" class="media-tile-wrap relative"
         :draggable="canMove ? 'true' : undefined"
         @dragstart="onDragStart($event, f)">
      <button
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
