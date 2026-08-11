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

const props = defineProps<{
  modelValue: string | null
  nodes: TreeSelectNode[]
  disabled?: boolean
  placeholder?: string
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

function findLabel(nodes: TreeSelectNode[], key: string): string | undefined {
  for (const n of nodes) {
    if (n.key === key) return n.label
    const nested = n.children ? findLabel(n.children, key) : undefined
    if (nested !== undefined) return nested
  }
  return undefined
}

// Fallback order pinned by the tests: a matching node's label, then the raw key (a folder deleted
// while the picker was open must still show something rather than reading as "nothing
// selected"), and only the placeholder when there is truly no value.
const triggerLabel = computed(() => {
  if (props.modelValue === null) return props.placeholder ?? t('fields.selectAFolder')
  return findLabel(props.nodes, props.modelValue) ?? props.modelValue
})

// Deliberately independent of the current value: once a node is picked, a trigger whose only
// accessible name comes from its own text content would have a screen reader announce the value
// alone with no indication of what it is a value of.
const accessibleName = computed(() => props.placeholder ?? t('fields.selectAFolder'))

// The one path both a real click on a rendered node and any other caller drive selection
// through — not a test-only surface. Selection stays one-way: this emits and lets modelValue flow
// back through the prop; it never keeps its own record of "the current node".
function select(key: string | null): void {
  emit('update:modelValue', key)
  open.value = false
}
defineExpose({ select })
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
    <!--
      force-mount keeps the tree in the DOM while the popover is closed — reka's Presence would
      otherwise unmount PopoverContent entirely, and node selectability here has nothing to do
      with the popover's own open/closed animation state. `hidden` below is a plain Tailwind
      utility applied by this component, not a reka data attribute.
    -->
    <PopoverContent force-mount :class="open ? 'w-64 p-1' : 'hidden'">
      <TreeRoot
        v-slot="{ flattenItems }"
        :items="nodes"
        :get-key="getKey"
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
