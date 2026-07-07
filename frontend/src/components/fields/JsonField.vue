<script setup lang="ts">
import { ref, watch } from 'vue'
import Textarea from 'primevue/textarea'
import type { FieldMeta } from '../../types/schema'

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()

function pretty(v: unknown): string {
  return v === null || v === undefined ? '' : JSON.stringify(v, null, 2)
}

const text = ref(pretty(props.modelValue))
const error = ref<string | null>(null)

// Re-sync the buffer when the model changes from outside (e.g. locale switch / form reset),
// but not from our own emits (guarded by comparing parsed equality is overkill — only reset
// when the incoming value differs from what our buffer currently parses to).
watch(() => props.modelValue, (v) => {
  try {
    const current = text.value.trim() === '' ? null : JSON.parse(text.value)
    if (JSON.stringify(current) !== JSON.stringify(v)) {
      text.value = pretty(v)
      error.value = null
    }
  } catch {
    text.value = pretty(v)
    error.value = null
  }
})

function onInput(v: string) {
  text.value = v
  if (v.trim() === '') {
    error.value = null
    emit('update:modelValue', null)
    return
  }
  try {
    const parsed = JSON.parse(v)
    error.value = null
    emit('update:modelValue', parsed)
  } catch (e) {
    error.value = (e as Error).message
    // suppress emit — keep the last valid model value
  }
}
</script>
<template>
  <div class="json-field">
    <Textarea :model-value="text" :disabled="disabled" rows="6" class="json-textarea"
      :invalid="error !== null" spellcheck="false"
      @update:model-value="(v: string) => onInput(v ?? '')" />
    <small v-if="error" class="json-error p-error">{{ error }}</small>
  </div>
</template>
