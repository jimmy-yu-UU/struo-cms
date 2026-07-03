<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import Button from 'primevue/button'
import MediaGrid from '../components/media/MediaGrid.vue'
import MediaUploadDropzone from '../components/media/MediaUploadDropzone.vue'
import type { FileRow } from '../components/media/FileThumbnail.vue'
import { itemsApi } from '../api/itemsApi'
import { filesApi } from '../api/filesApi'
import { useLanguageStore } from '../stores/languageStore'

const router = useRouter()
const langStore = useLanguageStore()
const files = ref<FileRow[]>([])
const loading = ref(false)
const error = ref('')

async function load(): Promise<void> {
  loading.value = true
  error.value = ''
  try {
    const res = await itemsApi.list('file', { page: 0, rows: 50, locale: langStore.defaultCode || undefined })
    files.value = res.data as unknown as FileRow[]
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to load media.'
  } finally {
    loading.value = false
  }
}

async function onDelete(id: string): Promise<void> {
  try {
    await filesApi.remove(id)
    await load()
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Delete failed.'
  }
}

function onEdit(id: string): void {
  void router.push({ name: 'collection-item', params: { name: 'file', id } })
}

onMounted(load)
defineExpose({ load, onDelete, onEdit })
</script>

<template>
  <section class="media-library">
    <h1>Media Library</h1>
    <p v-if="error" class="error" role="alert">{{ error }}</p>
    <MediaUploadDropzone @done="load" />
    <MediaGrid :files="files" />
    <div v-if="files.length" class="media-actions">
      <template v-for="f in files" :key="f.id">
        <Button label="Edit" text size="small" @click="onEdit(f.id)" />
        <Button label="Delete" text severity="danger" size="small" @click="onDelete(f.id)" />
      </template>
    </div>
  </section>
</template>
