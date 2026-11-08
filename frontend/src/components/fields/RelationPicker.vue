<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount, useAttrs, type StyleValue } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  Combobox, ComboboxAnchor, ComboboxEmpty, ComboboxInput, ComboboxItem,
  ComboboxItemIndicator, ComboboxList, ComboboxTrigger, ComboboxViewport,
} from '@/components/ui/combobox'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import TreeSelect from '@/components/form/TreeSelect.vue'
import { Check, ChevronsUpDown, Plus, X } from '@lucide/vue'
import { itemsApi } from '../../api/itemsApi'
import { useSchemaStore } from '../../stores/schemaStore'
import { useLanguageStore } from '../../stores/languageStore'
import { resolveDisplayLabel } from '../../lib/resolveDisplayLabel'
import { buildRelationTree, type TreeNode } from '../../lib/buildRelationTree'
import { debounce } from '../../lib/debounce'
import { createLatestWins } from '../../lib/latestWins'
import type { RelationMeta } from '../../types/schema'

const props = withDefaults(defineProps<{
  relation: RelationMeta
  modelValue: unknown
  multiple?: boolean
  tree?: boolean
  disabled?: boolean
  excludeId?: string
  // multiple only. JunctionLinksEditor renders each selection as its own payload row and turns the
  // chips off; every other caller keeps them.
  showChips?: boolean
  // Returns a reason when an option cannot be chosen; the option is then disabled and carries it as a tooltip.
  optionHint?: (raw: Record<string, unknown>) => string | null
  // multiple only. Renders the picker as one outline button reading `+ label` that opens the menu, for a
  // host that draws the box itself, as the junction links editor does; the menu works the same.
  addLabel?: string
}>(), { showChips: true, optionHint: undefined, addLabel: undefined })
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
// RelationInput's id, aria-invalid and aria-describedby are re-bound onto the one trigger that
// renders; class and style stay on the root element.
defineOptions({ inheritAttrs: false })
const attrs = useAttrs()
function controlAttrs(): Record<string, unknown> {
  const { class: _class, style: _style, ...rest } = attrs
  return rest
}

const { t } = useI18n()
const schema = useSchemaStore()
const langStore = useLanguageStore()

type Option = { id: string; label: string; raw: Record<string, unknown> }
const options = ref<Option[]>([])
const labelById = ref<Record<string, string>>({})
const rawById = ref<Record<string, Record<string, unknown>>>({})
// The multi-select menu is controlled so a click on the empty part of the field box can open it.
const menuOpen = ref(false)
const loading = ref(false)
const loadError = ref('')
const search = ref('')

const targetMeta = computed(() => schema.get(props.relation.targetCollection))

function toOption(row: Record<string, unknown>): Option {
  const tm = targetMeta.value
  const label = tm ? resolveDisplayLabel(row, props.relation, tm, langStore.defaultCode) : String(row.id)
  return { id: String(row.id), label, raw: row }
}

const optionsLoad = createLatestWins()

async function loadOptions(): Promise<void> {
  const token = optionsLoad.next()
  loading.value = true
  loadError.value = ''
  try {
    const res = await itemsApi.list(props.relation.targetCollection, {
      page: 0, rows: 25, search: search.value || undefined, locale: langStore.defaultCode || undefined,
    })
    if (!optionsLoad.isCurrent(token)) return
    options.value = res.data.map(toOption)
    for (const o of options.value) {
      labelById.value[o.id] = o.label
      rawById.value[o.id] = o.raw
    }
  } catch (e) {
    if (!optionsLoad.isCurrent(token)) return
    loadError.value = e instanceof Error ? e.message : t('fields.loadOptionsFailed')
  } finally {
    if (optionsLoad.isCurrent(token)) loading.value = false
  }
}

function selectedIds(): string[] {
  if (props.multiple) return ((props.modelValue as string[] | null) ?? []).map(String)
  return props.modelValue != null ? [String(props.modelValue)] : []
}

