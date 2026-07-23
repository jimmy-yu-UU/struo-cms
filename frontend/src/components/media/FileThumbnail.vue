<script setup lang="ts">
import { ref, computed } from 'vue'
import { filesApi } from '../../api/filesApi'

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
</script>

<template>
  <div class="file-thumb">
    <img v-if="isImage" :src="src" :alt="file.fileName" loading="lazy" @error="broken = true" />
    <div v-else class="file-chip">
      <i class="pi pi-file file-chip__icon" aria-hidden="true" />
      <span class="file-chip__meta">{{ file.contentType }}</span>
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
.file-chip__icon { font-size: 20px; color: var(--muted); }
.file-chip__meta {
  font-size: 11px;
  color: var(--muted);
}
</style>
