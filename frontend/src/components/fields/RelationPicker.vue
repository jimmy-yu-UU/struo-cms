<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import Select from 'primevue/select'
import MultiSelect from 'primevue/multiselect'
import TreeSelect from 'primevue/treeselect'
import { itemsApi } from '../../api/itemsApi'
import { useSchemaStore } from '../../stores/schemaStore'
import { useLanguageStore } from '../../stores/languageStore'
import { resolveDisplayLabel } from '../../lib/resolveDisplayLabel'
import { buildRelationTree, type TreeNode } from '../../lib/buildRelationTree'
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

async function loadOptions(): Promise<void> {
  loading.value = true
  loadError.value = ''
  try {
    const res = await itemsApi.list(props.relation.targetCollection, {
      page: 0, rows: 25, search: search.value || undefined, locale: langStore.defaultCode || undefined,
    })
    options.value = res.data.map(toOption)
    for (const o of options.value) labelById.value[o.id] = o.label
  } catch (e) {
    loadError.value = e instanceof Error ? e.message : 'Failed to load options.'
  } finally {
    loading.value = false
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

watch(search, loadOptions)
onMounted(async () => {
  await loadOptions()
  await ensureSelectedLabels()
})

defineExpose({ loadOptions, ensureSelectedLabels, onChange, options, loading, loadError })
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
      :options="options"
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
      :options="options"
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
