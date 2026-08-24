<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { TABLE_SIZE_MIN, TABLE_SIZE_MAX } from './richTextTableActions'

defineOptions({ name: 'RichTextTableSizeDialog' })

const props = defineProps<{ open: boolean }>()
const emit = defineEmits<{
  (e: 'update:open', open: boolean): void
  (e: 'insert', size: { rows: number; cols: number; withHeaderRow: boolean }): void
}>()

const { t } = useI18n()

const rows = ref(3)
const cols = ref(3)
const withHeaderRow = ref(true)

const min = TABLE_SIZE_MIN
const max = TABLE_SIZE_MAX

function inBounds(n: number): boolean {
  return Number.isInteger(n) && n >= min && n <= max
}
const valid = computed(() => inBounds(rows.value) && inBounds(cols.value))

// Reopening the dialog starts from the default again rather than from whatever the last rejected
// attempt left behind.
watch(() => props.open, (isOpen) => {
  if (!isOpen) return
  rows.value = 3
  cols.value = 3
  withHeaderRow.value = true
})

// The guard lives here, not only on the button's `disabled`: a caller (or a test) that invokes
// submit directly must get the same refusal.
function submit(): void {
  if (!valid.value) return
  emit('insert', { rows: rows.value, cols: cols.value, withHeaderRow: withHeaderRow.value })
  emit('update:open', false)
}

defineExpose({ rows, cols, withHeaderRow, valid, submit })
</script>

<template>
  <Dialog :open="props.open" @update:open="emit('update:open', $event)">
    <DialogContent class="max-w-xs">
      <DialogHeader>
        <DialogTitle>{{ t('fields.richtext.customSizeTitle') }}</DialogTitle>
      </DialogHeader>
      <div class="flex flex-col gap-3">
        <div class="flex flex-col gap-1.5">
          <Label for="rt-table-rows">{{ t('fields.richtext.rows') }}</Label>
          <Input id="rt-table-rows" v-model.number="rows" type="number" :min="min" :max="max" data-testid="rows" />
        </div>
        <div class="flex flex-col gap-1.5">
          <Label for="rt-table-cols">{{ t('fields.richtext.columns') }}</Label>
          <Input id="rt-table-cols" v-model.number="cols" type="number" :min="min" :max="max" data-testid="cols" />
        </div>
        <div class="flex items-center gap-2">
          <Checkbox id="rt-table-header" v-model="withHeaderRow" />
          <Label for="rt-table-header">{{ t('fields.richtext.withHeaderRow') }}</Label>
        </div>
        <p v-if="!valid" class="text-sm text-destructive" role="alert">
          {{ t('fields.richtext.sizeOutOfRange', { min, max }) }}
        </p>
      </div>
      <DialogFooter>
        <Button type="button" variant="ghost" @click="emit('update:open', false)">{{ t('common.cancel') }}</Button>
        <Button type="button" data-cmd="tableSizeConfirm" :disabled="!valid" @click="submit">
          {{ t('common.confirm') }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
