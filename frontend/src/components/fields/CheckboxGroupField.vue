<script setup lang="ts">
import Checkbox from 'primevue/checkbox'
import type { FieldMeta } from '../../types/schema'
const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: string[]): void }>()
</script>
<template>
  <div class="checkbox-group">
    <label v-for="opt in field.options ?? []" :key="opt.value" class="checkbox-option">
      <Checkbox :model-value="(props.modelValue as string[])" :value="opt.value" :disabled="disabled"
        @update:model-value="(v: string[]) => $emit('update:modelValue', v ?? [])" />
      <span>{{ opt.label }}</span>
    </label>
  </div>
</template>
