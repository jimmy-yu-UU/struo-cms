<script setup lang="ts">
import { onBeforeUnmount } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Editor } from '@tiptap/vue-3'
import { BubbleMenu } from '@tiptap/vue-3/menus'
import RichTextCommandButton from './RichTextCommandButton.vue'
import { RICH_TEXT_COMMANDS, type RichTextCommand } from './richTextCommands'
import { shouldShowBubbleMenu } from './richTextSelection'

defineOptions({ name: 'RichTextBubbleMenu' })

const props = defineProps<{ editor: Editor; disabled?: boolean }>()
defineEmits<{ (e: 'run', command: RichTextCommand): void }>()

const { t } = useI18n()

// A plain string, not left to default to a PluginKey instance: BubbleMenuView's own
// transactionHandler matches metadata with `tr.getMeta(this.pluginKey)`, and ProseMirror's
// Transaction#setMeta/getMeta key on the exact value passed -- a string looks itself up directly,
// while an object key looks up its own `.key` field, which PluginKey generates internally
// ("name$", with a numeric suffix past the first use of that name anywhere on the page) and this
// component never sees. Passing the same literal string here and in hide() below is what makes
// them the same bucket; confirmed by reading @tiptap/vue-3/menus's BubbleMenu.js and
// prosemirror-state's PluginKey/setMeta/getMeta directly, not assumed from the plugin's own
// "pluginKey defaults to bubbleMenu" comment (which describes the string-shaped default case, not
// this one).
const BUBBLE_MENU_PLUGIN_KEY = 'richTextBubbleMenu'

// Exposed for RichTextInput to force this menu away before opening a surface that steals DOM focus
// (the link dialog): a real click arms BubbleMenuPlugin's own `preventHide` on mousedown, which
// swallows the very next blur -- so once the dialog's autofocus moves DOM focus off the editor, the
// blur path this menu would otherwise rely on is a no-op, and it would linger beside the open
// dialog. Dispatching the plugin's own documented external-hide meta bypasses preventHide entirely,
// since that flag only guards the blur-driven path, not a direct call. Carries no steps, so the
// editor's selection is untouched. RichTextInput.test.ts's 'hides the bubble menu...' test
// dispatches a real mousedown-then-click specifically to arm preventHide first, so that passing
// actually proves this call (and the matching pluginKey string above) -- a bare VTU `.trigger`
// click, with no mousedown, leaves the ordinary blur path open and hides the menu on its own
// regardless of whether this function is ever called; that gap was caught by deleting this call and
// watching the test stay green before the dispatch order was fixed.
function hide(): void {
  props.editor.view.dispatch(props.editor.state.tr.setMeta(BUBBLE_MENU_PLUGIN_KEY, 'hide'))
}

defineExpose({ hide })

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
// Created eagerly here (script setup body), not in onMounted: not because it has to be. Upstream's
// own BubbleMenu.vue defers the actual work into a microtask -- its onMounted calls el.remove() and
// then nextTick(() => editor.registerPlugin(BubbleMenuPlugin(...))), so the BubbleMenuView
// constructor (and its own trailing `if (this.getShouldShow()) this.show()`) runs inside that
// nextTick callback, which fires only after the whole synchronous mount flush -- including this
// component's own onMounted -- has already completed (read directly from
// @tiptap/vue-3/dist/menus/index.js). A container created in this component's onMounted would
// already exist by the time BubbleMenuPlugin needs it. It is created here anyway because there is
// no reason to defer it: nothing else in this component has to run first.
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
  <BubbleMenu :editor="editor" :plugin-key="BUBBLE_MENU_PLUGIN_KEY" :should-show="shouldShowBubbleMenu"
    :append-to="appendBubbleMenuTo"
    class="rich-text__bubble z-50 flex gap-1 rounded-md border bg-popover p-1 shadow-md"
    role="toolbar" :aria-label="t('fields.richtext.selectionToolbar')">
    <RichTextCommandButton v-for="cmd in INLINE_COMMANDS" :key="cmd.id" :command="cmd"
      :editor="editor" :disabled="disabled" @run="$emit('run', cmd)" />
  </BubbleMenu>
</template>
