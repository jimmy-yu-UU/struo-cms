<script setup lang="ts">
import { onBeforeUnmount } from 'vue'
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

// One dedicated, unstyled div per component instance, appended to document.body and owned by this
// instance's lifecycle -- NOT `document.body` itself. BubbleMenuPlugin's blur guard is
// `this.element.parentNode?.contains(event.relatedTarget)`; when appendTo returned document.body
// directly, that parentNode WAS document.body, and document.body.contains(x) is true for every
// element on the page, so every blur was swallowed and hide() was never reached (confirmed: see
// RichTextBubbleMenu.test.ts's blur test, which fails without this container). Routing through this
// container instead restores the guard to its real meaning: true only for the menu's own subtree.
// Created eagerly here (script setup body), not in onMounted: Vue mounts children before parents, so
// BubbleMenu's own mount -- which can call show() synchronously if a selection is already present --
// runs before this component's onMounted would fire. The container must already exist by then.
// Deliberately left with no CSS of any kind (no position, no size): the menu itself is position:
// absolute and Floating UI resolves that against this container's nearest positioned ancestor, so a
// styled container could change where the menu lands. Left position: static (the default), it should
// be invisible to that resolution the same way document.body was -- unverified against a live
// browser, see the report for what still needs a manual re-check.
const bubbleMenuContainer = document.createElement('div')
document.body.appendChild(bubbleMenuContainer)

function appendBubbleMenuTo(): HTMLElement {
  return bubbleMenuContainer
}

onBeforeUnmount(() => {
  bubbleMenuContainer.remove()
})
</script>

<template>
  <!--
    No updateDelay/options: upstream's defaults already match what this bubble menu needs, and each
    one set here would be a decision needing its own justification. class lands on the plugin's own
    root div (BubbleMenu forwards attrs there), so it is styled here directly -- but position/left/top
    are not touched, since the plugin writes those inline every time it repositions the menu.

    appendTo bubbleMenuContainer (see script setup): without it, BubbleMenuPlugin's default
    (view.dom.parentElement) lands the menu inside AppShell's content column, which is
    overflow-x-clip (AppShell.vue), clipping any part of the menu that extends past the column edge.
    z-50 matches every other floating surface in this repo (PopoverContent, DialogContent,
    ContextMenuContent) -- without it the menu has no z-index at all and Sidebar's fixed z-10 layer
    (Sidebar.vue) paints over it.
  -->
  <BubbleMenu :editor="editor" :should-show="shouldShowBubbleMenu" :append-to="appendBubbleMenuTo"
    class="rich-text__bubble z-50 flex gap-1 rounded-md border bg-popover p-1 shadow-md"
    role="toolbar" :aria-label="t('fields.richtext.selectionToolbar')">
    <RichTextCommandButton v-for="cmd in INLINE_COMMANDS" :key="cmd.id" :command="cmd"
      :editor="editor" :disabled="disabled" @run="$emit('run', cmd)" />
  </BubbleMenu>
</template>
