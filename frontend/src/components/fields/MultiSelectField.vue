<script setup lang="ts">
import MultiSelect from 'primevue/multiselect'
import type { FieldMeta } from '../../types/schema'
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: string[]): void }>()
</script>
<template>
  <MultiSelect :model-value="(modelValue as string[])" :options="field.options ?? []"
    option-label="label" option-value="value" :disabled="disabled" display="chip" :show-toggle-all="false"
    @update:model-value="(v: string[]) => $emit('update:modelValue', v ?? [])" />
</template>
