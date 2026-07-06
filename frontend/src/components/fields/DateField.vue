<script setup lang="ts">
import { computed } from 'vue'
import DatePicker from 'primevue/datepicker'
import type { FieldMeta } from '../../types/schema'
const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
const timeOnly = computed(() => props.field.interface === 'time')
const showTime = computed(() => props.field.interface === 'dateTime')
</script>
<template>
  <DatePicker :model-value="(modelValue as Date)" :time-only="timeOnly" :show-time="showTime"
    :disabled="disabled" @update:model-value="$emit('update:modelValue', $event)" />
</template>
