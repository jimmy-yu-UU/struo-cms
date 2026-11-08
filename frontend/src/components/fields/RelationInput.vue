<script setup lang="ts">
import { computed } from 'vue'
import RelationPicker from './RelationPicker.vue'
import RelatedList from './RelatedList.vue'
import JunctionLinksEditor from './JunctionLinksEditor.vue'
import { relationInputKind } from '../../lib/relationInputKind'
import { usesLinksEditor } from '../../lib/junctionLinks'
import { useSchemaStore } from '../../stores/schemaStore'
import type { RelationMeta } from '../../types/schema'

const props = defineProps<{
  relation: RelationMeta
  modelValue: unknown
  disabled?: boolean
  parentId?: string
  excludeId?: string
  optionHint?: (raw: Record<string, unknown>) => string | null
}>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
// ItemForm's id, aria-invalid and aria-describedby are re-bound onto whichever control renders.
defineOptions({ inheritAttrs: false })

const schema = useSchemaStore()
const kind = computed(() => relationInputKind(props.relation.interface))
const isDisabled = computed(() => props.disabled === true || props.relation.editable === false)
// tagSelect splits two ways: payload/SortField relations get the per-row editor, the rest keep chips.
const linksEditor = computed(() => kind.value === 'tagSelect' && usesLinksEditor(props.relation, (n) => schema.get(n)))
function update(v: unknown): void { emit('update:modelValue', v) }
</script>

<template>
  <RelationPicker
    v-if="kind === 'dropdown'"
    v-bind="$attrs"
    :relation="relation" :model-value="modelValue" :disabled="isDisabled" :option-hint="optionHint"
    @update:model-value="update"
  />
  <JunctionLinksEditor
    v-else-if="linksEditor"
    v-bind="$attrs"
    :relation="relation" :model-value="modelValue" :disabled="isDisabled"
    @update:model-value="update"
  />
  <RelationPicker
    v-else-if="kind === 'tagSelect'"
    v-bind="$attrs"
    :relation="relation" :model-value="modelValue" multiple :disabled="isDisabled" :option-hint="optionHint"
    @update:model-value="update"
  />
  <RelationPicker
    v-else-if="kind === 'treeSelect'"
    v-bind="$attrs"
    :relation="relation" :model-value="modelValue" tree :exclude-id="excludeId" :disabled="isDisabled"
    @update:model-value="update"
  />
  <RelatedList
    v-else-if="kind === 'relatedList'"
    v-bind="$attrs"
    :relation="relation" :parent-id="parentId"
  />
  <span v-else v-bind="$attrs" class="readonly-relation">{{ relation.label }} (read-only)</span>
</template>
