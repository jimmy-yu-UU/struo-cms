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
