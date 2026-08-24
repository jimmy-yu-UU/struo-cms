<script setup lang="ts">
import { ref, computed, useId } from 'vue'
import { useI18n } from 'vue-i18n'

defineOptions({ name: 'RichTextTableGrid' })

const emit = defineEmits<{ (e: 'pick', size: { rows: number; cols: number }): void }>()

const { t } = useI18n()

// Word's own picker size. Anything larger goes through the custom-size dialog instead.
const ROWS = 8
const COLS = 10

const rows = Array.from({ length: ROWS }, (_, i) => i + 1)
const cols = Array.from({ length: COLS }, (_, i) => i + 1)

// Two instances mounted at once (unlikely in practice -- this only ever lives inside one
// toolbar's popover -- but cheap to make collision-proof) would otherwise share one id.
const readoutId = useId()

// Null until the first hover or key press: a highlight shown on mount would read as "already
// chosen 1x1", and the readout below would say so out loud to a screen-reader user who has only
// just landed on the grid and made no choice yet.
const cursor = ref<{ row: number; col: number } | null>(null)

// Two separate, independently pluralized phrases composed with the same '{count} column(s)'
// grammar the rest of the app's i18n packs use, rather than one message with two raw numbers
// slotted into a fixed English plural ("1 columns"): vue-i18n's numeric overload of t() selects
// the plural form AND supplies {count}, so passing the raw count is enough.
const readout = computed(() => {
  const c = cursor.value
  if (!c) return ''
  return `${t('fields.richtext.tableSizeCols', c.col)} × ${t('fields.richtext.tableSizeRows', c.row)}`
})

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
    case ' ': confirmPick(); break
    default: return
  }
  e.preventDefault()
}

// Named confirmPick, not confirm: this sits at <script setup> scope, where a bare `confirm` would
// shadow window.confirm -- and this file's neighbours in richtext do call the native dialogs.
function confirmPick(): void {
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
       popover must not insert 80 tab stops into the form's tab order.

       role="grid" genuinely describes this: the interaction is 2D arrow navigation, not a flat
       list, so a "group" would understate it -- but ARIA requires a grid's cells to be owned by
       "row" children, not handed to it directly. Each row is wrapped in a `role="row"` div with
       `class="contents"` (display: contents) so it disappears from the box layout entirely and
       `grid-template-columns` on the container still lays the buttons out directly, unaffected by
       the extra DOM level.

       The accessible name is the STATIC "Table" label, not the live size: a name that mutates on
       every arrow press makes some screen readers re-announce the whole widget's name each time,
       stepping on the aria-live readout below announcing the same words a second time -- and
       neither announcement would actually say what the widget IS. aria-describedby links the two
       instead: the name says what this is, the description (kept live by the paragraph's own
       aria-live="polite", not by this attribute) says its current state. -->
  <div class="flex flex-col gap-1.5">
    <div role="grid" tabindex="0" :aria-label="t('fields.richtext.table')" :aria-describedby="readoutId"
      class="grid gap-0.5 rounded outline-none focus-visible:ring-2 focus-visible:ring-ring"
      :style="{ gridTemplateColumns: `repeat(${cols.length}, 1rem)` }"
      @keydown="onKeydown" @mouseleave="cursor = null">
      <div v-for="r in rows" :key="r" role="row" class="contents">
        <button v-for="c in cols" :key="`${r}-${c}`" type="button" tabindex="-1" role="gridcell"
          :data-cell="`${r}-${c}`" :data-in-range="inRange(r, c)"
          class="size-4 rounded-[2px] border border-border data-[in-range=true]:border-primary data-[in-range=true]:bg-primary/30"
          @mouseenter="cursor = { row: r, col: c }" @click="pick(r, c)" />
      </div>
    </div>
    <p :id="readoutId" data-testid="table-size-readout" aria-live="polite" class="text-center text-xs text-muted-foreground">
      {{ readout }}
    </p>
  </div>
</template>
