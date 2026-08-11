<script setup lang="ts">
import { computed } from 'vue'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import type { FieldMeta } from '../../types/schema'

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()

const options = computed(() => props.field.options ?? [])

// registry.ts's def() default `empty` is '', and the `select` interface entry does not override
// it, so select's real empty value is '', never null — a null arriving here would only mean an
// unexpected caller. Coercing through `?? ''` keeps the value a plain string in every case, so
// reka's SelectRoot never sees `undefined` and never flips into its non-reactive `passive`
// (uncontrolled) mode, which production never exercises. (The null-means-nothing-selected
// convention belongs to the relation picker's own field, task 21 — not to this one.)
const current = computed(() => String(props.modelValue ?? ''))
</script>

<template>
  <Select :model-value="current" :disabled="disabled" @update:model-value="(v) => emit('update:modelValue', String(v ?? ''))">
    <SelectTrigger class="w-full max-w-[480px]" :aria-label="field.label">
      <SelectValue :placeholder="field.label" />
    </SelectTrigger>
    <SelectContent>
      <SelectItem v-for="opt in options" :key="opt.value" :value="opt.value">{{ opt.label }}</SelectItem>
    </SelectContent>
  </Select>
</template>
