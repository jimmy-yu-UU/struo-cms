<script setup lang="ts">
import { Checkbox } from '@/components/ui/checkbox'
import type { FieldMeta } from '../../types/schema'

defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()

// reka's checkbox model admits 'indeterminate'; this field never sets it, but the emit signature
// includes it, so collapse anything non-true to false rather than leaking the string outward.
function onUpdate(v: boolean | 'indeterminate'): void {
  emit('update:modelValue', v === true)
}
</script>

<template>
  <Checkbox :model-value="modelValue === true" :disabled="disabled" @update:model-value="onUpdate" />
</template>
