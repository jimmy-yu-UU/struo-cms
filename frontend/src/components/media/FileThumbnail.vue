<script setup lang="ts">
import { ref, computed } from 'vue'
import { filesApi } from '../../api/filesApi'

export type FileRow = { id: string; fileName: string; contentType: string; size: number }

const props = defineProps<{ file: FileRow }>()
const broken = ref(false)
const isImage = computed(() => props.file.contentType.startsWith('image/') && !broken.value)
const src = computed(() => filesApi.contentUrl(props.file.id))
</script>

<template>
  <div class="file-thumb">
    <img v-if="isImage" :src="src" :alt="file.fileName" loading="lazy" @error="broken = true" />
    <div v-else class="file-chip">
      <span class="file-chip__name">{{ file.fileName }}</span>
      <span class="file-chip__meta">{{ file.contentType }}</span>
    </div>
  </div>
</template>

<style scoped>
.file-thumb {
  width: 100%;
  height: 100px;
  display: flex;
}

.file-thumb img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  border-radius: 4px;
}

.file-chip {
  width: 100%;
  height: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 2px;
  border: 1px solid var(--border);
  border-radius: 4px;
  background: var(--code-bg);
  padding: 4px;
  overflow: hidden;
}

.file-chip__name {
  font-size: 12px;
  max-width: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.file-chip__meta {
  font-size: 11px;
  color: var(--text);
}
</style>
