<script setup lang="ts">
import { computed } from 'vue'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import type { FieldMeta } from '../../types/schema'

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()

const options = computed(() => (props.field.options ?? []) as { value: string; label: string }[])

// reka models "nothing selected" as undefined; the CMS model uses null. Convert at this boundary
// in both directions so neither side has to know the other's convention.
const current = computed(() => (props.modelValue == null ? undefined : String(props.modelValue)))

function selectValue(v: string): void {
  emit('update:modelValue', v)
}
defineExpose({ selectValue })
</script>

<template>
  <Select :model-value="current" :disabled="disabled" @update:model-value="(v) => selectValue(String(v))">
    <SelectTrigger class="w-full max-w-[480px]">
      <SelectValue :placeholder="field.label" />
    </SelectTrigger>
    <SelectContent>
      <SelectItem v-for="opt in options" :key="opt.value" :value="opt.value">{{ opt.label }}</SelectItem>
    </SelectContent>
  </Select>
</template>
