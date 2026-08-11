<script setup lang="ts" generic="T">
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import { ArrowDown, ArrowUp } from '@lucide/vue'

const props = defineProps<{
  modelValue: T[]
  itemKey: (item: T, index: number) => string
  disabled?: boolean
}>()
const emit = defineEmits<{ (e: 'update:modelValue', v: T[]): void }>()
defineSlots<{ item(props: { item: T; index: number }): unknown }>()
const { t } = useI18n()

// Always emit a fresh array rather than splicing the caller's: this repo's immutability
// convention, and the parent must receive a distinct array so no consumer can observe the old
// and the new order as the same object.
//
// Do NOT justify this with a dirty-check claim: snapshotModel returns a STRING and isDirty
// re-serialises the live model, so an in-place swap would still be detected. Save is gated on
// canWrite, never on dirtiness.
function move(from: number, to: number): void {
  if (to < 0 || to >= props.modelValue.length) return
  const next = [...props.modelValue]
  const [item] = next.splice(from, 1)
  next.splice(to, 0, item)
  emit('update:modelValue', next)
}
</script>

<template>
  <ul class="flex w-full flex-col gap-2">
    <li v-for="(item, i) in modelValue" :key="itemKey(item, i)" class="flex w-full items-center gap-2">
      <div class="min-w-0 flex-1">
        <slot name="item" :item="item" :index="i" />
      </div>
      <!--
        type="button" is load-bearing: this list is dispatched inside ItemForm.vue's
        <form @submit.prevent>, and a native <button> defaults to type="submit" -- an untyped
        control here would save the whole record on every reorder click instead of just
        reordering this field's local array.
      -->
      <Button
        type="button"
        data-testid="move-up"
        variant="ghost"
        size="icon"
        :disabled="disabled || i === 0"
        :aria-label="t('fields.moveUp')"
        @click="move(i, i - 1)"
      >
        <ArrowUp class="size-4" />
      </Button>
      <Button
        type="button"
        data-testid="move-down"
        variant="ghost"
        size="icon"
        :disabled="disabled || i === modelValue.length - 1"
        :aria-label="t('fields.moveDown')"
        @click="move(i, i + 1)"
      >
        <ArrowDown class="size-4" />
      </Button>
    </li>
  </ul>
</template>
