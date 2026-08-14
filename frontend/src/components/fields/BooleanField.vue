<script setup lang="ts">
import { Switch } from '@/components/ui/switch'
import type { FieldMeta } from '../../types/schema'

defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
</script>

<template>
  <Switch
    :model-value="modelValue === true"
    :disabled="disabled"
    @update:model-value="(v: boolean) => emit('update:modelValue', v)"
  />
</template>
