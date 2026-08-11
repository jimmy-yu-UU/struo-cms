import { describe, it, expect } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import TreeSelect from './TreeSelect.vue'
import en from '@/locales/en'
import zhTW from '@/locales/zh-TW'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
// reka's own portal component is itself named Teleport, which collides with VTU's
// `stubs: { teleport: true }` and drops slot content unless renderStubDefaultSlot is also set.
const opts = { global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true } }

const i18nZh = createI18n({ legacy: false, locale: 'zh-TW', messages: { 'zh-TW': zhTW } })
const optsZh = { global: { plugins: [i18nZh], stubs: { teleport: true }, renderStubDefaultSlot: true } }

const NODES = [
  { key: 'root', label: 'Root', children: [{ key: 'child', label: 'Child' }] },
  { key: 'other', label: 'Other' },
]

// PopoverContent is gated by reka's Presence, so the tree does not exist in the DOM until the
// trigger has actually been clicked open — mirrors what a real user does (see DatePicker.test.ts
// for the same helper shape).
async function open(w: VueWrapper): Promise<void> {
  await w.get('button').trigger('click')
  await w.vm.$nextTick()
  await w.vm.$nextTick()
}

function treeitem(w: VueWrapper, text: string) {
  return w.findAll('[role="treeitem"]').find((el) => el.text().includes(text))
}

