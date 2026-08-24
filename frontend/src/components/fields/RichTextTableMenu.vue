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

function onCustomSize(): void {
  emit('customSize')
  open.value = false
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
    <PopoverContent class="flex w-auto flex-col gap-1 p-2">
      <RichTextTableGrid @pick="onPick" />
      <button type="button" data-cmd="tableCustomSize"
        class="rounded px-2.5 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
        @click="onCustomSize">{{ t('fields.richtext.customSize') }}</button>
    </PopoverContent>
  </Popover>
</template>
