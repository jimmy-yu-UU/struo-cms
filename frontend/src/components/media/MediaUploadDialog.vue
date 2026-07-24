<script setup lang="ts">
import Dialog from 'primevue/dialog'
import MediaUploadDropzone from './MediaUploadDropzone.vue'

defineProps<{ visible: boolean; folderId?: string | null }>()
const emit = defineEmits<{
  (e: 'update:visible', value: boolean): void
  (e: 'done'): void
}>()
</script>

<template>
  <Dialog
    :visible="visible"
    modal
    :header="$t('media.uploadTitle')"
    :style="{ width: 'min(78vw, 1100px)' }"
    :breakpoints="{ '960px': '95vw' }"
    :dismissable-mask="true"
    @update:visible="emit('update:visible', $event)"
  >
    <MediaUploadDropzone :folder-id="folderId" @done="emit('done')" />
  </Dialog>
</template>