describe('TreeSelect', () => {
  it('shows the placeholder when nothing is selected', () => {
    const w = mount(TreeSelect, { props: { modelValue: null, nodes: NODES }, ...opts })
    expect(w.get('button').text()).toContain(en.fields.selectAnItem)
  })

  it('shows the selected node label, including a nested one', () => {
    const w = mount(TreeSelect, { props: { modelValue: 'child', nodes: NODES }, ...opts })
    expect(w.get('button').text()).toContain('Child')
  })

  // A key with no matching node must not render as an empty trigger: the caller's data can lag
  // behind (a folder deleted while the picker was open), and a blank control reads as "nothing
  // selected" when something IS selected.
  it('falls back to the raw key when no node matches', () => {
    const w = mount(TreeSelect, { props: { modelValue: 'gone', nodes: NODES }, ...opts })
    expect(w.get('button').text()).toContain('gone')
  })

  it('emits the selected key', async () => {
    const w = mount(TreeSelect, { props: { modelValue: null, nodes: NODES }, ...opts })
    ;(w.vm as unknown as { select: (k: string | null) => void }).select('child')
    await w.vm.$nextTick()
    expect(w.emitted('update:modelValue')?.[0]).toEqual(['child'])
  })

  it('emits null when the selection is cleared', async () => {
    const w = mount(TreeSelect, { props: { modelValue: 'child', nodes: NODES }, ...opts })
    ;(w.vm as unknown as { select: (k: string | null) => void }).select(null)
    await w.vm.$nextTick()
    expect(w.emitted('update:modelValue')?.[0]).toEqual([null])
  })

  it('disables the trigger', () => {
    const w = mount(TreeSelect, { props: { modelValue: null, nodes: NODES, disabled: true }, ...opts })
    expect(w.get('button').attributes('disabled')).toBeDefined()
  })

  it('renders every node, nested included, once opened', async () => {
    const w = mount(TreeSelect, { props: { modelValue: null, nodes: NODES }, ...opts })
    await open(w)
    expect(w.text()).toContain('Root')
    expect(w.text()).toContain('Child')
    expect(w.text()).toContain('Other')
  })

  // Both real callers (buildRelationTree.ts, FilePicker's synthetic folder nodes) seed every leaf
  // with `children: []`, which TreeRoot's default getChildren treats as "has children" (`[]` is
  // truthy). A leaf must not render as a collapsed, expandable branch.
  it('does not mark a leaf whose children is an empty array as expandable', async () => {
    const nodesWithEmptyChildren = [
      { key: 'root', label: 'Root', children: [{ key: 'leaf', label: 'Leaf', children: [] }] },
    ]
    const w = mount(TreeSelect, { props: { modelValue: null, nodes: nodesWithEmptyChildren }, ...opts })
    await open(w)
    const leaf = treeitem(w, 'Leaf')
    expect(leaf).toBeTruthy()
    expect(leaf!.attributes('aria-expanded')).toBeUndefined()
  })

  // Selection is one-way: the component must never hold its own idea of "the current node" past
  // the initial render. Swapping modelValue for defaultValue-seeded local state would make this
  // pass on first render only — the assertion has to survive a prop change made after mount.
  it('reflects a modelValue set after mount (not just on initial render)', async () => {
    const w = mount(TreeSelect, { props: { modelValue: null, nodes: NODES }, ...opts })
    expect(w.get('button').text()).toContain(en.fields.selectAnItem)
    await w.setProps({ modelValue: 'child' })
    expect(w.get('button').text()).toContain('Child')
    await w.setProps({ modelValue: null })
    expect(w.get('button').text()).toContain(en.fields.selectAnItem)
  })

  // The exposed select() above is real production surface (the same function a rendered node's
  // click runs), not a test-only escape hatch — this proves the click path itself, not just the
  // method it happens to share.
  it('emits the selected key from a real click on a rendered node', async () => {
    const w = mount(TreeSelect, { props: { modelValue: null, nodes: NODES }, ...opts })
    await open(w)
    const child = treeitem(w, 'Child')
    expect(child).toBeTruthy()
    await child!.trigger('click')
    expect(w.emitted('update:modelValue')?.[0]).toEqual(['child'])
  })

  // Non-leaf nodes must be selectable too: FilePicker's folders and RelationPicker's parent items
  // are both intermediate nodes, not just leaves.
  it('emits the selected key from a real click on a non-leaf (parent) node', async () => {
    const w = mount(TreeSelect, { props: { modelValue: null, nodes: NODES }, ...opts })
    await open(w)
    const root = treeitem(w, 'Root')
    expect(root).toBeTruthy()
    await root!.trigger('click')
    expect(w.emitted('update:modelValue')?.[0]).toEqual(['root'])
  })

  // TreeItem's single native click runs select AND toggle unconditionally. Left unhandled, that
  // toggle collapses the clicked branch in `expanded` state — which survives the popover closing
  // (selection closes it) because `expandedKeys` lives in this component, not in the popover's own
  // lifecycle. Re-opening would otherwise show the just-selected branch collapsed, its children
  // gone, with no other way to expand it back (the chevron is decorative, not a button).
  it('keeps a branch expanded after selecting it with a real click, even after the popover re-opens', async () => {
    const w = mount(TreeSelect, { props: { modelValue: null, nodes: NODES }, ...opts })
    await open(w)
    const root = treeitem(w, 'Root')
    await root!.trigger('click')
    expect(w.emitted('update:modelValue')?.[0]).toEqual(['root'])
    await open(w)
    expect(w.text()).toContain('Child')
  })

  // The node matching the current modelValue must announce as selected inside the open panel, and
  // that has to keep tracking modelValue after mount, not just on first render.
  it('marks the node matching modelValue aria-selected, including after a post-mount change', async () => {
    const w = mount(TreeSelect, { props: { modelValue: 'child', nodes: NODES }, ...opts })
    await open(w)
    expect(treeitem(w, 'Child')!.attributes('aria-selected')).toBe('true')
    expect(treeitem(w, 'Root')!.attributes('aria-selected')).toBe('false')

    await w.setProps({ modelValue: 'root' })
    await w.vm.$nextTick()
    expect(treeitem(w, 'Root')!.attributes('aria-selected')).toBe('true')
    expect(treeitem(w, 'Child')!.attributes('aria-selected')).toBe('false')
  })

  it('gives the trigger an explicit type="button"', () => {
    const w = mount(TreeSelect, { props: { modelValue: null, nodes: NODES }, ...opts })
    expect(w.get('button').attributes('type')).toBe('button')
  })

  it('gives the trigger an accessible name that combines the field label with the current value, and updates as the value changes', async () => {
    const w = mount(TreeSelect, { props: { modelValue: null, nodes: NODES, label: 'Parent page' }, ...opts })
    const before = `Parent page: ${en.fields.selectAnItem}`
    expect(w.get('button').attributes('aria-label')).toBe(before)

    await w.setProps({ modelValue: 'child' })
    const after = w.get('button').attributes('aria-label')
    // aria-label overrides element contents rather than supplementing them, so both the field
    // label and the current value must be present in the one attribute.
    expect(after).toBe('Parent page: Child')
    expect(after).not.toBe(before)
  })

  it('falls back to the value or placeholder alone as the accessible name when no label is given', () => {
    const w = mount(TreeSelect, { props: { modelValue: 'child', nodes: NODES } , ...opts })
    expect(w.get('button').attributes('aria-label')).toBe('Child')
  })

  // Asserted under zh-TW specifically: comparing against the English string cannot distinguish a
  // localised value from a hardcoded one.
  it('uses the localized default placeholder as the accessible name under zh-TW', () => {
    const w = mount(TreeSelect, { props: { modelValue: null, nodes: NODES }, ...optsZh })
    expect(w.get('button').attributes('aria-label')).toBe(zhTW.fields.selectAnItem)
    expect(w.get('button').text()).toContain(zhTW.fields.selectAnItem)
  })

  it('uses a custom placeholder for both the trigger text and the accessible name when given', () => {
    const w = mount(TreeSelect, { props: { modelValue: null, nodes: NODES, placeholder: 'Pick a node' }, ...opts })
    expect(w.get('button').text()).toContain('Pick a node')
    expect(w.get('button').attributes('aria-label')).toBe('Pick a node')
  })
})
