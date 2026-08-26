<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  ContextMenu, ContextMenuContent, ContextMenuItem, ContextMenuSeparator, ContextMenuTrigger,
} from '@/components/ui/context-menu'
import { IN_TABLE_ACTIONS, DESTRUCTIVE_TABLE_ACTIONS, type TableAction } from './richTextTableActions'
import { IMAGE_ACTIONS, DESTRUCTIVE_IMAGE_ACTIONS, type ImageAction } from './richTextImageActions'

defineOptions({ name: 'RichTextContextMenu' })

// `target` picks the action list for the CURRENT right-click; `null` renders nothing. Unlike
// `disabled`, it may be flipped from the contextmenu handler itself: reka reads `disabled`
// synchronously at the top of its own handler, but does not render this content until after an
// `await nextTick()`, by which time a prop update queued during event dispatch has flushed. Only
// a flip deferred past that microtask boundary (a `setTimeout`, say) would arrive too late.
const props = defineProps<{ disabled?: boolean; target: 'table' | 'image' | null }>()
const emit = defineEmits<{
  (e: 'tableAction', action: TableAction): void
  (e: 'imageAction', action: ImageAction): void
}>()

const { t } = useI18n()

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

// The separator sits before the first destructive entry of the CURRENT list. Every action list
// rendered here must keep its destructive entries contiguous, or this fires more than once.
function needsSeparator(index: number): boolean {
  const list = entries.value
  const entry = list[index]
  const previous = list[index - 1]
  return !!entry && !!previous && isDestructive(entry) && !isDestructive(previous)
}

// This re-check is the actual guard, not the `disabled` forwarded below: reka's ContextMenuItem
// already refuses to fire when disabled, but that third-party behaviour must not be the only line
// of defence -- the same reasoning MediaContextMenu records.
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
