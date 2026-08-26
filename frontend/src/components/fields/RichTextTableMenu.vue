<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Table } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import RichTextTableGrid from './RichTextTableGrid.vue'

defineOptions({ name: 'RichTextTableMenu' })

defineProps<{ disabled?: boolean }>()
const emit = defineEmits<{
  (e: 'insert', size: { rows: number; cols: number; withHeaderRow: boolean }): void
  (e: 'customSize'): void
}>()

const { t } = useI18n()
const open = ref(false)

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

function onCustomSize(): void {
  suppressCloseAutoFocus = true
  emit('customSize')
  open.value = false
}

// Verified this session in the installed reka-ui@2.10.3 source (Popover/PopoverContentNonModal.js
// and Popover/PopoverTrigger.js): this popover's own onCloseAutoFocus handler, unless
// preventDefault()'d here, calls rootContext.triggerElement.value?.focus() -- and
// PopoverTrigger's own onMounted sets that ref unconditionally to its own template ref (no
// document.body guard the way a reka Dialog's own fallback capture has), so it reliably points at
// the `[data-cmd="table"]` button below. Left unguarded on the custom-size path specifically, that
// restore runs after RichTextInput.vue's own openTableSizeDialog has already blurred focus away
// and opened the size dialog -- refocusing this button (a descendant of <main>) while the size
// dialog's own aria-hidden background is still applied, reintroducing the exact warning that
// batch was fixing, on this one path.
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
      <Button type="button" variant="ghost" size="icon" data-cmd="table" :disabled="disabled"
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
