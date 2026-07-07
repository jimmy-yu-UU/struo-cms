<script setup lang="ts">
import { computed } from 'vue'
import InputText from 'primevue/inputtext'
import Button from 'primevue/button'
import type { FieldMeta, TagItem } from '../../types/schema'

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: TagItem[]): void }>()

const items = computed<TagItem[]>(() => (Array.isArray(props.modelValue) ? (props.modelValue as TagItem[]) : []))

function commit(next: TagItem[]) { emit('update:modelValue', next) }
function add() { commit([...items.value, { value: '' }]) }
function remove(i: number) { commit(items.value.filter((_, idx) => idx !== i)) }
function setValue(i: number, v: string) {
  commit(items.value.map((it, idx) => (idx === i ? { ...it, value: v } : it)))
}
function setLabel(i: number, v: string) {
  // Drop the label key entirely when cleared, so an empty label never round-trips as "".
  commit(items.value.map((it, idx) => {
    if (idx !== i) return it
    if (v) return { ...it, value: it.value, label: v }
    const { label: _drop, ...rest } = it
    return rest
  }))
}
</script>
<template>
  <div class="tags-field">
    <div v-for="(it, i) in items" :key="i" class="tag-row">
      <InputText :model-value="it.value" :disabled="disabled" placeholder="value"
        @update:model-value="(v: string | undefined) => setValue(i, v ?? '')" />
      <InputText :model-value="it.label ?? ''" :disabled="disabled" placeholder="display text (optional)"
        @update:model-value="(v: string | undefined) => setLabel(i, v ?? '')" />
      <Button class="tag-remove" icon="pi pi-times" text :disabled="disabled" @click="remove(i)" />
    </div>
    <Button class="tag-add" icon="pi pi-plus" label="Add" text :disabled="disabled" @click="add" />
  </div>
</template>
