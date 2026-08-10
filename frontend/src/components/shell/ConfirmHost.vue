<script setup lang="ts">
import { computed } from 'vue'
import { storeToRefs } from 'pinia'
import { useI18n } from 'vue-i18n'
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent,
  AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { useConfirmStore } from '@/stores/confirmStore'

const store = useConfirmStore()
const { open, request } = storeToRefs(store)
const { t } = useI18n()

const header = computed(() => request.value?.header ?? t('common.confirmDefaultHeader'))
const acceptLabel = computed(() => request.value?.acceptLabel ?? t('common.confirmAccept'))
const rejectLabel = computed(() => request.value?.rejectLabel ?? t('common.confirmReject'))

// Escape / outside-click close the dialog through reka-ui's own open state. Route that back
// through reject() so the awaiting caller resolves false instead of hanging.
function onOpenChange(next: boolean): void {
  if (!next && store.open) store.reject()
}
</script>

<template>
  <AlertDialog :open="open" @update:open="onOpenChange">
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle>{{ header }}</AlertDialogTitle>
        <AlertDialogDescription>{{ request?.message }}</AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <AlertDialogCancel @click="store.reject()">{{ rejectLabel }}</AlertDialogCancel>
        <AlertDialogAction
          :class="request?.severity === 'danger' ? 'bg-destructive text-white hover:bg-destructive/90' : undefined"
          @click="store.accept()"
        >
          {{ acceptLabel }}
        </AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
