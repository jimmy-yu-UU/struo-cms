<script setup lang="ts">
import {
  NumberField as NumberFieldRoot,
  NumberFieldContent,
  NumberFieldDecrement,
  NumberFieldIncrement,
  NumberFieldInput,
} from '@/components/ui/number-field'
import type { FieldMeta } from '../../types/schema'

defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
// FieldInput dispatches id, aria-invalid and aria-describedby as plain attributes onto whatever this
// component renders as its root. Disabling automatic fallthrough and re-binding `$attrs` by hand
// keeps them landing on the real control that ItemForm's <FieldLabel :for> pairs with.
defineOptions({ inheritAttrs: false })
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()

// reka's NumberFieldRoot emits `undefined` on `update:modelValue` when the input is cleared and
// blurred (confirmed by direct observation, not assumed). The CMS
// model wants null: a nullable numeric column legitimately clears, and a NaN would survive into
// buildItemPayload and poison any arithmetic on the way. Normalise at this boundary so no
// downstream code has to know reka's convention.
function onUpdate(v: number | undefined): void {
  emit('update:modelValue', v === undefined || Number.isNaN(v) ? null : v)
}
</script>

<template>
  <NumberFieldRoot
    :model-value="typeof modelValue === 'number' ? modelValue : undefined"
    :disabled="disabled"
    class="max-w-[220px]"
    @update:model-value="onUpdate"
  >
    <NumberFieldContent>
      <NumberFieldDecrement />
      <NumberFieldInput v-bind="$attrs" />
      <NumberFieldIncrement />
    </NumberFieldContent>
  </NumberFieldRoot>
</template>
