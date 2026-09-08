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
}>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()

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
    :relation="relation" :model-value="modelValue" :disabled="isDisabled"
    @update:model-value="update"
  />
  <JunctionLinksEditor
    v-else-if="linksEditor"
    :relation="relation" :model-value="modelValue" :disabled="isDisabled"
    @update:model-value="update"
  />
  <RelationPicker
    v-else-if="kind === 'tagSelect'"
    :relation="relation" :model-value="modelValue" multiple :disabled="isDisabled"
    @update:model-value="update"
  />
  <RelationPicker
    v-else-if="kind === 'treeSelect'"
    :relation="relation" :model-value="modelValue" tree :exclude-id="excludeId" :disabled="isDisabled"
    @update:model-value="update"
  />
  <RelatedList
    v-else-if="kind === 'relatedList'"
    :relation="relation" :parent-id="parentId"
  />
  <span v-else class="readonly-relation">{{ relation.label }} (read-only)</span>
</template>
