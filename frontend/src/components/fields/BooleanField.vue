<script setup lang="ts">
import { Switch } from '@/components/ui/switch'
import type { FieldMeta } from '../../types/schema'

defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
// FieldInput dispatches `id` as a plain attribute onto whatever this component renders as its
// root. Disabling automatic fallthrough and re-binding `$attrs` onto Switch by hand keeps that id
// landing on the real switch button (which ItemForm's <FieldLabel :for> pairs with) instead of on
// the wrapper div below.
defineOptions({ inheritAttrs: false })
</script>

<template>
  <!-- ui/field's vertical orientation applies `[&>*]:w-full` to every DIRECT child of a <Field>.
       Without this wrapper, that rule would land straight on the Switch and stretch it across the
       whole form width — the original bug. This div exists purely to absorb that rule instead, so
       the switch keeps its natural (compact) size. Do not remove it as "pointless markup". -->
  <div>
    <Switch
      v-bind="$attrs"
      :model-value="modelValue === true"
      :disabled="disabled"
      @update:model-value="(v: boolean) => emit('update:modelValue', v)"
    />
  </div>
</template>
