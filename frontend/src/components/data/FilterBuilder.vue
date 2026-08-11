<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { Plus, X } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from '@/components/ui/select'
import type { FilterSpec } from '@/lib/buildListQuery'

export type FilterField = {
  name: string
  label: string
  /** Present for enumerated fields; renders a Select instead of a free-text Input. */
  options?: { label: string; value: string }[]
}

/** Only the operators QueryParser.cs actually accepts (src/Struo.Application/Query/QueryParser.cs). */
const OPERATORS = ['_eq', '_neq', '_contains'] as const
type Operator = (typeof OPERATORS)[number]

type DraftRow = { field: string; op: Operator; value: string }

const props = defineProps<{ fields: FilterField[]; applied: FilterSpec }>()
const emit = defineEmits<{ apply: [FilterSpec] }>()

// Draft is deliberately NOT the source of truth for the consumer: an explicit Search press (or
// Enter) must happen before anything reaches the query — editing conditions must not fire
// requests keystroke by keystroke.
const draft = ref<DraftRow[]>([])

function hydrate(spec: FilterSpec): void {
  draft.value = Object.entries(spec).map(([field, { op, value }]) => ({
    field, op: (OPERATORS as readonly string[]).includes(op) ? (op as Operator) : '_eq', value,
  }))
}
hydrate(props.applied)

function specsEqual(a: FilterSpec, b: FilterSpec): boolean {
  const aKeys = Object.keys(a)
  const bKeys = Object.keys(b)
  return aKeys.length === bKeys.length
    && aKeys.every((k) => b[k] !== undefined && b[k].op === a[k].op && b[k].value === a[k].value)
}
// Re-hydrate when the consumer resets the filter externally (e.g. switching collection) —
// but NOT when it merely echoes back the spec this component just emitted (the consumer
// re-assigns `applied` from the emitted spec, or derives it from a route-query `computed` that
// mints a new object per navigation). `deep: true` fires on that re-assignment even though
// nothing changed;
// a wholesale hydrate() there would silently delete any row toSpec() dropped as half-authored
// (an empty value, a field not yet picked). Only rebuild the draft when the incoming spec
// actually differs from what the current draft would itself produce.
watch(() => props.applied, (spec) => {
  if (!specsEqual(spec, toSpec())) hydrate(spec)
}, { deep: true })

const OPERATOR_LABEL: Record<Operator, string> = {
  _eq: 'filterBuilder.opEq',
  _neq: 'filterBuilder.opNeq',
  _contains: 'filterBuilder.opContains',
}

function fieldOptionsFor(index: number): FilterField[] {
  // FilterSpec is keyed by field name, so two rows on one field would collide. Offer only
  // fields no OTHER row has taken.
  const taken = new Set(draft.value.filter((_, i) => i !== index).map((r) => r.field))
  return props.fields.filter((f) => !taken.has(f.name))
}

function optionsFor(index: number): { label: string; value: string }[] | undefined {
  return props.fields.find((f) => f.name === draft.value[index]?.field)?.options
}

function addRow(): void {
  const free = fieldOptionsFor(draft.value.length)[0]
  draft.value = [...draft.value, { field: free?.name ?? '', op: '_eq', value: '' }]
}
function removeRow(index: number): void {
  draft.value = draft.value.filter((_, i) => i !== index)
}
function setDraftField(index: number, field: string): void {
  // Changing the field invalidates the value: an enum value has no meaning on a text field.
  draft.value = draft.value.map((r, i) => (i === index ? { ...r, field, value: '' } : r))
}
function setDraftOperator(index: number, op: Operator): void {
  draft.value = draft.value.map((r, i) => (i === index ? { ...r, op } : r))
}
function setDraftValue(index: number, value: string): void {
  draft.value = draft.value.map((r, i) => (i === index ? { ...r, value } : r))
}

function toSpec(): FilterSpec {
  const spec: FilterSpec = {}
  for (const row of draft.value) {
    // A row with no field or an empty (whitespace-only) value is half-authored; sending it
    // would filter on nothing and silently return zero results. Send the trimmed value too —
    // leading/trailing whitespace typed into the value field is never meaningful to the
    // backend's `_eq`/`_neq`/`_contains` comparison.
    const value = row.value.trim()
    if (!row.field || value === '') continue
    spec[row.field] = { op: row.op, value }
  }
  return spec
}

function apply(): void { emit('apply', toSpec()) }
function clear(): void { draft.value = []; emit('apply', {}) }

// NOT `draft.value.length < props.fields.length`: that undercounts the moment a hydrated row
// names a field absent from `props.fields` (e.g. a field removed from the schema after the
// filter was applied) — draft can reach `fields.length` while a real field is still free. Ask
// the same predicate the field picker itself uses.
const canAdd = computed(() => fieldOptionsFor(draft.value.length).length > 0)

// Exposed for the unit tests: the reka-ui Select does not expose a DOM-driven way to pick an
// option under jsdom, so the tests drive the draft through these instead of faking clicks.
defineExpose({ setDraftField, setDraftOperator, setDraftValue, fieldOptionsFor })
</script>

<template>
  <div class="grid gap-3 pb-4">
    <!-- Condition rows -->
    <div
      v-for="(row, index) in draft"
      :key="index"
      data-testid="filter-row"
      class="flex flex-wrap items-center gap-2"
    >
      <Select :model-value="row.field" @update:model-value="setDraftField(index, String($event))">
        <SelectTrigger class="w-44" :aria-label="$t('filterBuilder.field')">
          <SelectValue :placeholder="$t('filterBuilder.field')" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="f in fieldOptionsFor(index)" :key="f.name" :value="f.name">
            {{ f.label }}
          </SelectItem>
        </SelectContent>
      </Select>

      <Select :model-value="row.op" @update:model-value="setDraftOperator(index, $event as never)">
        <SelectTrigger class="w-36" :aria-label="$t('filterBuilder.operator')">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="op in OPERATORS" :key="op" :value="op">
            {{ $t(OPERATOR_LABEL[op]) }}
          </SelectItem>
        </SelectContent>
      </Select>

      <Select
        v-if="optionsFor(index)"
        :model-value="row.value"
        @update:model-value="setDraftValue(index, String($event))"
      >
        <SelectTrigger class="w-48" :aria-label="$t('filterBuilder.value')">
          <SelectValue :placeholder="$t('filterBuilder.value')" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="o in optionsFor(index)" :key="o.value" :value="o.value">
            {{ o.label }}
          </SelectItem>
        </SelectContent>
      </Select>
      <Input
        v-else
        class="w-48"
        :model-value="row.value"
        :aria-label="$t('filterBuilder.value')"
        @update:model-value="setDraftValue(index, String($event))"
        @keyup.enter="apply"
      />

      <Button
        variant="ghost" size="icon" data-testid="filter-remove"
        :aria-label="$t('filterBuilder.remove')" @click="removeRow(index)"
      >
        <X class="size-4" aria-hidden="true" />
      </Button>
    </div>

    <!-- Action row, visually separated from the condition rows -->
    <div class="flex flex-wrap items-center gap-2">
      <Button variant="outline" data-testid="filter-apply" @click="apply">
        {{ $t('filterBuilder.apply') }}
      </Button>
      <Button variant="ghost" data-testid="filter-add" :disabled="!canAdd" @click="addRow">
        <Plus class="size-4" aria-hidden="true" />
        {{ $t('filterBuilder.addCondition') }}
      </Button>
      <Button variant="ghost" data-testid="filter-clear" @click="clear">
        {{ $t('filterBuilder.clear') }}
      </Button>
    </div>
  </div>
</template>
