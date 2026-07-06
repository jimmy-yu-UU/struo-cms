<script setup lang="ts">
import Select from 'primevue/select'
import type { FieldMeta } from '../../types/schema'
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
</script>
<template>
  <Select :model-value="modelValue" :options="field.options ?? []" option-label="label" option-value="value"
    :disabled="disabled" @update:model-value="$emit('update:modelValue', $event)" />
</template>
