<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import {
  ContextMenu, ContextMenuContent, ContextMenuItem, ContextMenuSeparator, ContextMenuTrigger,
} from '@/components/ui/context-menu'
import { IN_TABLE_ACTIONS, DESTRUCTIVE_TABLE_ACTIONS, type TableAction } from './richTextTableActions'

defineOptions({ name: 'RichTextTableContextMenu' })

const props = defineProps<{ disabled?: boolean }>()
const emit = defineEmits<{ (e: 'action', action: TableAction): void }>()

const { t } = useI18n()
const actions = IN_TABLE_ACTIONS

// This re-check is the actual guard, not the `disabled` forwarded below. reka's ContextMenuItem
// already refuses to fire when disabled, but this component must not depend on that third-party
// behaviour as its only line of defence -- the same reasoning MediaContextMenu records. Exposed so
// a test can call it with `disabled` on, bypassing the DOM.
function run(action: TableAction): void {
  if (props.disabled) return
  emit('action', action)
}

// The separator sits before the first destructive entry, wherever IN_TABLE_ACTIONS puts it.
function needsSeparator(index: number): boolean {
  const action = actions[index]
  const previous = actions[index - 1]
  return !!action && !!previous
    && DESTRUCTIVE_TABLE_ACTIONS.has(action) && !DESTRUCTIVE_TABLE_ACTIONS.has(previous)
}

defineExpose({ run })
</script>

<template>
  <ContextMenu>
    <!-- `disabled` restores the native browser menu and suppresses ours entirely. -->
    <ContextMenuTrigger :disabled="props.disabled" as-child>
      <slot />
    </ContextMenuTrigger>
    <ContextMenuContent>
      <template v-for="(action, i) in actions" :key="action">
        <ContextMenuSeparator v-if="needsSeparator(i)" />
        <ContextMenuItem :data-cmd="`table-${action}`" @select="run(action)">
          {{ t(`fields.richtext.${action}`) }}
        </ContextMenuItem>
      </template>
    </ContextMenuContent>
  </ContextMenu>
</template>
