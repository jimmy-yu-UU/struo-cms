<script setup lang="ts" generic="T">
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import { ArrowDown, ArrowUp } from '@lucide/vue'
import { reorder } from './sortableReorder'

const props = withDefaults(defineProps<{
  modelValue: T[]
  itemKey: (item: T, index: number) => string
  disabled?: boolean
  // false hides the arrows entirely (a relation without a SortField has no order to edit --
  // disabled arrows would wrongly advertise one); the list then only supplies the per-row shell.
  reorderable?: boolean
}>(), { reorderable: true })
const emit = defineEmits<{ (e: 'update:modelValue', v: T[]): void }>()
defineSlots<{ item(props: { item: T; index: number }): unknown }>()
const { t } = useI18n()

// Duplicate itemKey() results across rows would make v-for's :key ambiguous (two <li>s claiming
// the same key). Not guarded here, same as any other v-for :key: it is the caller's
// responsibility to hand back unique keys, and detecting the duplicate here would only mask that
// caller bug rather than fix it. FilesField's file ids, this component's only consumer, are
// unique by construction.
function move(from: number, to: number): void {
  const next = reorder(props.modelValue, from, to)
  // reorder() returns the SAME reference when the move was out of bounds -- that is what tells
  // us nothing changed, so a boundary click (or any other invalid from/to) emits nothing.
  if (next === props.modelValue) return
  emit('update:modelValue', next)
}
</script>

<template>
  <ul class="flex w-full flex-col gap-2">
    <li v-for="(item, i) in modelValue" :key="itemKey(item, i)" class="flex w-full items-center gap-2">
      <div class="min-w-0 flex-1">
        <slot name="item" :item="item" :index="i" />
      </div>
      <template v-if="reorderable">
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
      </template>
    </li>
  </ul>
</template>
