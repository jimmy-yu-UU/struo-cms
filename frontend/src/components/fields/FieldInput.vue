<script setup lang="ts">
import { computed } from 'vue'
import { getFieldType } from '../../lib/fieldTypes/registry'
import type { FieldMeta } from '../../types/schema'

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean; id?: string }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()

const def = computed(() => getFieldType(props.field.interface))
const isDisabled = computed(() => props.disabled === true || props.field.readOnly)
</script>

<template>
  <!-- `id` and the aria-invalid / aria-describedby the caller passes are forwarded, never generated
       here. A field whose rendered root IS its native control (TextField's Input) takes them
       directly; one with a wrapper root (DateField, MultiSelectField, SelectField, NumberField,
       JsonField, RelationInput) sets `inheritAttrs: false` and re-binds them onto the real control,
       so the caller's <label for> and error text reach it. Group fields (TagsField, KeyValueField,
       RepeaterField, FilesField, FileField, CheckboxGroupField) carry them on their group wrapper. -->
  <component :is="def.component" :id="id" :field="field" :model-value="modelValue" :disabled="isDisabled"
    @update:model-value="(v: unknown) => emit('update:modelValue', v)" />
</template>
