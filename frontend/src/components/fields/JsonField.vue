<script setup lang="ts">
import { computed, ref, useId, useAttrs, watch } from 'vue'
import { Textarea } from '@/components/ui/textarea'
import type { FieldMeta } from '../../types/schema'
import type { FieldDraft } from '../../lib/fieldDrafts'

// `draft` is the form's record of text this field could not parse: it seeds the buffer when the field
// is mounted again and, once withdrawn by the form, drops the unparsable buffer.
const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean; draft?: FieldDraft | null }>()
const emit = defineEmits<{
  (e: 'update:modelValue', v: unknown): void
  (e: 'update:draft', v: FieldDraft | null): void
}>()
// FieldInput dispatches id, aria-invalid and aria-describedby as plain attributes onto whatever this
// component renders as its root. Disabling automatic fallthrough and re-binding `$attrs` by hand
// keeps them landing on the real control that ItemForm's <FieldLabel :for> pairs with.
defineOptions({ inheritAttrs: false })
const attrs = useAttrs()
const parseErrorId = `${useId()}-json-error`

// The textarea is invalid for a JSON parse failure or for an error ItemForm reports on the field.
const formReportsError = computed(() => attrs['aria-invalid'] === 'true' || attrs['aria-invalid'] === true)
const invalid = computed(() => error.value !== null || formReportsError.value)
// The form's error text on this field already says the text does not parse, so this field adds its own
// only when the form has none.
const showsOwnError = computed(() => error.value !== null && !formReportsError.value)
const describedBy = computed(() => {
  const given = attrs['aria-describedby']
  const ids = [typeof given === 'string' ? given : '', showsOwnError.value ? parseErrorId : '']
  const joined = ids.filter((id) => id !== '').join(' ')
  return joined === '' ? undefined : joined
})

function pretty(v: unknown): string {
  return v === null || v === undefined ? '' : JSON.stringify(v, null, 2)
}

const text = ref(props.draft?.text ?? pretty(props.modelValue))
const error = ref<string | null>(props.draft?.error ?? null)

// Re-sync the buffer when the model changes from outside (e.g. locale switch / form reset),
// but not from our own emits (guarded by comparing parsed equality is overkill — only reset
// when the incoming value differs from what our buffer currently parses to).
watch(() => props.modelValue, (v) => {
  try {
    const current = text.value.trim() === '' ? null : JSON.parse(text.value)
    if (JSON.stringify(current) !== JSON.stringify(v)) {
      resetBuffer(v)
    }
  } catch {
    resetBuffer(v)
  }
})

function resetBuffer(v: unknown) {
  text.value = pretty(v)
  error.value = null
  emit('update:draft', null)
}

watch(() => props.draft, (d) => {
  if (!d && error.value !== null) resetBuffer(props.modelValue)
})

function onInput(v: string) {
  text.value = v
  if (v.trim() === '') {
    error.value = null
    emit('update:draft', null)
    emit('update:modelValue', null)
    return
  }
  try {
    const parsed = JSON.parse(v)
    error.value = null
    emit('update:draft', null)
    emit('update:modelValue', parsed)
  } catch (e) {
    error.value = (e as Error).message
    emit('update:draft', { text: v, error: error.value })
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
    <small v-if="showsOwnError" :id="parseErrorId" class="json-error text-destructive text-xs" role="alert">{{ error }}</small>
  </div>
</template>
