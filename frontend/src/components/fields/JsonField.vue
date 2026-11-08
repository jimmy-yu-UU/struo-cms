<script setup lang="ts">
import { computed, ref, useId, useAttrs, watch } from 'vue'
import { Textarea } from '@/components/ui/textarea'
import type { FieldMeta } from '../../types/schema'

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
// FieldInput dispatches id, aria-invalid and aria-describedby as plain attributes onto whatever this
// component renders as its root. Disabling automatic fallthrough and re-binding `$attrs` by hand
// keeps them landing on the real control that ItemForm's <FieldLabel :for> pairs with.
defineOptions({ inheritAttrs: false })
const attrs = useAttrs()
const parseErrorId = `${useId()}-json-error`

// The textarea is invalid for a JSON parse failure or for an error ItemForm reports on the field.
const invalid = computed(() => error.value !== null || String(attrs['aria-invalid']) === 'true')
const describedBy = computed(() => {
  const ids = [String(attrs['aria-describedby'] ?? ''), error.value !== null ? parseErrorId : '']
  const joined = ids.filter((id) => id !== '').join(' ')
  return joined === '' ? undefined : joined
})

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
    <Textarea
      v-bind="$attrs"
      :model-value="text"
      :disabled="disabled"
      :aria-invalid="invalid"
      :aria-describedby="describedBy"
      :aria-label="field.label"
      class="json-textarea font-mono min-h-36"
      spellcheck="false"
      @update:model-value="(v) => onInput(String(v ?? ''))"
    />
    <small v-if="error" :id="parseErrorId" class="json-error text-destructive text-xs" role="alert">{{ error }}</small>
  </div>
</template>
