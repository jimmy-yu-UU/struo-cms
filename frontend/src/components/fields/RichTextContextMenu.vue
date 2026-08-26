<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  ContextMenu, ContextMenuContent, ContextMenuItem, ContextMenuSeparator, ContextMenuTrigger,
} from '@/components/ui/context-menu'
import { IN_TABLE_ACTIONS, DESTRUCTIVE_TABLE_ACTIONS, type TableAction } from './richTextTableActions'
import { IMAGE_ACTIONS, DESTRUCTIVE_IMAGE_ACTIONS, type ImageAction } from './richTextImageActions'

defineOptions({ name: 'RichTextContextMenu' })

// `target` says which content this menu shows for the CURRENT right-click: 'table' and 'image'
// each render their own action list, and `null` renders nothing (used for a right-click that
// landed on neither). It is a per-event decision, not a component-lifetime setting -- same shape
// as `disabled` below, and RichTextInput.vue's own comment on why a ref flip in the capture phase
// is (or is not) visible in time explains the constraint this prop lives under.
const props = defineProps<{ disabled?: boolean; target: 'table' | 'image' | null }>()
const emit = defineEmits<{
  (e: 'tableAction', action: TableAction): void
  (e: 'imageAction', action: ImageAction): void
}>()

const { t } = useI18n()

// Two independent emits rather than one discriminated-union emit: the two action types are
// unrelated enums with unrelated command handlers on the caller's side (see RichTextInput.vue's
// onTableAction), so a caller that only cares about one of them can listen to exactly that one
// event instead of narrowing a union on every payload.
type MenuEntry =
  | { kind: 'table'; action: TableAction }
  | { kind: 'image'; action: ImageAction }

const entries = computed<ReadonlyArray<MenuEntry>>(() => {
  if (props.target === 'table') return IN_TABLE_ACTIONS.map((action) => ({ kind: 'table', action }))
  if (props.target === 'image') return IMAGE_ACTIONS.map((action) => ({ kind: 'image', action }))
  return []
})

function isDestructive(entry: MenuEntry): boolean {
  return entry.kind === 'table'
    ? DESTRUCTIVE_TABLE_ACTIONS.has(entry.action)
    : DESTRUCTIVE_IMAGE_ACTIONS.has(entry.action)
}

// The separator sits before the first destructive entry of the CURRENT list, wherever that list
// puts it. This relies on the same constraint richTextTableActions.ts documents for
// IN_TABLE_ACTIONS and richTextImageActions.ts documents for IMAGE_ACTIONS: the destructive
// entries must stay contiguous within their list, or this predicate fires more than once. That
// constraint is not specific to tables -- it holds for whichever action list is active here.
function needsSeparator(index: number): boolean {
  const list = entries.value
  const entry = list[index]
  const previous = list[index - 1]
  return !!entry && !!previous && isDestructive(entry) && !isDestructive(previous)
}

// This re-check is the actual guard, not the `disabled` forwarded below. reka's ContextMenuItem
// already refuses to fire when disabled, but this component must not depend on that third-party
// behaviour as its only line of defence -- the same reasoning MediaContextMenu records. Both
// action kinds get their own guarded runner rather than sharing one, so each stays a plain
// single-argument function exposed for a test to call directly, bypassing the DOM.
function runTable(action: TableAction): void {
  if (props.disabled) return
  emit('tableAction', action)
}

function runImage(action: ImageAction): void {
  if (props.disabled) return
  emit('imageAction', action)
}

defineExpose({ runTable, runImage })
</script>

<template>
  <ContextMenu>
    <!-- `disabled` restores the native browser menu and suppresses ours entirely. -->
    <ContextMenuTrigger :disabled="props.disabled" as-child>
      <slot />
    </ContextMenuTrigger>
    <ContextMenuContent>
      <template v-for="(entry, i) in entries" :key="`${entry.kind}-${entry.action}`">
        <ContextMenuSeparator v-if="needsSeparator(i)" />
        <ContextMenuItem
          v-if="entry.kind === 'table'"
          :data-cmd="`table-${entry.action}`"
          @select="runTable(entry.action)"
        >
          {{ t(`fields.richtext.${entry.action}`) }}
        </ContextMenuItem>
        <ContextMenuItem
          v-else
          :data-cmd="`image-${entry.action}`"
          @select="runImage(entry.action)"
        >
          {{ t(`fields.richtext.${entry.action}`) }}
        </ContextMenuItem>
      </template>
    </ContextMenuContent>
  </ContextMenu>
</template>
