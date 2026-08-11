<script setup lang="ts">
import { computed, useId } from 'vue'
import { Checkbox } from '@/components/ui/checkbox'
import { Label } from '@/components/ui/label'
import type { FieldMeta } from '../../types/schema'

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string[]): void }>()

const options = computed(() => props.field.options ?? [])
const selected = computed(() => (Array.isArray(props.modelValue) ? (props.modelValue as string[]) : []))
// Instance-scoped, index-based ids. NOT keyed by option value: RepeaterField renders the same
// sub-field once per row, so an id repeated across rows would make `<label for>` resolve to row
// one, and indexing by position (rather than by option value) additionally keeps a selector-unsafe
// option value out of the id.
const uid = useId()
const idFor = (index: number): string => `${uid}-${index}`

// PrimeVue's Checkbox owned the array arithmetic itself (array modelValue + a per-option `value`
// prop); the vendored Checkbox has no such mode, so the add/remove is ours here. Always build a
// new array — the CMS form model is snapshot-compared for dirtiness (lib/formDirty.ts), and
// mutating the incoming array in place would change it without that comparison ever seeing a diff.
function toggle(value: string, checked: boolean | 'indeterminate'): void {
  const next = checked === true
    ? [...selected.value, value]
    : selected.value.filter((v) => v !== value)
  emit('update:modelValue', next)
}
</script>

<template>
  <div role="group" :aria-label="field.label" class="flex flex-wrap gap-x-5 gap-y-2">
    <div v-for="(opt, i) in options" :key="opt.value" class="inline-flex items-center gap-2">
      <Checkbox
        :id="idFor(i)"
        :model-value="selected.includes(opt.value)"
        :disabled="disabled"
        @update:model-value="(v) => toggle(opt.value, v)"
      />
      <Label :for="idFor(i)">{{ opt.label }}</Label>
    </div>
  </div>
</template>
