<script setup lang="ts">
import InputNumber from 'primevue/inputnumber'
import type { FieldMeta } from '../../types/schema'
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
</script>
<template>
  <InputNumber :model-value="(modelValue as number)" :disabled="disabled"
    @update:model-value="$emit('update:modelValue', $event)" />
</template>
