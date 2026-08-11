<script setup lang="ts">
import { Textarea } from '@/components/ui/textarea'
import type { FieldMeta } from '@/types/schema'
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
</script>
<template>
  <Textarea :model-value="(modelValue as string)" :disabled="disabled" :rows="6"
    :maxlength="field.maxLength ?? undefined"
    @update:model-value="$emit('update:modelValue', $event)" />
</template>
