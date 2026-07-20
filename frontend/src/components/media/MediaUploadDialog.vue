<script setup lang="ts">
import Dialog from 'primevue/dialog'
import MediaUploadDropzone from './MediaUploadDropzone.vue'

defineProps<{ visible: boolean }>()
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
    :style="{ width: '32rem' }"
    :dismissable-mask="true"
    @update:visible="emit('update:visible', $event)"
  >
    <MediaUploadDropzone @done="emit('done')" />
  </Dialog>
</template>
