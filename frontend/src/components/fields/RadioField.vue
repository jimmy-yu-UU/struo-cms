<script setup lang="ts">
import { computed, useId } from 'vue'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { Label } from '@/components/ui/label'
import type { FieldMeta } from '../../types/schema'

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()

const options = computed(() => props.field.options ?? [])
// registry.ts's def() default `empty` is '', and the `radio` interface entry does not override it,
// so this total coercion keeps reka's RadioGroupRoot in controlled mode on every mount, matching
// SelectField's settled convention rather than admitting `undefined` (reka's `passive` mode).
const current = computed(() => String(props.modelValue ?? ''))
// Instance-scoped so a repeater rendering this field once per row never collides: field.name alone
// repeats identically across rows, which would make every row's <label for> and reka's own
// [for=…] name lookup resolve to the first row's element. Indexed rather than option-value-based so
// a value containing a selector-unsafe character (e.g. a quote) can never break that lookup.
const uid = useId()
const idFor = (index: number): string => `${uid}-${index}`
</script>

<template>
  <RadioGroup
    :model-value="current"
    :disabled="disabled"
    :aria-label="field.label"
    class="flex flex-wrap gap-x-5 gap-y-2"
    @update:model-value="(v) => emit('update:modelValue', v)"
  >
    <div v-for="(opt, i) in options" :key="opt.value" class="inline-flex items-center gap-2">
      <RadioGroupItem :id="idFor(i)" :value="opt.value" />
      <Label :for="idFor(i)">{{ opt.label }}</Label>
    </div>
  </RadioGroup>
</template>
