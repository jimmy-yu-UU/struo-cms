<script setup lang="ts">
import FileThumbnail, { type FileRow } from './FileThumbnail.vue'

const props = defineProps<{
  files: FileRow[]
  selectable?: boolean
  selectedId?: string | null
  multiple?: boolean
  selectedIds?: string[]
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
</script>

<template>
  <div class="media-grid">
    <button
      v-for="f in files"
      :key="f.id"
      type="button"
      class="media-tile"
      :class="{ 'is-selected': isSelected(f.id) }"
      @click="onClick(f.id)"
    >
      <FileThumbnail :file="f" />
      <span class="media-tile__name">{{ f.fileName }}</span>
    </button>
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
  border: 1px solid var(--border);
  border-radius: var(--radius-lg, 12px);
  background: var(--surface);
  cursor: pointer;
  font: inherit;
  color: inherit;
  text-align: left;
  overflow: hidden;
  transition: border-color var(--speed, .15s), box-shadow var(--speed, .15s);
}
.media-tile:hover { border-color: var(--accent); box-shadow: var(--shadow-1); }
.media-tile.is-selected { outline: 2px solid var(--accent); outline-offset: -1px; }
.media-tile__name {
  font-size: 13px;
  color: var(--fg);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
