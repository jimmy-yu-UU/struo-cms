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
  <!-- `id` is forwarded, never generated here: a field interface whose rendered component root IS
       its native control (e.g. TextField's Input, BooleanField's Switch) carries this straight
       onto that control, genuinely pairing it with the caller's <label for>. A component whose root
       is a wrapper div (TagsField, DateField, …) just receives it there instead — harmless, but no
       association is created; those already carry their own aria-label/per-option labelling. -->
  <component :is="def.component" :id="id" :field="field" :model-value="modelValue" :disabled="isDisabled"
    @update:model-value="(v: unknown) => emit('update:modelValue', v)" />
</template>
