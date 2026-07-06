<script setup lang="ts">
import RadioButton from 'primevue/radiobutton'
import type { FieldMeta } from '../../types/schema'
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
</script>
<template>
  <div class="radio-group">
    <label v-for="opt in field.options ?? []" :key="opt.value" class="radio-option">
      <RadioButton :model-value="modelValue" :value="opt.value" :disabled="disabled"
        @update:model-value="$emit('update:modelValue', $event)" />
      <span>{{ opt.label }}</span>
    </label>
  </div>
</template>
