<script setup lang="ts">
import Checkbox from 'primevue/checkbox'
import type { FieldMeta } from '../../types/schema'
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
</script>
<template>
  <Checkbox :model-value="(modelValue as boolean)" :binary="true" :disabled="disabled"
    @update:model-value="$emit('update:modelValue', $event)" />
</template>
