<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { TreeItem, TreeRoot } from 'reka-ui'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Button } from '@/components/ui/button'
import { ChevronDown, ChevronRight } from '@lucide/vue'

// Structurally compatible with lib/buildRelationTree.ts's TreeNode (key/label/children, plus an
// extra `data` field only PrimeVue's TreeSelect needed) — a TreeNode[] satisfies this prop as-is,
// so callers building on buildRelationTree need no shape conversion, only the modelValue
// unwrapping this component already removes (PrimeVue's TreeSelect bound a `{ [key]: true }`
// keyed object; this one takes and emits a plain key).
export type TreeSelectNode = { key: string; label: string; children?: TreeSelectNode[] }

// `label` names the field this control belongs to (e.g. "Parent page") — DatePicker.vue takes the
// same prop for the same reason: this component has no `field` of its own to read one from, and
// `aria-label` below overrides the trigger's visible contents rather than supplementing them, so
// without it two of these on one form would announce identically.
const props = defineProps<{
  modelValue: string | null
  nodes: TreeSelectNode[]
  disabled?: boolean
  placeholder?: string
  label?: string
}>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string | null): void }>()
const { t } = useI18n()

const open = ref(false)

// reka's TreeRoot only flattens a parent's children into the render list while that parent's key
// is in `expanded` (collapsed by default, accordion-style) — a plain "show everything" tree needs
// every branch open from the start, and again whenever the node list itself is replaced (e.g. an
// async folder load resolving after the popover already opened).
function branchKeys(nodes: TreeSelectNode[]): string[] {
  return nodes.flatMap((n) => (n.children?.length ? [n.key, ...branchKeys(n.children)] : []))
}
const expandedKeys = ref<string[]>(branchKeys(props.nodes))
watch(() => props.nodes, (n) => { expandedKeys.value = branchKeys(n) })

const getKey = (n: TreeSelectNode) => n.key
// TreeRoot's default getChildren (`val => val.children`) treats an empty array as "has children"
// (`[]` is truthy), and both real callers pass exactly that: buildRelationTree.ts seeds every node
// with `children: []`, and FilePicker's two synthetic folder nodes hardcode it too. Without this,
// every leaf would render a collapsed-branch chevron and announce `aria-expanded="false"` despite
// having nothing to disclose.
const getChildren = (n: TreeSelectNode) => (n.children?.length ? n.children : undefined)

function findInTree<R>(nodes: TreeSelectNode[], key: string, pick: (n: TreeSelectNode) => R): R | undefined {
  for (const n of nodes) {
    if (n.key === key) return pick(n)
    const nested = n.children ? findInTree(n.children, key, pick) : undefined
    if (nested !== undefined) return nested
  }
  return undefined
}

// Fallback order pinned by the tests: a matching node's label, then the raw key (a folder deleted
// while the picker was open must still show something rather than reading as "nothing
// selected"), and only the placeholder when there is truly no value.
const triggerLabel = computed(() => {
  if (props.modelValue === null) return props.placeholder ?? t('fields.selectAnItem')
  return findInTree(props.nodes, props.modelValue, (n) => n.label) ?? props.modelValue
})

// The actual node object for the current key, fed to TreeRoot below so the open panel can show
// which node is selected. One-way and read-only from this component's point of view: TreeRoot's
// own `modelValue` is hardcoded `passive: true` upstream, so it just mirrors whatever this
// computed produces and re-syncs on every change — nothing here listens for or stores its writes.
const selectedNode = computed(() => (
  props.modelValue === null ? undefined : findInTree(props.nodes, props.modelValue, (n) => n)
))

// aria-label overrides the trigger's visible contents rather than supplementing them, so it has to
// carry everything the sighted trigger text shows — the field label AND the current value or
// placeholder — or picking a value would make the announcement forget which field it came from.
const accessibleName = computed(() => (
  props.label ? `${props.label}${t('fields.namePairSeparator')}${triggerLabel.value}` : triggerLabel.value
))

// Selection stays one-way: this emits and lets modelValue flow back through the prop; it never
// keeps its own record of "the current node". The template's node click below is its only caller,
// and always passes a real key — the `| null` half of the parameter and emit type exists only to
// match `modelValue`'s own type, which callers like FilePicker's onFolderChange(key: string | null)
// need for their own "nothing selected" state; this component never produces null itself.
function select(key: string | null): void {
  emit('update:modelValue', key)
  open.value = false
}
</script>

<template>
  <Popover v-model:open="open">
    <PopoverTrigger as-child>
      <!--
        type="button" is explicit even though reka's PopoverTrigger (as-child) already merges its
        own type="button" onto whatever it wraps — an untyped native <button> would otherwise
        default to type="submit", and FilePicker/RelationPicker render this inside ItemForm.vue's
        <form @submit.prevent>.
      -->
      <Button
        type="button"
        variant="outline"
        :disabled="disabled"
        :aria-label="accessibleName"
        class="w-full justify-between font-normal"
      >
        <span class="truncate">{{ triggerLabel }}</span>
        <ChevronDown class="size-4 shrink-0 opacity-50" aria-hidden="true" />
      </Button>
    </PopoverTrigger>
    <PopoverContent class="w-64 p-1">
      <TreeRoot
        v-slot="{ flattenItems }"
        :items="nodes"
        :get-key="getKey"
        :get-children="getChildren"
        :model-value="selectedNode"
        :disabled="disabled"
        v-model:expanded="expandedKeys"
      >
        <TreeItem
          v-for="item in flattenItems"
          :key="item._id"
          v-bind="item.bind"
          class="flex cursor-pointer items-center gap-1 rounded-sm px-2 py-1.5 text-sm outline-none aria-selected:bg-accent hover:bg-accent"
          :style="{ paddingLeft: `${(item.level - 1) * 12 + 8}px` }"
          @select="() => select(item.value.key)"
          @toggle="(e: Event) => e.preventDefault()"
        >
          <ChevronDown v-if="item.hasChildren && expandedKeys.includes(item._id)" class="size-4 shrink-0 opacity-70" aria-hidden="true" />
          <ChevronRight v-else-if="item.hasChildren" class="size-4 shrink-0 opacity-70" aria-hidden="true" />
          <span v-else class="size-4 shrink-0" />
          <span class="truncate">{{ item.value.label }}</span>
        </TreeItem>
      </TreeRoot>
    </PopoverContent>
  </Popover>
</template>
