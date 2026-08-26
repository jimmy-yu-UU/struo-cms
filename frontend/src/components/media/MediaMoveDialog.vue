<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
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

/**
 * Folder ids in depth-first tree order (each parent immediately followed by its own subtree),
 * NOT the order `folders` happens to arrive in (the view loads them sorted by name). Rendering
 * raw `folders` order would let a child appear above unrelated root folders while indented as
 * if it descended from something not shown there -- indentation only communicates parentage
 * when row order matches tree order too.
 *
 * A folder is placed under exactly one parent bucket (its own `parentId`), so a given id can
 * only ever be pushed into `ordered` from one place -- `visited` guards against that id somehow
 * appearing in two buckets (malformed input) rather than against any real recursion cycle: a
 * true parent cycle (including a folder listed as its own parent) has no path from `null`, so
 * `visit` never reaches it in the first place. A folder unreachable from the root (an orphan
 * pointing at a missing/cyclic ancestor chain) is appended at the end in its original order
 * instead of being dropped, so the full `folders` list still renders.
 */
function orderIdsByTree(folders: FolderRow[]): string[] {
  const byParent = new Map<string | null, string[]>()
  for (const f of folders) {
    const siblings = byParent.get(f.parentId)
    if (siblings) siblings.push(f.id)
    else byParent.set(f.parentId, [f.id])
  }
  const visited = new Set<string>()
  const ordered: string[] = []
  function visit(parentId: string | null): void {
    for (const id of byParent.get(parentId) ?? []) {
      if (visited.has(id)) continue
      visited.add(id)
      ordered.push(id)
      visit(id)
    }
  }
  visit(null)
  for (const f of folders) {
    if (!visited.has(f.id)) ordered.push(f.id)
  }
  return ordered
}

const options = computed<MoveOption[]>(() => {
  const byId = new Map(props.folders.map((f) => [f.id, f]))
  return [
    { id: null, label: t('media.moveRoot'), depth: 0, disabled: false },
    ...orderIdsByTree(props.folders).map((id) => {
      const f = byId.get(id)!
      return {
        id: f.id,
        label: f.name,
        // folderPath is root-first and always ends with `f` itself, so its length minus one is
        // the folder's depth (a root folder's own path has length 1, i.e. depth 0). Reusing
        // folderPath here avoids a second tree walk just to compute depth.
        depth: folderPath(props.folders, f.id).length - 1,
        disabled: isCycle(f.id),
      }
    }),
  ]
})

function choose(option: MoveOption): void {
  // This guard is the real protection against a disabled option acting -- it is NOT redundant
  // with the button's `disabled` attribute. A real user click on a disabled native <button> never
  // reaches this handler (the browser suppresses it), and neither does @vue/test-utils'
  // trigger('click') (it checks the element's disabled state itself and refuses to dispatch), so
  // today the attribute alone would already be enough. But a later refactor -- swapping this
  // Button for a non-native element, moving @click to the wrapping <li>, or replacing `disabled`
  // with `aria-disabled` + CSS -- would silently stop that suppression, and a disabled option
  // would start emitting `submit`. This check is what actually prevents that outcome.
  if (option.disabled) return
  emit('submit', option.id)
  emit('update:visible', false)
}

defineExpose({ choose })
</script>

<template>
  <!-- reka names the open flag `open`; this component's published prop is `visible` (matching
       MediaFolderNameDialog), so the two are bridged here rather than renaming the prop. -->
  <Dialog :open="visible" @update:open="(v: boolean) => emit('update:visible', v)">
    <DialogContent class="max-w-[min(90vw,420px)] sm:max-w-[min(90vw,420px)]">
      <DialogHeader>
        <DialogTitle>{{ t('media.moveTo') }}</DialogTitle>
        <!--
          Not decoration. reka points DialogContent's aria-describedby at a DialogDescription id
          whether or not one is rendered, and warns on mount when nothing carries that id -- so a
          dialog without one leaves assistive tech following a dangling reference.
        -->
        <DialogDescription>{{ t('media.moveDescription') }}</DialogDescription>
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
