<script setup lang="ts">
import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'

defineOptions({ name: 'RichTextTableGrid' })

const emit = defineEmits<{ (e: 'pick', size: { rows: number; cols: number }): void }>()

const { t } = useI18n()

// Word's own picker size. Anything larger goes through the custom-size dialog instead.
const ROWS = 8
const COLS = 10

const rows = Array.from({ length: ROWS }, (_, i) => i + 1)
const cols = Array.from({ length: COLS }, (_, i) => i + 1)

// Null until the first hover or key press: a highlight shown on mount would read as "already
// chosen 1x1".
const cursor = ref<{ row: number; col: number } | null>(null)
const readout = computed(() =>
  t('fields.richtext.tableSize', { cols: cursor.value?.col ?? 1, rows: cursor.value?.row ?? 1 }))

function inRange(row: number, col: number): boolean {
  const c = cursor.value
  return !!c && row <= c.row && col <= c.col
}

function move(dRow: number, dCol: number): void {
  const c = cursor.value ?? { row: 1, col: 1 }
  cursor.value = {
    row: Math.min(ROWS, Math.max(1, c.row + dRow)),
    col: Math.min(COLS, Math.max(1, c.col + dCol)),
  }
}

function onKeydown(e: KeyboardEvent): void {
  switch (e.key) {
    case 'ArrowRight': move(0, 1); break
    case 'ArrowLeft': move(0, -1); break
    case 'ArrowDown': move(1, 0); break
    case 'ArrowUp': move(-1, 0); break
    case 'Enter':
    case ' ': confirm(); break
    default: return
  }
  e.preventDefault()
}

function confirm(): void {
  const c = cursor.value ?? { row: 1, col: 1 }
  emit('pick', { rows: c.row, cols: c.col })
}

function pick(row: number, col: number): void {
  cursor.value = { row, col }
  emit('pick', { rows: row, cols: col })
}
</script>

<template>
  <!-- One tab stop for the whole widget with a roving cursor, not 80 focusable cells: a toolbar
       popover must not insert 80 tab stops into the form's tab order. -->
  <div class="flex flex-col gap-1.5">
    <div role="grid" tabindex="0" :aria-label="readout"
      class="grid gap-0.5 rounded outline-none focus-visible:ring-2 focus-visible:ring-ring"
      :style="{ gridTemplateColumns: `repeat(${cols.length}, 1rem)` }"
      @keydown="onKeydown" @mouseleave="cursor = null">
      <button v-for="cell in rows.flatMap((r) => cols.map((c) => ({ r, c })))"
        :key="`${cell.r}-${cell.c}`" type="button" tabindex="-1"
        :data-cell="`${cell.r}-${cell.c}`" :data-in-range="inRange(cell.r, cell.c)"
        class="size-4 rounded-[2px] border border-border data-[in-range=true]:border-primary data-[in-range=true]:bg-primary/30"
        @mouseenter="cursor = { row: cell.r, col: cell.c }" @click="pick(cell.r, cell.c)" />
    </div>
    <p data-testid="table-size-readout" aria-live="polite" class="text-center text-xs text-muted-foreground">
      {{ readout }}
    </p>
  </div>
</template>
