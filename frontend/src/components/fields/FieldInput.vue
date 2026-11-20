<script setup lang="ts">
import { computed } from 'vue'
import { getFieldType } from '../../lib/fieldTypes/registry'
import type { FieldMeta } from '../../types/schema'
import type { FieldDraft } from '../../lib/fieldDrafts'

const props = defineProps<{
  field: FieldMeta; modelValue: unknown; disabled?: boolean; id?: string
  // The unparsable text of a field that owns its input; handed only to such a field.
  draft?: FieldDraft | null
}>()
const emit = defineEmits<{
  (e: 'update:modelValue', v: unknown): void
  (e: 'update:draft', v: FieldDraft | null): void
}>()

const def = computed(() => getFieldType(props.field.interface))
const isDisabled = computed(() => props.disabled === true || props.field.readOnly)
const draftBindings = computed(() => (def.value.ownsInput
  ? { draft: props.draft, 'onUpdate:draft': (d: FieldDraft | null) => emit('update:draft', d) }
  : {}))
</script>

<template>
  <!-- `id` and the aria-invalid / aria-describedby the caller passes are forwarded, never generated
       here. A field whose rendered root IS its control (TextField, TextareaField, RadioField's
       group) takes them directly, and RichTextField passes them on as props to its editor. One with
       a wrapper root (BooleanField, DateField, MultiSelectField, SelectField, NumberField, JsonField,
       RelationInput) sets `inheritAttrs: false` and re-binds them onto the real control, so the
       caller's <label for> and error text reach it. Group fields (TagsField, KeyValueField,
       RepeaterField, FilesField, FileField, CheckboxGroupField) carry them on their group wrapper. -->
  <component :is="def.component" :id="id" :field="field" :model-value="modelValue" :disabled="isDisabled"
    v-bind="draftBindings"
    @update:model-value="(v: unknown) => emit('update:modelValue', v)" />
</template>
