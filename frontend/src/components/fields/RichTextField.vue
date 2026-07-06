<script setup lang="ts">
import RichTextInput from './RichTextInput.vue'
import type { FieldMeta } from '../../types/schema'
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
</script>
<template>
  <RichTextInput :model-value="((modelValue as string) ?? '')" :disabled="disabled"
    @update:model-value="(v: string) => $emit('update:modelValue', v)" />
</template>
