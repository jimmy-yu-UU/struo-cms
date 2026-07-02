<script setup lang="ts">
import { computed } from 'vue'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import InputNumber from 'primevue/inputnumber'
import Checkbox from 'primevue/checkbox'
import DatePicker from 'primevue/datepicker'
import Select from 'primevue/select'
import RadioButton from 'primevue/radiobutton'
import { fieldInputKind } from '../../lib/fieldInputKind'
import type { FieldMeta } from '../../types/schema'

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()

const kind = computed(() => fieldInputKind(props.field.interface))
const isDisabled = computed(() => props.disabled === true || props.field.readOnly)
function update(v: unknown): void { emit('update:modelValue', v) }
</script>

<template>
  <InputText v-if="kind === 'text'" :model-value="(modelValue as string)" :disabled="isDisabled"
    @update:model-value="update" />

  <Textarea v-else-if="kind === 'textarea' || kind === 'richtext'" :model-value="(modelValue as string)"
    :disabled="isDisabled" :rows="6" @update:model-value="update" />

  <InputNumber v-else-if="kind === 'number'" :model-value="(modelValue as number)" :disabled="isDisabled"
    @update:model-value="update" />

  <Checkbox v-else-if="kind === 'boolean'" :model-value="(modelValue as boolean)" :binary="true"
    :disabled="isDisabled" @update:model-value="update" />

  <DatePicker v-else-if="kind === 'date'" :model-value="(modelValue as Date)" :disabled="isDisabled"
    @update:model-value="update" />
  <DatePicker v-else-if="kind === 'time'" :model-value="(modelValue as Date)" time-only :disabled="isDisabled"
    @update:model-value="update" />
  <DatePicker v-else-if="kind === 'datetime'" :model-value="(modelValue as Date)" show-time :disabled="isDisabled"
    @update:model-value="update" />

  <Select v-else-if="kind === 'select'" :model-value="modelValue" :options="field.options ?? []"
    option-label="label" option-value="value" :disabled="isDisabled" @update:model-value="update" />

  <div v-else-if="kind === 'radio'" class="radio-group">
    <label v-for="opt in field.options ?? []" :key="opt.value" class="radio-option">
      <RadioButton :model-value="modelValue" :value="opt.value" :disabled="isDisabled"
        @update:model-value="update" />
      <span>{{ opt.label }}</span>
    </label>
  </div>

  <hr v-else-if="kind === 'divider'" />

  <span v-else class="readonly-field">{{ modelValue ?? '—' }}</span>
</template>