async function ensureSelectedLabels(): Promise<void> {
  const missing = [...new Set(selectedIds())].filter((id) => !labelById.value[id])
  await Promise.all(missing.map(async (id) => {
    try {
      const row = await itemsApi.get(props.relation.targetCollection, id, { locale: langStore.defaultCode })
      const option = toOption(row)
      labelById.value[id] = option.label
      rawById.value[id] = option.raw
    } catch {
      labelById.value[id] = id // deleted / inaccessible -> show id
    }
  }))
}

// Select/Combobox render labels from their options list only, so a preselected id that is not on
// the current options page would show as a raw id. Merge any such selected-but-absent ids
// (labelled from labelById, back-filled by ensureSelectedLabels) into the rendered options without
// mutating either source.
const displayOptions = computed<Option[]>(() => {
  const selected = selectedIds()
  const present = new Set(options.value.map((o) => o.id))
  const missingIds = [...new Set(selected.filter((id) => !present.has(id)))]
  const missing: Option[] = missingIds.map((id) => ({
    id, label: labelById.value[id] ?? id, raw: rawById.value[id] ?? {},
  }))
  return [...options.value, ...missing]
})

const treeNodes = computed<TreeNode[]>(() => {
  if (!props.tree) return []
  const parentKey = props.relation.foreignKey
    ? props.relation.foreignKey[0].toLowerCase() + props.relation.foreignKey.slice(1)
    : 'parentId'
  return buildRelationTree(
    options.value.map((o) => ({ id: o.id, label: o.label, ...o.raw })),
    parentKey,
    props.excludeId,
  )
})

function onChange(v: unknown): void {
  emit('update:modelValue', v)
}

function hintFor(opt: Option): string | null {
  return props.optionHint ? props.optionHint(opt.raw) : null
}

// A click on the empty part of the field box opens or closes the menu; a click on a chip or its remove
// button is not on the box itself and never gets here.
function onBoxClick(): void {
  if (!props.disabled) menuOpen.value = !menuOpen.value
}

// The field box: bordered and growing with its chips, or, as an add button, just the button's own width.
const multipleBoxClass = computed(() => {
  const cursor = props.disabled ? 'cursor-not-allowed opacity-50' : 'cursor-pointer'
  return props.addLabel
    ? 'inline-flex w-auto'
    : `border-input flex min-h-9 w-full flex-wrap items-center gap-1.5 rounded-md border bg-transparent px-2 py-1 text-sm ${cursor}`
})

function openMenu(): void {
  if (!props.disabled) menuOpen.value = true
}

// form/TreeSelect.vue takes and emits a plain key directly; no `{ [key]: true }` packing.
function onTreeChange(key: string | null): void {
  onChange(key)
}

// The single-select id, or null when nothing is chosen.
const singleSelected = computed(() => (props.modelValue != null ? String(props.modelValue) : null))
// The multi-select ids, coerced to string[] regardless of what shape the model actually carries.
const multipleSelected = computed(() => ((props.modelValue as string[] | null) ?? []).map(String))

// reka's ComboboxRoot defaults resetSearchTermOnSelect/resetSearchTermOnBlur to true, and
// ComboboxInput seeds its own value from the *root's* modelValue the instant it mounts (its
// Presence-gated content exists only while the popup is open) — with no displayValue override, a
// scalar single-select id gets stringified straight into this search box. Because that box is
// bound to this component's own `search` ref, the id would flow back out through
// @update:model-value and poison `search`, firing a bogus server-side query that filters the
// option list down to just the row already selected. The multi-select branch never hits this:
// resetSearchTerm() takes reka's own `multiple` path there and always resets to '' regardless of
// what the model holds.
const emptySearchDisplay = (): string => ''

const singleSelectedOption = computed(() => (
  singleSelected.value == null ? null : displayOptions.value.find((o) => o.id === singleSelected.value) ?? null
))
const multipleSelectedOptions = computed(() => (
  multipleSelected.value.map((id) => displayOptions.value.find((o) => o.id === id) ?? { id, label: id, raw: rawById.value[id] ?? {} })
))

