<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'
import { Plus, X } from '@lucide/vue'
import type { FieldMeta, TagItem } from '@/types/schema'

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: TagItem[]): void }>()
const { t } = useI18n()

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
  <div class="flex flex-col items-start gap-2">
    <div v-for="(it, i) in items" :key="i" class="tag-row flex w-full items-center gap-2">
      <Input
        :model-value="it.value"
        :disabled="disabled"
        placeholder="value"
        @update:model-value="(v) => setValue(i, String(v ?? ''))"
      />
      <Input
        :model-value="it.label ?? ''"
        :disabled="disabled"
        placeholder="display text (optional)"
        @update:model-value="(v) => setLabel(i, String(v ?? ''))"
      />
      <Button
        class="tag-remove"
        variant="ghost"
        size="icon"
        :disabled="disabled"
        :aria-label="t('common.delete')"
        @click="remove(i)"
      >
        <X class="size-4" />
      </Button>
    </div>
    <Button class="tag-add" variant="ghost" size="sm" :disabled="disabled" @click="add">
      <Plus class="size-4" />
      {{ t('fields.add') }}
    </Button>
  </div>
</template>
