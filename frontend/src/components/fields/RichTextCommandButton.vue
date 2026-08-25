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
    through data-active, so handing it to those four would add a class attribute the current
    toolbar never emits for no visible effect.
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
