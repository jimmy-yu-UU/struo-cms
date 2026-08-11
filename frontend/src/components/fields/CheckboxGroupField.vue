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

// Each checkbox models a single boolean with no array semantics of its own, so building the next
// array from the current selection and the toggled option is this component's job. `selected.value`
// aliases the live `modelValue` array, and this repository's immutability convention is that a
// value received through a prop is never mutated by its receiver — the emit must carry a distinct
// array back to the parent, not the same array altered in place.
function toggle(value: string, checked: boolean | 'indeterminate'): void {
  const next = checked === true
    ? [...selected.value, value]
    : selected.value.filter((v) => v !== value)
  emit('update:modelValue', next)
}
</script>

<template>
  <!-- Checkbox renders as a <button>, so tokens.css's base-layer `button:not(:disabled)` rule
       already gives it `cursor: pointer`. Label deliberately carries no cursor utility of its own:
       it is a separate element paired only through `for`/`id`, not the interactive control itself. -->
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
