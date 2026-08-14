<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import { selectionCount } from '../../lib/mediaSelection'
import type { MovePayload } from '../../lib/mediaMove'

// Task 9's selection toolbar: shown only while the shared batch selection (owned by
// MediaLibraryView, the same MovePayload passed to MediaGrid/MediaFolderCards/MediaFileList) is
// non-empty. Renders the count, a Move to… trigger onto the existing Task 7 dialog (via the same
// `requestMove` event name MediaGrid/MediaFolderCards/MediaFileList already use for their own
// context-menu "Move to…" entries, so the view wires this the exact same way), and a clear button.
const props = defineProps<{
  selection: MovePayload
  canMoveFiles: boolean
  canMoveFolders: boolean
}>()
const emit = defineEmits<{
  (e: 'requestMove', payload: MovePayload): void
  (e: 'clear'): void
}>()

const { t } = useI18n()

const count = computed(() => selectionCount(props.selection))

// A user must not be able to move what they could not move individually: files need
// canWrite('file'), folders need canWrite('mediafolder'). In practice the selection can only ever
// contain ids the user was already allowed to check (each surface's own toggle handler refuses
// otherwise -- see MediaGrid/MediaFolderCards/MediaFileList's own toggleBatchSelect/toggleFile
// Select/toggleFolderSelect), so this is defence in depth, not the only line of defence -- same
// reasoning as MediaContextMenu's onMove re-checking its own grant despite the entry already
// being hidden by a v-if.
const canMove = computed(() => {
  if (props.selection.files.length > 0 && !props.canMoveFiles) return false
  if (props.selection.folders.length > 0 && !props.canMoveFolders) return false
  return count.value > 0
})

// The real guard: a native <button>'s `disabled` attribute already stops a real click (and
// @vue/test-utils' trigger('click') refuses to dispatch on one), but this handler is reachable
// through any future path that isn't a plain DOM click on this exact element, so it re-checks the
// same grant rather than trusting the attribute alone.
function onMove(): void {
  if (!canMove.value) return
  emit('requestMove', props.selection)
}

defineExpose({ onMove })
</script>

<template>
  <!-- bg-card, not bg-surface: --color-surface is not registered under tokens.css's @theme block
       (only --color-card/--color-background/etc. are), so a bg-surface utility would silently
       generate no rule at all -- see MediaContextMenu.vue's own comment on the analogous
       text-destructive-foreground trap. bg-card + border-border matches TheTopbar's own panel
       styling (border-b border-border bg-card). -->
  <div v-if="count > 0" class="media-selection-toolbar flex items-center gap-2.5 rounded-md border border-border bg-card px-3.5 py-2 mb-3">
    <span class="text-sm">{{ t('media.selectionCount', { n: count }) }}</span>
    <Button type="button" size="sm" data-test="selection-move" :disabled="!canMove" @click="onMove">
      {{ t('media.moveTo') }}
    </Button>
    <Button type="button" variant="ghost" size="sm" data-test="selection-clear" @click="emit('clear')">
      {{ t('media.selectionClear') }}
    </Button>
  </div>
</template>
