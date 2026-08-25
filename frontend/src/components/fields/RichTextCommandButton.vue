<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import { RICHTEXT_ACTIVE_BUTTON_CLASS, type RichTextCommand } from './richTextCommands'

defineOptions({ name: 'RichTextCommandButton' })

defineProps<{ command: RichTextCommand; active?: boolean; disabled?: boolean }>()
defineEmits<{ (e: 'run'): void }>()

const { t } = useI18n()
</script>

<template>
  <!--
    The override class is applied only when the command HAS an active state (isActive !== null):
    hr/image/undo/redo carry no data-active binding at all today, and the override only ever paints
    through data-active, so handing it to those four would add extra tokens inside the class
    attribute they already render (buttonVariants' own ghost/icon output) for no visible effect.

    :data-active is gated the same way, for a reason that's easy to miss: an absent
    Boolean-typed prop resolves to `false` in Vue, not `undefined`, so an ungated binding would
    render data-active="false" on these four stateless commands instead of no attribute at all --
    gating on command.isActive keeps the attribute's presence tied to the command's own capability,
    not to whatever the active prop happens to resolve to.
  -->
  <Button type="button" variant="ghost" size="icon" :data-cmd="command.id"
    :data-active="command.isActive ? active : undefined"
    :disabled="disabled" :aria-label="t(command.labelKey)" :title="t(command.labelKey)"
    :class="command.isActive ? RICHTEXT_ACTIVE_BUTTON_CLASS : undefined" @click="$emit('run')">
    <component :is="command.icon" v-if="command.icon" />
    <component :is="command.glyphTag" v-else-if="command.glyphTag">{{ command.glyph }}</component>
    <template v-else>{{ command.glyph }}</template>
  </Button>
</template>
