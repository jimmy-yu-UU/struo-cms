<script setup lang="ts">
import { ref, computed } from 'vue'
import { filesApi } from '../../api/filesApi'
import { fileTypeDisplay } from '../../lib/fileTypeDisplay'
import { resolveIcon } from '../../lib/icons'

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

// `tile` is the media-grid square; `sm` is the inline row thumbnail every list-shaped consumer
// (FilePicker's current value, FilesField's rows, MediaFileList, the trash table) needs. Before
// this prop each of those out-specified the tile height with its own compound selector, which only
// worked as long as the two scoped rules stayed at the exact specificities they happened to have.
const props = withDefaults(defineProps<{ file: FileRow; size?: 'tile' | 'sm' }>(), { size: 'tile' })
const broken = ref(false)
const isImage = computed(() => props.file.contentType.startsWith('image/') && !broken.value)
const src = computed(() => filesApi.contentUrl(props.file.id))
const typeDisplay = computed(() => fileTypeDisplay(props.file.contentType, props.file.fileName))
// lib/fileTypeDisplay returns PrimeIcons tokens because it predates the icon migration and lib/ is
// frozen; resolveIcon maps every token it can emit to a lucide component.
const typeIcon = computed(() => resolveIcon(typeDisplay.value.icon))
</script>

<template>
  <div class="file-thumb" :data-size="size">
    <img v-if="isImage" :src="src" :alt="file.fileName" loading="lazy" @error="broken = true" />
    <div v-else class="file-chip">
      <component :is="typeIcon" class="file-chip__icon size-8 text-muted-foreground" aria-hidden="true" />
      <span class="file-chip__meta">{{ typeDisplay.label }}</span>
    </div>
  </div>
</template>

<style scoped>
.file-thumb { display: flex; width: 100%; height: 120px; }
.file-thumb[data-size='sm'] { width: 56px; height: 44px; flex: none; }
.file-thumb img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  border-radius: var(--legacy-radius, 8px);
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
  border-radius: var(--legacy-radius, 8px);
  background: var(--surface-2);
  padding: 8px;
  overflow: hidden;
}
.file-chip__meta {
  font-size: 11px;
  font-weight: 700;
  letter-spacing: .04em;
  color: var(--legacy-muted);
}
</style>
