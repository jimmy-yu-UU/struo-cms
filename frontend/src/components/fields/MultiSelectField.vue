<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  Combobox, ComboboxAnchor, ComboboxEmpty, ComboboxInput, ComboboxItem,
  ComboboxItemIndicator, ComboboxList, ComboboxTrigger, ComboboxViewport,
} from '@/components/ui/combobox'
import { Badge } from '@/components/ui/badge'
import { Check, ChevronsUpDown, X } from '@lucide/vue'
import type { FieldMeta } from '../../types/schema'

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string[]): void }>()
const { t } = useI18n()

const options = computed(() => props.field.options ?? [])
const selected = computed(() => (Array.isArray(props.modelValue) ? (props.modelValue as string[]) : []))
const selectedOptions = computed(() =>
  selected.value.map((v) => ({ value: v, label: options.value.find((o) => o.value === v)?.label ?? v })),
)

// The trigger's `aria-label` overrides accessible-name-from-contents entirely, so the visible
// summary text ("N selected") would otherwise never reach a screen reader — folding the count into
// the label itself is the only way an AT user learns whether anything is selected without opening
// the popup.
const triggerAccessibleName = computed(() =>
  selected.value.length
    ? `${props.field.label}${t('fields.nameListSeparator')}${t('fields.selectedCount', { n: selected.value.length })}`
    : props.field.label,
)

// The combobox owns neither the chip display nor the array arithmetic, so both live here. Emit a
// new array every time — this repo's immutability convention, and the parent must receive a
// distinct array rather than the one it passed in.
function toggleValue(value: string): void {
  const next = selected.value.includes(value)
    ? selected.value.filter((v) => v !== value)
    : [...selected.value, value]
  emit('update:modelValue', next)
}
</script>

<template>
  <div class="flex w-full max-w-[480px] flex-col gap-1.5">
    <!-- Chips live outside the trigger, not inside it: the trigger renders as a <button>, and a
         remove control nested inside another <button> is invalid HTML with real focus/click
         hazards. Each chip's own remove button carries the accessible name; the Badge text beside
         it is plain content. -->
    <div v-if="selectedOptions.length" class="flex flex-wrap gap-1.5">
      <Badge v-for="opt in selectedOptions" :key="opt.value" variant="secondary" class="gap-1 py-0.5 pr-1">
        {{ opt.label }}
        <button
          type="button"
          class="inline-flex size-4 items-center justify-center rounded-full hover:bg-foreground/10"
          :aria-label="t('fields.removeOption', { label: opt.label })"
          :disabled="disabled"
          @click="toggleValue(opt.value)"
        >
          <X class="size-3" />
        </button>
      </Badge>
    </div>
    <Combobox :model-value="selected" multiple :disabled="disabled">
      <ComboboxAnchor class="w-full">
        <ComboboxTrigger
          :aria-label="triggerAccessibleName"
          class="border-input flex w-full items-center justify-between gap-2 rounded-md border bg-transparent px-3 py-2 text-sm disabled:cursor-not-allowed disabled:opacity-50"
        >
          <span :class="selected.length ? '' : 'text-muted-foreground'">
            {{ selected.length ? t('fields.selectedCount', { n: selected.length }) : field.label }}
          </span>
          <ChevronsUpDown class="size-4 shrink-0 opacity-50" />
        </ComboboxTrigger>
      </ComboboxAnchor>
      <ComboboxList class="w-(--reka-combobox-trigger-width)">
        <ComboboxInput :placeholder="t('fields.searchOptions')" />
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
  </div>
</template>
