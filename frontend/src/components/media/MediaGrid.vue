<script setup lang="ts">
import FileThumbnail, { type FileRow } from './FileThumbnail.vue'

const props = defineProps<{ files: FileRow[]; selectable?: boolean; selectedId?: string | null }>()
const emit = defineEmits<{ (e: 'select', id: string): void }>()

function onClick(id: string): void {
  if (props.selectable) emit('select', id)
}
</script>

<template>
  <div class="media-grid">
    <button
      v-for="f in files"
      :key="f.id"
      type="button"
      class="media-tile"
      :class="{ 'is-selected': selectable && selectedId === f.id }"
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
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
  gap: 12px;
}

.media-tile {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 6px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: none;
  cursor: pointer;
  font: inherit;
  color: inherit;
  text-align: left;
  overflow: hidden;
}

.media-tile.is-selected {
  outline: 2px solid var(--accent);
  outline-offset: -1px;
}

.media-tile__name {
  font-size: 13px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
