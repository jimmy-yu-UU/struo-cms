import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import TreeSelect from './TreeSelect.vue'
import en from '@/locales/en'
import zhTW from '@/locales/zh-TW'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
// reka's portal wrapper is itself named Teleport (plan-1 constraint 7).
const opts = { global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true } }

const i18nZh = createI18n({ legacy: false, locale: 'zh-TW', messages: { 'zh-TW': zhTW } })
const optsZh = { global: { plugins: [i18nZh], stubs: { teleport: true }, renderStubDefaultSlot: true } }

const NODES = [
  { key: 'root', label: 'Root', children: [{ key: 'child', label: 'Child' }] },
  { key: 'other', label: 'Other' },
]

describe('TreeSelect', () => {
  it('shows the placeholder when nothing is selected', () => {
    const w = mount(TreeSelect, { props: { modelValue: null, nodes: NODES }, ...opts })
    expect(w.get('button').text()).toContain(en.fields.selectAFolder)
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

  it('renders every node, nested included', () => {
    const w = mount(TreeSelect, { props: { modelValue: null, nodes: NODES }, ...opts })
    expect(w.text()).toContain('Root')
    expect(w.text()).toContain('Child')
    expect(w.text()).toContain('Other')
  })

  // Selection is one-way: the component must never hold its own idea of "the current node" past
  // the initial render. Swapping modelValue for defaultValue-seeded local state would make this
  // pass on first render only — the assertion has to survive a prop change made after mount.
  it('reflects a modelValue set after mount (not just on initial render)', async () => {
    const w = mount(TreeSelect, { props: { modelValue: null, nodes: NODES }, ...opts })
    expect(w.get('button').text()).toContain(en.fields.selectAFolder)
    await w.setProps({ modelValue: 'child' })
    expect(w.get('button').text()).toContain('Child')
    await w.setProps({ modelValue: null })
    expect(w.get('button').text()).toContain(en.fields.selectAFolder)
  })

  // The exposed select() above is real production surface (the same function a rendered node's
  // click runs), not a test-only escape hatch — this proves the click path itself, not just the
  // method it happens to share.
  it('emits the selected key from a real click on a rendered node', async () => {
    const w = mount(TreeSelect, { props: { modelValue: null, nodes: NODES }, ...opts })
    const child = w.findAll('[role="treeitem"]').find((el) => el.text().includes('Child'))
    expect(child).toBeTruthy()
    await child!.trigger('click')
    expect(w.emitted('update:modelValue')?.[0]).toEqual(['child'])
  })

  // Non-leaf nodes must be selectable too: FilePicker's folders and RelationPicker's parent items
  // are both intermediate nodes, not just leaves.
  it('emits the selected key from a real click on a non-leaf (parent) node', async () => {
    const w = mount(TreeSelect, { props: { modelValue: null, nodes: NODES }, ...opts })
    const root = w.findAll('[role="treeitem"]').find((el) => el.text().includes('Root'))
    expect(root).toBeTruthy()
    await root!.trigger('click')
    expect(w.emitted('update:modelValue')?.[0]).toEqual(['root'])
  })

  it('gives the trigger an explicit type="button"', () => {
    const w = mount(TreeSelect, { props: { modelValue: null, nodes: NODES }, ...opts })
    expect(w.get('button').attributes('type')).toBe('button')
  })

  // The accessible name must not depend on the current value, or a screen reader would announce
  // only the picked node's label with no indication of what it is a value of.
  it('gives the trigger a stable accessible name independent of the current value', () => {
    const w = mount(TreeSelect, { props: { modelValue: 'child', nodes: NODES }, ...opts })
    expect(w.get('button').attributes('aria-label')).toBe(en.fields.selectAFolder)
  })

  // Asserted under zh-TW specifically: comparing against the English string cannot distinguish a
  // localised value from a hardcoded one.
  it('uses the localized placeholder as the accessible name under zh-TW', () => {
    const w = mount(TreeSelect, { props: { modelValue: null, nodes: NODES }, ...optsZh })
    expect(w.get('button').attributes('aria-label')).toBe(zhTW.fields.selectAFolder)
    expect(w.get('button').text()).toContain(zhTW.fields.selectAFolder)
  })

  it('uses a custom placeholder for both the trigger text and the accessible name when given', () => {
    const w = mount(TreeSelect, { props: { modelValue: null, nodes: NODES, placeholder: 'Pick a node' }, ...opts })
    expect(w.get('button').text()).toContain('Pick a node')
    expect(w.get('button').attributes('aria-label')).toBe('Pick a node')
  })
})