// aria-label overrides the trigger's visible contents entirely, so it has to carry both the
// relation's own label AND the current value/count, matching the pattern MultiSelectField.vue and
// DatePicker.vue settled on — a static label alone would make every pick announce identically.
const singleAccessibleName = computed(() => (
  singleSelectedOption.value
    ? `${props.relation.label}${t('fields.namePairSeparator')}${singleSelectedOption.value.label}`
    : props.relation.label
))
const multipleAccessibleName = computed(() => (
  multipleSelected.value.length
    ? `${props.relation.label}${t('fields.nameListSeparator')}${t('fields.selectedCount', { n: multipleSelected.value.length })}`
    : props.relation.label
))

// The combobox owns neither the chip display nor the array arithmetic — emit a new array every
// time per this repo's immutability convention.
function toggleValue(id: string): void {
  // A value the hint locks is neither added nor removed, from the menu or any other path.
  const option = displayOptions.value.find((o) => o.id === id)
  if (option && hintFor(option) !== null) return
  const next = multipleSelected.value.includes(id)
    ? multipleSelected.value.filter((v) => v !== id)
    : [...multipleSelected.value, id]
  onChange(next)
}

// Debounce only the search-driven reloads; the initial load must not wait 300ms.
const debouncedLoad = debounce(loadOptions, 300)
watch(search, debouncedLoad)
onMounted(async () => {
  await loadOptions()
  await ensureSelectedLabels()
})
onBeforeUnmount(() => debouncedLoad.cancel())

defineExpose({ loadOptions, ensureSelectedLabels, onChange, options, displayOptions, loading, loadError, search })
</script>

