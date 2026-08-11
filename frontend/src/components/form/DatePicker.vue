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
const props = defineProps<{ modelValue: Date | null; disabled?: boolean; placeholder?: string }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: Date | null): void }>()
const { t, locale } = useI18n()

const formatter = computed(() => new DateFormatter(locale.value, { dateStyle: 'medium' }))
const label = computed(() =>
  props.modelValue ? formatter.value.format(props.modelValue) : (props.placeholder ?? t('fields.pickADate')),
)

// ui/calendar speaks @internationalized/date's DateValue: year/month/day only, month 1-based, no
// time and no time zone. The conversion to and from the CMS's plain JS Date lives entirely here.
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
  // its date is touched.
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
      <Button
        variant="outline"
        :disabled="disabled"
        :aria-label="t('fields.pickADate')"
        class="w-[240px] justify-start text-left font-normal"
      >
        <CalendarIcon class="mr-2 size-4" />
        {{ label }}
      </Button>
    </PopoverTrigger>
    <PopoverContent class="w-auto p-0">
      <Calendar :model-value="calendarValue" @update:model-value="onCalendarUpdate" />
    </PopoverContent>
  </Popover>
</template>
