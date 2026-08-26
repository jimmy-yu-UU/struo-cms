<script setup lang="ts">
import { Dialog, DialogScrollContent, DialogHeader, DialogTitle, DialogDescription } from '@/components/ui/dialog'
import MediaUploadDropzone from './MediaUploadDropzone.vue'

defineProps<{ visible: boolean; folderId?: string | null }>()
const emit = defineEmits<{
  (e: 'update:visible', value: boolean): void
  (e: 'done'): void
}>()
</script>

<template>
  <Dialog :open="visible" @update:open="(v: boolean) => emit('update:visible', v)">
    <!--
      DialogScrollContent, not DialogContent: the dropzone is min-height min(68vh, 680px) and grows
      a row per queued file, and reka's DialogRoot locks body scroll while open — a fixed-position,
      viewport-centered box leaves no scroll container, so the rows past the viewport edge become
      unreachable. DialogScrollContent's overlay carries its own overflow-y-auto and holds the box
      in normal flow.

      Its own width class is an UNPREFIXED max-w-lg (unlike DialogContent's sm:max-w-lg), so this
      override supplies no modifier either — a bare max-w-* only loses to another bare max-w-*.
      max-[960px]:max-w-[95vw] reproduces the old `:breakpoints="{ '960px': '95vw' }"`.
    -->
    <DialogScrollContent class="max-w-[min(78vw,1100px)] max-[960px]:max-w-[95vw]">
      <DialogHeader>
        <DialogTitle>{{ $t('media.uploadTitle') }}</DialogTitle>
        <!--
          Not decoration. reka points DialogContent's aria-describedby at a DialogDescription id
          whether or not one is rendered, and warns on mount when nothing carries that id -- so a
          dialog without one leaves assistive tech following a dangling reference.
        -->
        <DialogDescription>{{ $t('media.uploadDescription') }}</DialogDescription>
      </DialogHeader>
      <MediaUploadDropzone :folder-id="folderId" @done="emit('done')" />
    </DialogScrollContent>
  </Dialog>
</template>
