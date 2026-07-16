<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import Select from 'primevue/select'
import MultiSelect from 'primevue/multiselect'
import TreeSelect from 'primevue/treeselect'
import { itemsApi } from '../../api/itemsApi'
import { useSchemaStore } from '../../stores/schemaStore'
import { useLanguageStore } from '../../stores/languageStore'
import { resolveDisplayLabel } from '../../lib/resolveDisplayLabel'
import { buildRelationTree, type TreeNode } from '../../lib/buildRelationTree'
import { debounce } from '../../lib/debounce'
import { createLatestWins } from '../../lib/latestWins'
import type { RelationMeta } from '../../types/schema'

const props = defineProps<{
  relation: RelationMeta
  modelValue: unknown
  multiple?: boolean
  tree?: boolean
  disabled?: boolean
  excludeId?: string
}>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()

const schema = useSchemaStore()
const langStore = useLanguageStore()

type Option = { id: string; label: string; raw: Record<string, unknown> }
const options = ref<Option[]>([])
const labelById = ref<Record<string, string>>({})
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
    for (const o of options.value) labelById.value[o.id] = o.label
  } catch (e) {
    if (!optionsLoad.isCurrent(token)) return
    loadError.value = e instanceof Error ? e.message : 'Failed to load options.'
  } finally {
    if (optionsLoad.isCurrent(token)) loading.value = false
  }
}

async function ensureSelectedLabels(): Promise<void> {
  const ids = props.multiple
    ? ((props.modelValue as string[] | null) ?? [])
    : props.modelValue != null ? [String(props.modelValue)] : []
  for (const id of ids) {
    if (labelById.value[id]) continue
    try {
      const row = await itemsApi.get(props.relation.targetCollection, id, { locale: langStore.defaultCode })
      labelById.value[id] = toOption(row).label
    } catch {
      labelById.value[id] = id // deleted / inaccessible -> show id
    }
  }
}

// Select/MultiSelect render labels from their options list only, so a preselected
// id that is not on the current options page would show as a raw id. Merge any
// such selected-but-absent ids (labelled from labelById, back-filled by
// ensureSelectedLabels) into the rendered options without mutating either source.
const displayOptions = computed<Option[]>(() => {
  const selected = props.multiple
    ? ((props.modelValue as string[] | null) ?? []).map(String)
    : props.modelValue != null ? [String(props.modelValue)] : []
  const present = new Set(options.value.map((o) => o.id))
  const missingIds = [...new Set(selected.filter((id) => !present.has(id)))]
  const missing: Option[] = missingIds.map((id) => ({
    id, label: labelById.value[id] ?? id, raw: {},
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

// TreeSelect binds an object keyed by node key; map to a single id.
const treeValue = computed(() => (props.modelValue != null ? { [String(props.modelValue)]: true } : {}))
function onTreeChange(selection: Record<string, boolean>): void {
  const keys = Object.keys(selection)
  emit('update:modelValue', keys.length ? keys[0] : null)
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
  <div class="relation-picker">
    <p v-if="loadError" class="error" role="alert">{{ loadError }}</p>

    <TreeSelect
      v-if="tree"
      :model-value="treeValue"
      :options="treeNodes"
      selection-mode="single"
      :disabled="disabled"
      :loading="loading"
      @update:model-value="onTreeChange"
    />

    <MultiSelect
      v-else-if="multiple"
      :model-value="modelValue"
      :options="displayOptions"
      option-label="label"
      option-value="id"
      filter
      :disabled="disabled"
      :loading="loading"
      @filter="(e: { value: string }) => (search = e.value)"
      @update:model-value="onChange"
    />

    <Select
      v-else
      :model-value="modelValue"
      :options="displayOptions"
      option-label="label"
      option-value="id"
      filter
      show-clear
      :disabled="disabled"
      :loading="loading"
      @filter="(e: { value: string }) => (search = e.value)"
      @update:model-value="onChange"
    />
  </div>
</template>
