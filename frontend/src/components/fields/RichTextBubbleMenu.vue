<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { Editor } from '@tiptap/vue-3'
import { BubbleMenu } from '@tiptap/vue-3/menus'
import RichTextCommandButton from './RichTextCommandButton.vue'
import { RICH_TEXT_COMMANDS, type RichTextCommand } from './richTextCommands'
import { shouldShowBubbleMenu } from './richTextSelection'

defineOptions({ name: 'RichTextBubbleMenu' })

defineProps<{ editor: Editor; disabled?: boolean }>()
defineEmits<{ (e: 'run', command: RichTextCommand): void }>()

const { t } = useI18n()

// Derived, not a second hand-written list: this menu carries inline text formatting and nothing
// else, and deriving that from the registry's own `group` is what keeps it true, rather than a
// hand-kept list here that could drift out of sync.
const INLINE_COMMANDS = RICH_TEXT_COMMANDS.filter((c) => c.group === 'inline')

// A named function, not an inline template arrow: Vue's template compiler resolves any bare
// identifier not on its GLOBALS_ALLOWED whitelist (@vue/shared) as `_ctx.<name>` -- `document` is
// not on that list, so `:append-to="() => document.body"` in the template compiles to
// `_ctx.document.body`, which throws (the component has no `document` property). Defined here in
// script setup instead, this is plain JS closing over the real global.
function appendBubbleMenuTo(): HTMLElement {
  return document.body
}
</script>

<template>
  <!--
    No updateDelay/options: upstream's defaults already match what this bubble menu needs, and each
    one set here would be a decision needing its own justification. class lands on the plugin's own
    root div (BubbleMenu forwards attrs there), so it is styled here directly -- but position/left/top
    are not touched, since the plugin writes those inline every time it repositions the menu.

    appendTo document.body: without it, BubbleMenuPlugin's default (view.dom.parentElement) lands
    the menu inside AppShell's content column, which is overflow-x-clip (AppShell.vue), clipping any
    part of the menu that extends past the column edge. z-50 matches every other floating surface in
    this repo (PopoverContent, DialogContent, ContextMenuContent) -- without it the menu has no
    z-index at all and Sidebar's fixed z-10 layer (Sidebar.vue) paints over it.
  -->
  <BubbleMenu :editor="editor" :should-show="shouldShowBubbleMenu" :append-to="appendBubbleMenuTo"
    class="rich-text__bubble z-50 flex gap-1 rounded-md border bg-popover p-1 shadow-md"
    role="toolbar" :aria-label="t('fields.richtext.selectionToolbar')">
    <RichTextCommandButton v-for="cmd in INLINE_COMMANDS" :key="cmd.id" :command="cmd"
      :editor="editor" :disabled="disabled" @run="$emit('run', cmd)" />
  </BubbleMenu>
</template>
