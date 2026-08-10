<script setup lang="ts">
import { computed } from 'vue'
import { storeToRefs } from 'pinia'
import { useI18n } from 'vue-i18n'
import {
  AlertDialog, AlertDialogContent, AlertDialogDescription,
  AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { buttonVariants } from '@/components/ui/button'
import { useConfirmStore } from '@/stores/confirmStore'

const store = useConfirmStore()
const { open, request } = storeToRefs(store)
const { t } = useI18n()

const header = computed(() => request.value?.header ?? t('common.confirmDefaultHeader'))
const acceptLabel = computed(() => request.value?.acceptLabel ?? t('common.confirm'))
const rejectLabel = computed(() => request.value?.rejectLabel ?? t('common.cancel'))
const acceptClass = computed(() =>
  buttonVariants({ variant: request.value?.severity === 'danger' ? 'destructive' : 'default' }),
)
const rejectClass = computed(() => buttonVariants({ variant: 'outline' }))

// reka-ui's AlertDialog only ever routes Escape here — outside-click and overlay click are
// deliberately blocked by AlertDialog's own semantics (onPointerDownOutside/onInteractOutside
// are prevented). Route Escape back through reject() so the awaiting caller resolves false
// instead of hanging.
function onOpenChange(next: boolean): void {
  if (!next && store.open) store.reject()
}

// Deliberately NOT `@/components/ui/alert-dialog`'s AlertDialogAction/AlertDialogCancel: both
// are reka's DialogClose under the hood, which fires its own onOpenChange(false) synchronously
// — ahead of a plain @click handler, because Vue's mergeProps puts the component's own listener
// first — settling every confirmation false before our handler ever runs. Plain buttons styled
// with the vendored buttonVariants sidestep that: only store.accept()/reject() decide the
// outcome, and settle() happens as a side effect of that decision.
//
// Each handler is a computed (not an inline template expression) so the id it closes over is
// fixed at the render that produced *this* button, not re-read at click time. If a concurrent
// ask() supersedes the request before Vue re-renders, the still-attached (stale) button keeps
// calling accept()/reject() with the *old* id, and settle() in the store ignores it — instead
// of resolving the new, current request.
const onAcceptClick = computed(() => {
  const id = store.requestId
  return () => store.accept(id)
})
const onRejectClick = computed(() => {
  const id = store.requestId
  return () => store.reject(id)
})
</script>

<template>
  <AlertDialog :open="open" @update:open="onOpenChange">
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle>{{ header }}</AlertDialogTitle>
        <AlertDialogDescription>{{ request?.message }}</AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <button type="button" :class="rejectClass" @click="onRejectClick">{{ rejectLabel }}</button>
        <button type="button" :class="acceptClass" @click="onAcceptClick">{{ acceptLabel }}</button>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
