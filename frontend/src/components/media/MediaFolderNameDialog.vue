<script setup lang="ts">
import { ref, watch, computed } from 'vue'
import Dialog from 'primevue/dialog'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'

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
  <Dialog :visible="visible" modal :header="header" :style="{ width: 'min(90vw, 420px)' }"
          @update:visible="emit('update:visible', $event)">
    <label class="folder-name-field">
      <span>{{ $t('media.folderName') }}</span>
      <InputText v-model="name" autofocus @keydown.enter="onSubmit" />
    </label>
    <template #footer>
      <Button :label="$t('media.folderConfirm')" :disabled="!valid" @click="onSubmit" />
    </template>
  </Dialog>
</template>

<style scoped>
.folder-name-field { display: grid; gap: 4px; }
.folder-name-field > span { font-size: .8rem; color: var(--legacy-muted); font-weight: 500; }
</style>
