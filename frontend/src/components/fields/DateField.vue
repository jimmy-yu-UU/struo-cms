<script setup lang="ts">
import { computed } from 'vue'
import DatePicker from '@/components/form/DatePicker.vue'
import { Input } from '@/components/ui/input'
import type { FieldMeta } from '../../types/schema'

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()

const timeOnly = computed(() => props.field.interface === 'time')
const isDateTime = computed(() => props.field.interface === 'dateTime')
const withTime = computed(() => isDateTime.value || timeOnly.value)
const showDate = computed(() => !timeOnly.value)

// aria-label overrides element contents, the same convention form/DatePicker.vue's trigger uses.
// A dateTime field renders both controls, so the time input's accessible name must say "time" or
// a screen reader announces two controls under the identical name once both carry a value. A
// time-only field renders nothing else to disambiguate from, so it keeps the plain field label.
const timeAriaLabel = computed(() => (isDateTime.value ? `${props.field.label} time` : props.field.label))

// The model arrives as a Date once a save round-trips through the API, or as an ISO string on the
// very first load of an existing item (the API's raw JSON). Both render identically; an
// unparsable string, or the registry's default empty value ('' for this interface), renders as no
// value rather than throwing.
const current = computed<Date | null>(() => {
  const v = props.modelValue
  if (v instanceof Date) return v
  if (typeof v === 'string' && v !== '') {
    const d = new Date(v)
    return Number.isNaN(d.getTime()) ? null : d
  }
  return null
})

// <input type="time"> speaks "HH:mm"; a null value must render as an empty string, not "null".
const timeText = computed(() => {
  const d = current.value
  if (!d) return ''
  return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
})

function atMidnight(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate(), 0, 0, 0, 0)
}

function onDate(v: Date | null): void {
  emit('update:modelValue', v)
}

function onTime(raw: string): void {
  if (raw === '') {
    // Clearing the time on a time-only field clears the field outright. A dateTime field still
    // has a date the user chose in `current`; dropping it because an unrelated control went blank
    // would be a surprising side effect, so it falls to midnight instead of null.
    emit('update:modelValue', timeOnly.value ? null : current.value ? atMidnight(current.value) : null)
    return
  }
  const [h, m] = raw.split(':').map((n) => Number.parseInt(n, 10))
  const base = current.value ?? new Date()
  emit('update:modelValue', new Date(base.getFullYear(), base.getMonth(), base.getDate(), h, m, 0, 0))
}
</script>

<template>
  <div class="flex flex-wrap items-center gap-2">
    <DatePicker
      v-if="showDate"
      :model-value="current"
      :disabled="disabled"
      :label="field.label"
      @update:model-value="onDate"
    />
    <Input
      v-if="withTime"
      type="time"
      :model-value="timeText"
      :disabled="disabled"
      :aria-label="timeAriaLabel"
      class="w-[130px]"
      @update:model-value="(v) => onTime(String(v ?? ''))"
    />
  </div>
</template>
