<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Table } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { IN_TABLE_ACTIONS, type TableAction } from './richTextTableActions'

defineOptions({ name: 'RichTextTableMenu' })

defineProps<{ disabled?: boolean; inTable: boolean }>()
const emit = defineEmits<{ (e: 'action', action: TableAction): void }>()

const { t } = useI18n()

const open = ref(false)
const inTableActions = IN_TABLE_ACTIONS

function run(action: TableAction): void {
  emit('action', action)
  open.value = false
}
</script>

<template>
  <Popover v-model:open="open">
    <PopoverTrigger as-child>
      <!-- type="button" is explicit even though PopoverTrigger (as-child) already merges its own
           type="button" onto whatever it wraps: this sits inside ItemForm.vue's <form>, so the
           Button doesn't rely on the merge behaviour of the component wrapping it. -->
      <Button type="button" variant="ghost" size="icon" data-cmd="table" :disabled="disabled" :aria-label="t('fields.richtext.table')" :title="t('fields.richtext.table')">
        <Table />
      </Button>
    </PopoverTrigger>
    <PopoverContent class="flex w-auto min-w-40 flex-col gap-0.5 p-1">
      <button type="button" data-cmd="tableInsert" class="rounded px-2.5 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground" @click="run('insert')">
        Insert 3×3 table
      </button>
      <button v-for="[action, labelKey] in inTableActions" :key="action" type="button"
        :data-cmd="`table-${action}`" class="rounded px-2.5 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground disabled:pointer-events-none disabled:opacity-50" :disabled="!inTable"
        @click="run(action)">{{ t(`fields.richtext.${labelKey}`) }}</button>
    </PopoverContent>
  </Popover>
</template>
