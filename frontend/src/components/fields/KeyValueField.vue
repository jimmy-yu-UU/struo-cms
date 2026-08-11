<script setup lang="ts">
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'
import { Plus, X } from '@lucide/vue'
import type { FieldMeta } from '../../types/schema'

type Row = { key: string; value: string }

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: Record<string, string>): void }>()
const { t } = useI18n()

function toRows(v: unknown): Row[] {
  if (v && typeof v === 'object' && !Array.isArray(v)) {
    return Object.entries(v as Record<string, unknown>).map(([key, value]) => ({ key, value: String(value ?? '') }))
  }
  return []
}

const rows = ref<Row[]>(toRows(props.modelValue))

// Re-derive rows only when the incoming model differs from what our rows serialize to,
// so typing (which emits) doesn't clobber the working array (incl. blank rows).
watch(() => props.modelValue, (v) => {
  if (JSON.stringify(serialize(rows.value)) !== JSON.stringify(v ?? {})) {
    rows.value = toRows(v)
  }
})

function serialize(list: Row[]): Record<string, string> {
  const out: Record<string, string> = {}
  for (const r of list) {
    const k = r.key.trim()
    if (k === '') continue // drop blank keys; last-wins on duplicates
    out[k] = r.value
  }
  return out
}

function commit(next: Row[]) {
  rows.value = next
  emit('update:modelValue', serialize(next))
}
function add() { commit([...rows.value, { key: '', value: '' }]) }
function remove(i: number) { commit(rows.value.filter((_, idx) => idx !== i)) }
function setKey(i: number, key: string) {
  commit(rows.value.map((r, idx) => (idx === i ? { ...r, key } : r)))
}
function setValue(i: number, value: string) {
  commit(rows.value.map((r, idx) => (idx === i ? { ...r, value } : r)))
}
</script>
<template>
  <div class="key-value-field">
    <div v-for="(r, i) in rows" :key="i" class="kv-row">
      <Input
        :model-value="r.key"
        :disabled="disabled"
        placeholder="key"
        @update:model-value="(v) => setKey(i, String(v ?? ''))"
      />
      <Input
        :model-value="r.value"
        :disabled="disabled"
        placeholder="value"
        @update:model-value="(v) => setValue(i, String(v ?? ''))"
      />
      <Button
        class="kv-remove"
        variant="ghost"
        size="icon"
        :disabled="disabled"
        :aria-label="t('common.delete')"
        @click="remove(i)"
      >
        <X class="size-4" />
      </Button>
    </div>
    <Button class="kv-add" variant="ghost" size="sm" :disabled="disabled" @click="add">
      <Plus class="size-4" />
      {{ t('fields.add') }}
    </Button>
  </div>
</template>
