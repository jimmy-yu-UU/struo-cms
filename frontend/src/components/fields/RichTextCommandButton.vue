<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { Editor } from '@tiptap/vue-3'
import { Button } from '@/components/ui/button'
import { RICHTEXT_ACTIVE_BUTTON_CLASS, type RichTextCommand } from './richTextCommands'

defineOptions({ name: 'RichTextCommandButton' })

defineProps<{ command: RichTextCommand; editor: Editor; disabled?: boolean }>()
defineEmits<{ (e: 'run'): void }>()

const { t } = useI18n()
</script>

<template>
  <!--
    The active state is read here rather than handed down as a prop, so that every surface rendering
    a command gets it from one place instead of each re-deriving it. Reading it during render is what
    subscribes this component to it: tiptap/vue-3's Editor holds its state in a customRef that tracks
    on read, so isActive() re-runs without the parent passing anything down. That ref's trigger is
    deferred by two nested requestAnimationFrames, so the re-render lands a couple of frames after
    the transaction, not in the same tick -- a test that asserts data-active immediately after a
    click sees the old value and reads as a false red. RichTextInput.test.ts's waitForEditorReactivity
    is the wait that exists for this.

    Running the command stays with the parent, deliberately: run() takes a context carrying
    openImageDialog, which is the parent's own state and nothing this button should hold.

    Both bindings are gated on command.isActive being non-null, and that null is load-bearing.
    hr/image/undo/redo have no active state, and their buttons carry no data-active attribute at
    all -- not data-active="false". The optional call yields undefined for them, which is what makes
    Vue omit the attribute; a command that CAN be active renders the literal string instead. The
    override class follows the same gate because it only ever paints through data-active, so giving
    it to those four would add tokens to the class attribute they already render, for no effect.
  -->
  <Button type="button" variant="ghost" size="icon" :data-cmd="command.id"
    :data-active="command.isActive?.(editor)"
    :disabled="disabled" :aria-label="t(command.labelKey)" :title="t(command.labelKey)"
    :class="command.isActive ? RICHTEXT_ACTIVE_BUTTON_CLASS : undefined" @click="$emit('run')">
    <component :is="command.icon" v-if="command.icon" />
    <component :is="command.glyphTag" v-else-if="command.glyphTag">{{ command.glyph }}</component>
    <template v-else>{{ command.glyph }}</template>
  </Button>
</template>
