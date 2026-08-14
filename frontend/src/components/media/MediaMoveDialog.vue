<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { folderPath, type FolderRow } from '../../lib/folderTree'
import { canMoveFolder, type MovePayload } from '../../lib/mediaMove'

const props = defineProps<{ visible: boolean; folders: FolderRow[]; payload: MovePayload }>()
const emit = defineEmits<{ (e: 'update:visible', v: boolean): void; (e: 'submit', targetId: string | null): void }>()

const { t } = useI18n()

type MoveOption = { id: string | null; label: string; depth: number; disabled: boolean }

// An option is disabled when moving ANY of the dragged folders there would create a cycle.
// canMoveFolder always returns true for a null target, so the root option is never disabled here.
function isCycle(folderId: string): boolean {
  return props.payload.folders.some((sourceId) => !canMoveFolder(props.folders, sourceId, folderId))
}

const options = computed<MoveOption[]>(() => [
  { id: null, label: t('media.moveRoot'), depth: 0, disabled: false },
  ...props.folders.map((f) => ({
    id: f.id,
    label: f.name,
    // folderPath is root-first and always ends with `f` itself, so its length minus one is the
    // folder's depth (a root folder's own path has length 1, i.e. depth 0). Reusing folderPath
    // here avoids a second tree walk.
    depth: folderPath(props.folders, f.id).length - 1,
    disabled: isCycle(f.id),
  })),
])

function choose(option: MoveOption): void {
  // Guarded here too, not only via the button's `disabled` attribute: a disabled native <button>
  // suppresses a real user click, but a programmatic click (including @vue/test-utils'
  // trigger('click')) does not respect that state, so the handler must refuse on its own.
  if (option.disabled) return
  emit('submit', option.id)
  emit('update:visible', false)
}
</script>

<template>
  <!-- reka names the open flag `open`; this component's published prop is `visible` (matching
       MediaFolderNameDialog), so the two are bridged here rather than renaming the prop. -->
  <Dialog :open="visible" @update:open="(v: boolean) => emit('update:visible', v)">
    <DialogContent class="max-w-[min(90vw,420px)] sm:max-w-[min(90vw,420px)]">
      <DialogHeader>
        <DialogTitle>{{ t('media.moveTo') }}</DialogTitle>
      </DialogHeader>
      <ul class="move-option-list grid max-h-[60vh] gap-1 overflow-y-auto">
        <li v-for="opt in options" :key="opt.id ?? '__root__'">
          <Button
            type="button"
            variant="ghost"
            class="w-full justify-start"
            data-test="move-option"
            :data-folder-id="opt.id ?? '__root__'"
            :style="{ paddingLeft: `${8 + opt.depth * 16}px` }"
            :disabled="opt.disabled"
            :aria-label="`${opt.label} — ${t('media.moveSubmit')}`"
            @click="choose(opt)"
          >
            {{ opt.label }}
          </Button>
        </li>
      </ul>
    </DialogContent>
  </Dialog>
</template>
