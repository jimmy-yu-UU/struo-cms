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
