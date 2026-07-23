<script setup lang="ts">
import { ref, watch } from 'vue'
import Button from 'primevue/button'
import { getFieldType } from '../../lib/fieldTypes/registry'
import type { FieldMeta } from '../../types/schema'

defineOptions({ name: 'RepeaterField' })

type Row = Record<string, unknown>

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: Row[]): void }>()

function toRows(v: unknown): Row[] {
  return Array.isArray(v) ? (v as Row[]).map((r) => ({ ...(r ?? {}) })) : []
}

const rows = ref<Row[]>(toRows(props.modelValue))

// Re-derive rows only when the incoming model differs from our working array, so our own
// emits (edit/add/remove/move) don't clobber in-progress edits (incl. blank rows).
watch(
  () => props.modelValue,
  (v) => {
    if (JSON.stringify(v ?? []) !== JSON.stringify(rows.value)) rows.value = toRows(v)
  },
)

function subFields(): FieldMeta[] {
  return props.field.fields ?? []
}

function emptyRow(): Row {
  const r: Row = {}
  for (const f of subFields()) r[f.name] = getFieldType(f.interface).defaultValue(f)
  return r
}

function commit(next: Row[]): void {
  rows.value = next
  emit('update:modelValue', next)
}
function add(): void { commit([...rows.value, emptyRow()]) }
function removeAt(i: number): void { commit(rows.value.filter((_, idx) => idx !== i)) }
function moveUp(i: number): void {
  if (i <= 0) return
  const next = [...rows.value]
  ;[next[i - 1], next[i]] = [next[i], next[i - 1]]
  commit(next)
}
function moveDown(i: number): void {
  if (i >= rows.value.length - 1) return
  const next = [...rows.value]
  ;[next[i + 1], next[i]] = [next[i], next[i + 1]]
  commit(next)
}
function setSub(i: number, name: string, v: unknown): void {
  commit(rows.value.map((r, idx) => (idx === i ? { ...r, [name]: v } : r)))
}
</script>

<template>
  <div class="repeater-field">
    <div v-for="(row, i) in rows" :key="i" class="repeater-row">
      <div class="repeater-row__fields">
        <div v-for="sub in subFields()" :key="sub.name" class="repeater-subfield">
          <label class="repeater-subfield__label">{{ sub.label }}</label>
          <component
            :is="getFieldType(sub.interface).component"
            :field="sub"
            :model-value="row[sub.name]"
            :disabled="disabled || sub.readOnly"
            @update:model-value="(v: unknown) => setSub(i, sub.name, v)"
          />
        </div>
      </div>
      <div class="repeater-row__controls">
        <Button class="repeater-up" icon="pi pi-arrow-up" text :disabled="disabled || i === 0" @click="moveUp(i)" />
        <Button class="repeater-down" icon="pi pi-arrow-down" text
          :disabled="disabled || i === rows.length - 1" @click="moveDown(i)" />
        <Button class="repeater-remove" icon="pi pi-times" text :disabled="disabled" @click="removeAt(i)" />
      </div>
    </div>
    <p v-if="!rows.length" class="repeater-field__empty">No items</p>
    <Button class="repeater-add" icon="pi pi-plus" label="Add" size="small" :disabled="disabled" @click="add" />
  </div>
</template>

<style scoped>
.repeater-field { display: flex; flex-direction: column; gap: 12px; align-items: flex-start; }
.repeater-row {
  display: flex; gap: 12px; width: 100%;
  border: 1px solid var(--border); border-radius: 6px; padding: 12px;
}
.repeater-row__fields { display: flex; flex-direction: column; gap: 8px; flex: 1; }
.repeater-subfield { display: flex; flex-direction: column; gap: 4px; }
.repeater-subfield__label { font-size: 0.85em; opacity: 0.8; }
.repeater-row__controls { display: flex; flex-direction: column; gap: 4px; }
.repeater-field__empty { font-style: italic; opacity: 0.7; }
</style>
