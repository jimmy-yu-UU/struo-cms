<script setup lang="ts">
import { computed } from 'vue'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { Label } from '@/components/ui/label'
import type { FieldMeta } from '../../types/schema'

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()

const options = computed(() => (props.field.options ?? []) as { value: string; label: string }[])
const current = computed(() => (props.modelValue == null ? undefined : String(props.modelValue)))
const idFor = (value: string): string => `${props.field.name}-${value}`
</script>

<template>
  <RadioGroup
    :model-value="current"
    :disabled="disabled"
    class="flex flex-wrap gap-x-5 gap-y-2"
    @update:model-value="(v) => emit('update:modelValue', v)"
  >
    <div v-for="opt in options" :key="opt.value" class="inline-flex items-center gap-2">
      <RadioGroupItem :id="idFor(opt.value)" :value="opt.value" />
      <Label :for="idFor(opt.value)">{{ opt.label }}</Label>
    </div>
  </RadioGroup>
</template>
