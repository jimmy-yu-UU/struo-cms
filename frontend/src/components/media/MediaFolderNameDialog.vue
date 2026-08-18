<script setup lang="ts">
import { ref, watch, computed } from 'vue'
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'

const props = defineProps<{ visible: boolean; header: string; initialName?: string }>()
const emit = defineEmits<{ (e: 'update:visible', v: boolean): void; (e: 'submit', name: string): void }>()

const name = ref('')
// immediate: true so a dialog that's already mounted `visible` (e.g. a fresh mount, or a parent
// that swaps `initialName` at the same time as flipping `visible`) still seeds the field --
// a bare (non-immediate) watch only fires on a false->true *transition*.
watch(() => props.visible, (v) => { if (v) name.value = props.initialName ?? '' }, { immediate: true })
const valid = computed(() => name.value.trim().length > 0)

function onSubmit(): void {
  if (!valid.value) return
  emit('submit', name.value.trim())
  emit('update:visible', false)
}
</script>

<template>
  <!-- reka names the open flag `open`; this component's published prop is `visible` (MediaLibraryView
       binds it with v-model:visible), so the two are bridged here rather than renaming the prop. -->
  <Dialog :open="visible" @update:open="(v: boolean) => emit('update:visible', v)">
    <!--
      Two copies of the same min(90vw, 420px) value are needed because tailwind-merge keys
      conflicts on (modifier set, class group): the bare max-w-[min(90vw,420px)] displaces
      DialogContent's own bare max-w-[calc(100%-2rem)], and the sm:-prefixed copy separately
      displaces its sm:max-w-lg (512px) -- a bare override alone would leave sm:max-w-lg alive
      and winning from the sm breakpoint (640px) up, since a bare and an sm:-scoped class in the
      same group don't conflict with each other.
    -->
    <DialogContent class="max-w-[min(90vw,420px)] sm:max-w-[min(90vw,420px)]">
      <DialogHeader>
        <DialogTitle>{{ header }}</DialogTitle>
      </DialogHeader>
      <label class="folder-name-field grid gap-1">
        <span class="text-xs font-medium text-muted-foreground">{{ $t('media.folderName') }}</span>
        <Input v-model="name" autofocus @keydown.enter="onSubmit" />
      </label>
      <div class="flex justify-end">
        <Button
          type="button"
          data-test="folder-name-confirm"
          :disabled="!valid"
          @click="onSubmit"
        >
          {{ $t('media.folderConfirm') }}
        </Button>
      </div>
    </DialogContent>
  </Dialog>
</template>
