<script setup lang="ts">
import { ref, computed, watch, useId } from 'vue'
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

// One RichTextInput per field, and an item form can carry several -- these ids must not collide
// across two dialogs mounted at once, the same reasoning RichTextTableGrid already applies.
const rowsId = useId()
const colsId = useId()
const headerId = useId()
const errorId = useId()

function inBounds(n: number): boolean {
  return Number.isInteger(n) && n >= min && n <= max
}
const rowsValid = computed(() => inBounds(rows.value))
const colsValid = computed(() => inBounds(cols.value))
const valid = computed(() => rowsValid.value && colsValid.value)

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
    <DialogContent class="sm:max-w-xs">
      <DialogHeader>
        <DialogTitle>{{ t('fields.richtext.customSizeTitle') }}</DialogTitle>
      </DialogHeader>
      <div class="flex flex-col gap-3">
        <div class="flex flex-col gap-1.5">
          <Label :for="rowsId">{{ t('fields.richtext.rows') }}</Label>
          <Input :id="rowsId" v-model.number="rows" type="number" :min="min" :max="max"
            :aria-invalid="!rowsValid" :aria-describedby="!valid ? errorId : undefined" data-testid="rows" />
        </div>
        <div class="flex flex-col gap-1.5">
          <Label :for="colsId">{{ t('fields.richtext.columns') }}</Label>
          <Input :id="colsId" v-model.number="cols" type="number" :min="min" :max="max"
            :aria-invalid="!colsValid" :aria-describedby="!valid ? errorId : undefined" data-testid="cols" />
        </div>
        <div class="flex items-center gap-2">
          <Checkbox :id="headerId" v-model="withHeaderRow" />
          <Label :for="headerId">{{ t('fields.richtext.withHeaderRow') }}</Label>
        </div>
        <p v-if="!valid" :id="errorId" class="text-sm text-destructive" role="alert">
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
