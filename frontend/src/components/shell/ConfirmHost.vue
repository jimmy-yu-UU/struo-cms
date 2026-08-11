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

// reka-ui's AlertDialog only ever calls this for Escape. Route it back through reject() so the
// awaiting caller resolves false instead of hanging.
function onOpenChange(next: boolean): void {
  if (!next && store.open) store.reject()
}

// Not `@/components/ui/alert-dialog`'s AlertDialogAction/AlertDialogCancel: both
// are reka's DialogClose under the hood, which fires its own onOpenChange(false) synchronously
// — ahead of a plain @click handler, because Vue's mergeProps puts the component's own listener
// first — settling every confirmation false before our handler ever runs. Plain buttons styled
// with the vendored buttonVariants sidestep that: only store.accept()/reject() decide the
// outcome, and settle() happens as a side effect of that decision.
//
// These buttons call accept()/reject() with no id — they do not snapshot which request they
// were rendered for (Vue's cached inline-handler codegen re-reads reactive state at call time,
// not at render time, so a computed-returning-a-closure doesn't actually snapshot anything
// either). In practice this is safe: a real user click is a separate task from whatever called
// ask(), so Vue always flushes and re-renders in between — by the time a click lands, the
// button on screen already belongs to the current request. confirmStore's id guard exists for
// a different caller: code that captures an id across an await and might still be holding it
// after a supersede (see confirmStore.ts).
</script>

<template>
  <AlertDialog :open="open" @update:open="onOpenChange">
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle>{{ header }}</AlertDialogTitle>
        <AlertDialogDescription>{{ request?.message }}</AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <button type="button" :class="rejectClass" @click="store.reject()">{{ rejectLabel }}</button>
        <button type="button" :class="acceptClass" @click="store.accept()">{{ acceptLabel }}</button>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
