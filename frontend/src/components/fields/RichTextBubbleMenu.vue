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

// Derived, not a second hand-written list: this is what makes the four-surface separation (spec
// §3) a property of the registry's own `group` rather than something this file could drift out of
// sync with by hand.
const INLINE_COMMANDS = RICH_TEXT_COMMANDS.filter((c) => c.group === 'inline')
</script>

<template>
  <!--
    No updateDelay/options/appendTo: upstream's defaults already match spec §8, and each one set
    here would be a decision needing its own justification. class lands on the plugin's own root
    div (BubbleMenu forwards attrs there), so it is styled here directly -- but position/left/top
    are not touched, since the plugin writes those inline every time it repositions the menu.
  -->
  <BubbleMenu :editor="editor" :should-show="shouldShowBubbleMenu"
    class="rich-text__bubble flex gap-1 rounded-md border bg-popover p-1 shadow-md"
    role="toolbar" :aria-label="t('fields.richtext.selectionToolbar')">
    <RichTextCommandButton v-for="cmd in INLINE_COMMANDS" :key="cmd.id" :command="cmd"
      :editor="editor" :disabled="disabled" @run="$emit('run', cmd)" />
  </BubbleMenu>
</template>
