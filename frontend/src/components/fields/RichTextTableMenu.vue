<script setup lang="ts">
import { ref, type ComponentPublicInstance } from 'vue'
import { useI18n } from 'vue-i18n'
import { Table } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import RichTextTableGrid from './RichTextTableGrid.vue'

defineOptions({ name: 'RichTextTableMenu' })

defineProps<{ disabled?: boolean }>()
const emit = defineEmits<{
  (e: 'insert', size: { rows: number; cols: number; withHeaderRow: boolean }): void
  // Carries this popover's own trigger button so RichTextInput can restore focus to it when the
  // dialog this opens is cancelled -- see onCustomSize below.
  (e: 'customSize', restoreFocusTo: HTMLElement | null): void
}>()

const { t } = useI18n()
const open = ref(false)
const triggerRef = ref<ComponentPublicInstance | null>(null)

// The grid path is opinionated: a CMS table almost always wants a header row, and this keeps the
// pre-RT-2 behaviour. The dialog path is where that becomes a choice.
function onPick(size: { rows: number; cols: number }): void {
  emit('insert', { ...size, withHeaderRow: true })
  open.value = false
}

// Set only for the custom-size path below, checked and cleared by onPopoverCloseAutoFocus --
// never for onPick's own close, where restoring focus to this popover's own trigger button is
// exactly the correct, wanted behaviour.
let suppressCloseAutoFocus = false

// Hands the trigger button over with the event: suppressCloseAutoFocus above means this popover
// will not put focus back on it, and the "Custom size..." button that holds focus now unmounts
// with the popover -- so without this the dialog's cancel has nothing to restore to.
function onCustomSize(): void {
  suppressCloseAutoFocus = true
  const el: unknown = triggerRef.value?.$el
  emit('customSize', el instanceof HTMLElement ? el : null)
  open.value = false
}

// Unless prevented, reka's popover refocuses its own trigger on close. On the custom-size path
// that lands after RichTextInput has already blurred focus and opened the size dialog, putting
// focus back inside the dialog's aria-hidden background.
function onPopoverCloseAutoFocus(event: Event): void {
  if (!suppressCloseAutoFocus) return
  suppressCloseAutoFocus = false
  event.preventDefault()
}
</script>

<template>
  <Popover v-model:open="open">
    <PopoverTrigger as-child>
      <!-- type="button" is explicit even though PopoverTrigger (as-child) merges its own
           type="button" onto what it wraps: this sits inside ItemForm.vue's <form>, so the Button
           does not rely on the merge behaviour of the component wrapping it. -->
      <Button ref="triggerRef" type="button" variant="ghost" size="icon" data-cmd="table" :disabled="disabled"
        :aria-label="t('fields.richtext.table')" :title="t('fields.richtext.table')">
        <Table />
      </Button>
    </PopoverTrigger>
    <PopoverContent class="flex w-auto flex-col gap-1 p-2" @close-auto-focus="onPopoverCloseAutoFocus">
      <RichTextTableGrid @pick="onPick" />
      <button type="button" data-cmd="tableCustomSize"
        class="rounded px-2.5 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
        @click="onCustomSize">{{ t('fields.richtext.customSize') }}</button>
    </PopoverContent>
  </Popover>
</template>
