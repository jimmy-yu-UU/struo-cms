<script setup lang="ts">
import { ref, watch, useId } from 'vue'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import { ArrowDown, ArrowUp, Plus, X } from '@lucide/vue'
import { getFieldType } from '../../lib/fieldTypes/registry'
import type { FieldMeta } from '../../types/schema'

defineOptions({ name: 'RepeaterField' })

type Row = Record<string, unknown>

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: Row[]): void }>()
const { t } = useI18n()

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

// Instance- AND row-scoped: this same sub-field schema renders once per row, so an id keyed only
// by the sub-field's name would repeat identically across rows, making every row's <label for>
// resolve to row zero's control. useId() additionally keeps two RepeaterField instances (or a
// repeater nested as another repeater's sub-field) from colliding with each other.
const uid = useId()
function subFieldId(rowIndex: number, name: string): string { return `${uid}-${rowIndex}-${name}` }
</script>

<template>
  <div class="repeater-field flex flex-col items-start gap-3">
    <div v-for="(row, i) in rows" :key="i" class="repeater-row flex w-full gap-3 rounded-md border p-3">
      <div class="repeater-row__fields flex flex-1 flex-col gap-2">
        <div v-for="sub in subFields()" :key="sub.name" class="repeater-subfield flex flex-col gap-1">
          <label class="repeater-subfield__label text-xs text-muted-foreground" :for="subFieldId(i, sub.name)">{{ sub.label }}</label>
          <component
            :is="getFieldType(sub.interface).component"
            :id="subFieldId(i, sub.name)"
            :field="sub"
            :model-value="row[sub.name]"
            :disabled="disabled || sub.readOnly"
            @update:model-value="(v: unknown) => setSub(i, sub.name, v)"
          />
        </div>
      </div>
      <!-- type="button" on every control below is load-bearing: a native <button> defaults to
           type="submit", and this component is dispatched inside ItemForm.vue's
           <form @submit.prevent> — an untyped button here would submit (and, for a
           Revisions-enabled collection, snapshot) the whole record on every row click instead of
           just updating this field's local array. -->
      <div class="repeater-row__controls flex flex-col gap-1">
        <Button
          type="button"
          class="repeater-up"
          variant="ghost"
          size="icon"
          :aria-label="t('fields.moveUp')"
          :disabled="disabled || i === 0"
          @click="moveUp(i)"
        >
          <ArrowUp class="size-4" />
        </Button>
        <Button
          type="button"
          class="repeater-down"
          variant="ghost"
          size="icon"
          :aria-label="t('fields.moveDown')"
          :disabled="disabled || i === rows.length - 1"
          @click="moveDown(i)"
        >
          <ArrowDown class="size-4" />
        </Button>
        <Button
          type="button"
          class="repeater-remove"
          variant="ghost"
          size="icon"
          :aria-label="t('common.delete')"
          :disabled="disabled"
          @click="removeAt(i)"
        >
          <X class="size-4" />
        </Button>
      </div>
    </div>
    <p v-if="!rows.length" class="repeater-field__empty text-sm italic text-muted-foreground">{{ t('fields.noItems') }}</p>
    <!-- Same type="button" reasoning as the row controls above. No aria-label: the button's own
         visible text already gives it an accessible name, so a label repeating that text would be
         pure duplication rather than an improvement. -->
    <Button
      type="button"
      class="repeater-add"
      variant="outline"
      size="sm"
      :disabled="disabled"
      @click="add"
    >
      <Plus class="size-4" />
      {{ t('fields.add') }}
    </Button>
  </div>
</template>