<template>
  <div class="relation-picker" :class="attrs.class" :style="attrs.style as StyleValue">
    <p v-if="loadError" class="error" role="alert">{{ loadError }}</p>

    <!-- form/TreeSelect.vue is layout-neutral (`w-full`, no ceiling) so its other caller
         (FilePicker's folder filter, already width-constrained by its dialog) isn't affected —
         the 480px cap the other three branches below carry is applied here, at this consumer. -->
    <div v-if="tree" class="w-full max-w-[480px]">
      <TreeSelect
        v-bind="controlAttrs()"
        :model-value="modelValue == null ? null : String(modelValue)"
        :nodes="treeNodes"
        :label="relation.label"
        :disabled="disabled"
        @update:model-value="onTreeChange"
      />
    </div>

    <div v-else-if="multiple" class="w-full max-w-[480px]">
      <Combobox v-model:open="menuOpen" :model-value="multipleSelected" multiple :disabled="disabled">
        <!-- The field box. The chips sit in it beside the one trigger, as siblings, never inside it: a remove
             control nested in the trigger button would be invalid HTML with real focus and click hazards. -->
        <ComboboxAnchor
          :class="multipleBoxClass"
          @click.self="onBoxClick"
        >
          <template v-if="showChips">
            <Badge
              v-for="opt in multipleSelectedOptions"
              :key="opt.id"
              variant="secondary"
              data-testid="relation-chip"
              class="gap-1 py-0.5"
              :class="disabled || hintFor(opt) !== null ? '' : 'pr-1'"
              :title="hintFor(opt) ?? undefined"
            >
              {{ opt.label }}
              <button
                v-if="!disabled && hintFor(opt) === null"
                type="button"
                class="inline-flex size-4 items-center justify-center rounded-full hover:bg-foreground/10"
                :aria-label="t('fields.removeOption', { label: opt.label })"
                @click="toggleValue(opt.id)"
              >
                <X class="size-3" />
              </button>
            </Badge>
          </template>
          <ComboboxTrigger v-if="addLabel" v-bind="controlAttrs()" as-child :aria-label="addLabel" @keydown.down.prevent="openMenu">
            <Button type="button" variant="outline" data-testid="relation-add" class="cursor-pointer" :disabled="disabled">
              <Plus />{{ addLabel }}
            </Button>
          </ComboboxTrigger>
          <ComboboxTrigger
            v-else
            v-bind="controlAttrs()"
            :aria-label="multipleAccessibleName"
            class="flex min-h-6 min-w-8 flex-1 items-center justify-between gap-2 self-stretch rounded-sm px-1 text-left disabled:cursor-not-allowed"
            @keydown.down.prevent="openMenu"
          >
            <span :class="multipleSelected.length ? '' : 'text-muted-foreground'">
              {{ showChips
                ? (multipleSelected.length ? '' : relation.label)
                : (multipleSelected.length ? t('fields.selectedCount', { n: multipleSelected.length }) : relation.label) }}
            </span>
            <ChevronsUpDown class="size-4 shrink-0 opacity-50" />
          </ComboboxTrigger>
        </ComboboxAnchor>
        <ComboboxList :align="addLabel ? 'start' : 'center'" :class="addLabel ? 'w-72 max-w-[calc(100vw-2rem)]' : 'w-(--reka-combobox-trigger-width)'">
          <ComboboxInput :model-value="search" :placeholder="t('fields.searchOptions')" @update:model-value="(v) => (search = String(v ?? ''))" />
          <ComboboxEmpty>{{ loading ? t('common.loading') : t('fields.noOptions') }}</ComboboxEmpty>
          <ComboboxViewport>
            <ComboboxItem
              v-for="opt in displayOptions"
              :key="opt.id"
              :value="opt.id"
              :disabled="hintFor(opt) !== null"
              :title="hintFor(opt) ?? undefined"
              @select.prevent="toggleValue(opt.id)"
            >
              {{ opt.label }}
              <ComboboxItemIndicator><Check class="size-4" /></ComboboxItemIndicator>
            </ComboboxItem>
          </ComboboxViewport>
        </ComboboxList>
      </Combobox>
    </div>

    <!-- ui/select has no search input, and this picker's options are server-side paginated, so a
         plain Select could only ever offer the first page — the combobox shape is used here too,
         with its own clear button. -->
    <div v-else class="flex w-full max-w-[480px] items-center gap-1.5">
      <Combobox class="flex-1" :model-value="singleSelected" :disabled="disabled" @update:model-value="onChange">
        <ComboboxAnchor class="w-full">
          <ComboboxTrigger
            v-bind="controlAttrs()"
            :aria-label="singleAccessibleName"
            class="border-input flex w-full items-center justify-between gap-2 rounded-md border bg-transparent px-3 py-2 text-sm disabled:cursor-not-allowed disabled:opacity-50"
          >
            <span :class="singleSelectedOption ? '' : 'text-muted-foreground'">
              {{ singleSelectedOption ? singleSelectedOption.label : relation.label }}
            </span>
            <ChevronsUpDown class="size-4 shrink-0 opacity-50" />
          </ComboboxTrigger>
        </ComboboxAnchor>
        <ComboboxList class="w-(--reka-combobox-trigger-width)">
          <ComboboxInput
            :model-value="search"
            :display-value="emptySearchDisplay"
            :placeholder="t('fields.searchOptions')"
            @update:model-value="(v) => (search = String(v ?? ''))"
          />
          <ComboboxEmpty>{{ loading ? t('common.loading') : t('fields.noOptions') }}</ComboboxEmpty>
          <ComboboxViewport>
            <ComboboxItem v-for="opt in displayOptions" :key="opt.id" :value="opt.id"
                          :disabled="hintFor(opt) !== null" :title="hintFor(opt) ?? undefined">
              {{ opt.label }}
              <ComboboxItemIndicator><Check class="size-4" /></ComboboxItemIndicator>
            </ComboboxItem>
          </ComboboxViewport>
        </ComboboxList>
      </Combobox>
      <Button
        v-if="modelValue != null"
        type="button"
        variant="ghost"
        size="icon-sm"
        data-testid="relation-clear"
        :aria-label="t('fields.clear')"
        :disabled="disabled"
        @click="onChange(null)"
      >
        <X class="size-4" />
      </Button>
    </div>
  </div>
</template>
