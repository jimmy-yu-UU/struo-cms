<script setup lang="ts">
import { computed } from 'vue'
import { getFieldType } from '../../lib/fieldTypes/registry'
import type { FieldMeta } from '../../types/schema'

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()

const def = computed(() => getFieldType(props.field.interface))
const isDisabled = computed(() => props.disabled === true || props.field.readOnly)
</script>

<template>
  <component :is="def.component" :field="field" :model-value="modelValue" :disabled="isDisabled"
    @update:model-value="(v: unknown) => emit('update:modelValue', v)" />
</template>
