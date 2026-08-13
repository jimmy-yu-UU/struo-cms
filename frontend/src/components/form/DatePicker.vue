<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { CalendarDate, DateFormatter } from '@internationalized/date'
import type { DateValue } from 'reka-ui'
import { Calendar } from '@/components/ui/calendar'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Button } from '@/components/ui/button'
import { CalendarIcon } from '@lucide/vue'

// Talks JS Date on the outside; @internationalized/date's DateValue never leaks past this file.
// `label` is the localised field label (e.g. "Published at"), prefixed onto the trigger's
// accessible name — DatePicker has no `field` of its own to read one from, unlike the field
// wrappers that read `field.label` directly.
const props = defineProps<{
  modelValue: Date | null
  disabled?: boolean
  placeholder?: string
  label?: string
}>()
const emit = defineEmits<{ (e: 'update:modelValue', v: Date | null): void }>()
const { t, locale } = useI18n()

const formatter = computed(() => new DateFormatter(locale.value, { dateStyle: 'medium' }))
const displayText = computed(() =>
  props.modelValue ? formatter.value.format(props.modelValue) : (props.placeholder ?? t('fields.pickADate')),
)
// aria-label overrides element contents rather than supplementing them, so the accessible name
// must carry everything the sighted trigger text shows (the field label AND the current value or
// placeholder) — a fixed label would hide the stored value and, on a form with several date
// fields, make every trigger announce identically.
const accessibleName = computed(() => (
  props.label ? `${props.label}${t('fields.namePairSeparator')}${displayText.value}` : displayText.value
))

// ui/calendar speaks @internationalized/date's DateValue: year/month/day only, month 1-based, no
// time and no time zone. The conversion to and from the CMS's plain JS Date lives entirely here.
// Only a single CalendarDate is ever produced or consumed — Calendar's `multiple` prop is never
// set, so `onCalendarUpdate` never receives an array; if it ever did, `new Date(array, …)` would
// silently build an Invalid Date rather than throw, which is the real hazard of widening this
// contract, not the (harmless here) CalendarDateTime/ZonedDateTime siblings of DateValue.
const calendarValue = computed(() =>
  props.modelValue
    ? new CalendarDate(props.modelValue.getFullYear(), props.modelValue.getMonth() + 1, props.modelValue.getDate())
    : undefined,
)

function onCalendarUpdate(value: DateValue | undefined): void {
  if (!value) {
    emit('update:modelValue', null)
    return
  }
  // Carry the incoming value's time-of-day forward: DateValue cannot represent it, so a naive
  // round-trip through it would silently reset every dateTime field to midnight the first time
  // its date is touched. With no incoming value (the fresh-create, passive-mode path) the result
  // defaults to midnight, a defensible zero rather than an accident.
  const base = props.modelValue
  emit(
    'update:modelValue',
    new Date(
      value.year,
      value.month - 1,
      value.day,
      base?.getHours() ?? 0,
      base?.getMinutes() ?? 0,
      base?.getSeconds() ?? 0,
      base?.getMilliseconds() ?? 0,
    ),
  )
}
</script>

<template>
  <Popover>
    <PopoverTrigger as-child>
      <!-- type="button" is explicit here even though reka's PopoverTrigger (as-child) already
           merges its own type="button" onto whatever it wraps — an untyped native <button> would
           otherwise default to type="submit", and this trigger sits inside ItemForm.vue's
           <form @submit.prevent>. Stating it directly means this Button doesn't rely on the
           merge behaviour of the component wrapping it. -->
      <Button
        type="button"
        variant="outline"
        :disabled="disabled"
        :aria-label="accessibleName"
        class="w-[240px] justify-start text-left font-normal"
      >
        <CalendarIcon class="mr-2 size-4" />
        {{ displayText }}
      </Button>
    </PopoverTrigger>
    <PopoverContent class="w-auto p-0">
      <!--
        default-placeholder (not placeholder) seeds which month the grid opens on without taking
        over control of it: Calendar's own placeholder stays uncontrolled, so its internal
        watch(modelValue, …) can still move the grid on a later model change and the user can
        still navigate months freely. Left unset (undefined when there is no value yet), it falls
        back to Calendar's own default of today — the calendar remounts fresh on every popover
        open (PopoverContent is Presence-gated), so this has to be supplied on every mount, not
        just the first.
      -->
      <Calendar
        :model-value="calendarValue"
        :default-placeholder="calendarValue"
        @update:model-value="onCalendarUpdate"
      />
    </PopoverContent>
  </Popover>
</template>
