<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  Combobox, ComboboxAnchor, ComboboxEmpty, ComboboxInput, ComboboxItem,
  ComboboxItemIndicator, ComboboxList, ComboboxTrigger, ComboboxViewport,
} from '@/components/ui/combobox'
import { Badge } from '@/components/ui/badge'
import { Check, ChevronsUpDown } from '@lucide/vue'
import type { FieldMeta } from '../../types/schema'

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string[]): void }>()
const { t } = useI18n()

const options = computed(() => props.field.options ?? [])
const selected = computed(() => (Array.isArray(props.modelValue) ? (props.modelValue as string[]) : []))
const selectedLabels = computed(() =>
  selected.value.map((v) => options.value.find((o) => o.value === v)?.label ?? v),
)

// PrimeVue's MultiSelect owned both the chip display and the array arithmetic. The combobox owns
// neither, so both live here. Emit a new array every time — this repo's immutability convention,
// and the parent must receive a distinct array rather than the one it passed in.
//
// Do NOT justify this with a dirty-check claim: `snapshotModel` returns a STRING and `isDirty`
// re-serialises the live model, so in-place mutation would still register as dirty. Save is gated
// on write permission, never on dirtiness.
function toggleValue(value: string): void {
  const next = selected.value.includes(value)
    ? selected.value.filter((v) => v !== value)
    : [...selected.value, value]
  emit('update:modelValue', next)
}
</script>

<template>
  <Combobox :model-value="selected" multiple :disabled="disabled">
    <ComboboxAnchor class="w-full max-w-[480px]">
      <ComboboxTrigger
        :aria-label="field.label"
        class="border-input flex w-full items-center justify-between gap-2 rounded-md border bg-transparent px-3 py-2 text-sm disabled:cursor-not-allowed disabled:opacity-50"
      >
        <div class="flex flex-1 flex-wrap items-center gap-1.5">
          <Badge v-for="label in selectedLabels" :key="label" variant="secondary">{{ label }}</Badge>
          <span v-if="!selected.length" class="text-muted-foreground">{{ field.label }}</span>
        </div>
        <ChevronsUpDown class="size-4 shrink-0 opacity-50" />
      </ComboboxTrigger>
    </ComboboxAnchor>
    <ComboboxList>
      <ComboboxInput :placeholder="field.label" />
      <ComboboxEmpty>{{ t('fields.noOptions') }}</ComboboxEmpty>
      <ComboboxViewport>
        <ComboboxItem
          v-for="opt in options"
          :key="opt.value"
          :value="opt.value"
          @select.prevent="toggleValue(opt.value)"
        >
          {{ opt.label }}
          <ComboboxItemIndicator><Check class="size-4" /></ComboboxItemIndicator>
        </ComboboxItem>
      </ComboboxViewport>
    </ComboboxList>
  </Combobox>
</template>
