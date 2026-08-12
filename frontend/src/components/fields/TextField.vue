<script setup lang="ts">
import { Input } from '@/components/ui/input'
import type { FieldMeta } from '@/types/schema'
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
</script>
<template>
  <Input :model-value="(modelValue as string)" :disabled="disabled"
    :maxlength="field.maxLength ?? undefined"
    @update:model-value="$emit('update:modelValue', $event)" />
</template>
