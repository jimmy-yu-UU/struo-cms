<script setup lang="ts">
import { ref, computed } from 'vue'
import { filesApi } from '../../api/filesApi'
import { fileTypeDisplay } from '../../lib/fileTypeDisplay'

export type FileRow = {
  id: string
  fileName: string
  contentType: string
  size: number
  width?: number | null
  height?: number | null
  status?: string
  createdAt?: string
}

const props = defineProps<{ file: FileRow }>()
const broken = ref(false)
const isImage = computed(() => props.file.contentType.startsWith('image/') && !broken.value)
const src = computed(() => filesApi.contentUrl(props.file.id))
const typeDisplay = computed(() => fileTypeDisplay(props.file.contentType, props.file.fileName))
</script>

<template>
  <div class="file-thumb">
    <img v-if="isImage" :src="src" :alt="file.fileName" loading="lazy" @error="broken = true" />
    <div v-else class="file-chip">
      <i class="pi file-chip__icon" :class="typeDisplay.icon" aria-hidden="true" />
      <span class="file-chip__meta">{{ typeDisplay.label }}</span>
    </div>
  </div>
</template>

<style scoped>
.file-thumb {
  width: 100%;
  height: 120px;
  display: flex;
}
.file-thumb img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  border-radius: var(--radius, 8px);
}
.file-chip {
  width: 100%;
  height: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 4px;
  border: 1px solid var(--border);
  border-radius: var(--radius, 8px);
  background: var(--bg);
  padding: 8px;
  overflow: hidden;
}
.file-chip__icon { font-size: 32px; color: var(--muted); }
.file-chip__meta {
  font-size: 11px;
  font-weight: 700;
  letter-spacing: .04em;
  color: var(--muted);
}
</style>
