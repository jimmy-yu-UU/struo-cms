import { describe, it, expect, vi, beforeEach } from 'vitest'
import { nextTick } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import RelationPicker from './RelationPicker.vue'
import { itemsApi } from '../../api/itemsApi'
import { useSchemaStore } from '../../stores/schemaStore'
import { useLanguageStore } from '../../stores/languageStore'
import en from '../../locales/en'
import zhTW from '../../locales/zh-TW'
import type { RelationMeta, CollectionMeta } from '../../types/schema'

// The real packs, not a hand-picked subset: the migrated template now calls several `fields.*`
// keys (searchOptions, noOptions, selectedCount, removeOption, clear) that a minimal fixture
// would silently fall back to raw key paths for, defeating the zh-TW assertions below.
const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })
const i18nZh = createI18n({ legacy: false, locale: 'zh-TW', fallbackLocale: 'zh-TW', messages: { 'zh-TW': zhTW } })

const targetMeta: CollectionMeta = {
  name: 'category',
  label: 'Category',
  defaultDisplayField: 'name',
  fields: [
    { name: 'name', label: 'Name', interface: 'text', required: false, searchable: false, sortable: false,
      readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false },
  ],
  relations: [],
}

const relation: RelationMeta = {
  name: 'category',
  label: 'Category',
  kind: 'manyToOne',
  targetCollection: 'category',
  interface: 'dropdown',
  foreignKey: 'CategoryId',
  displayTemplate: null,
  editable: true,
  selfReferencing: false,
}

const stubs = {
  Combobox: true,
  TreeSelect: true,
}

// reka's own portal is itself named Teleport, colliding with VTU's `stubs: { teleport: true }`
// and dropping slot content unless renderStubDefaultSlot is also set. Used by the tests below that
// need the real combobox/tree subtree mounted (child-emit assertions, real clicks, zh-TW text).
const live = { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true }
const liveZh = { plugins: [i18nZh], stubs: { teleport: true }, renderStubDefaultSlot: true }

function setupStores() {
  const schema = useSchemaStore()
  schema.get = vi.fn().mockReturnValue(targetMeta) as never
  const lang = useLanguageStore()
  lang.languages = [{ code: 'en', name: 'English', isDefault: true }]
  return { schema, lang }
}

function mountPicker(
  props: Partial<{ modelValue: unknown; multiple: boolean; tree: boolean; disabled: boolean }> = {},
  global: Record<string, unknown> = { plugins: [i18n], stubs },
) {
  return mount(RelationPicker, { props: { relation, modelValue: null, ...props }, global })
}

async function open(w: ReturnType<typeof mount>): Promise<void> {
  await w.get('[data-slot="combobox-trigger"]').trigger('click')
  await w.vm.$nextTick()
  await w.vm.$nextTick()
}

describe('RelationPicker', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
  })

  it('lazy-loads options via itemsApi.list and resolves labels', async () => {
    setupStores()
    const listSpy = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [{ id: 'c1', name: 'Tech' }], total: 1 })
    const w = mount(RelationPicker, { props: { relation, modelValue: null }, global: { plugins: [i18n], stubs } })
    await (w.vm as any).loadOptions()
    expect(listSpy).toHaveBeenCalledWith('category', expect.objectContaining({ page: 0 }))
    expect((w.vm as any).options).toHaveLength(1)
    expect((w.vm as any).options[0].label).toBe('Tech')
  })

  it('emits update:modelValue on single-select change', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mount(RelationPicker, { props: { relation, modelValue: null }, global: { plugins: [i18n], stubs } })
    ;(w.vm as any).onChange('c1')
    expect(w.emitted('update:modelValue')).toBeTruthy()
    expect(w.emitted('update:modelValue')![0]).toEqual(['c1'])
  })

  it('debounces search-driven reloads into a single request', async () => {
    setupStores()
    const listSpy = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mount(RelationPicker, { props: { relation, modelValue: null }, global: { plugins: [i18n], stubs } })
    await flushPromises() // mount load settles under real timers
    listSpy.mockClear()
    vi.useFakeTimers()
    try {
      ;(w.vm as any).search = 'a'
      await nextTick()
      ;(w.vm as any).search = 'ab'
      await nextTick()
      ;(w.vm as any).search = 'abc'
      await nextTick()
      expect(listSpy).not.toHaveBeenCalled()
      await vi.advanceTimersByTimeAsync(300)
      expect(listSpy).toHaveBeenCalledTimes(1)
    } finally {
      vi.useRealTimers()
    }
  })

  it('back-fills a preselected label via itemsApi.get when not present in options', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const getSpy = vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'c9', name: 'Archived' })
    const w = mount(RelationPicker, { props: { relation, modelValue: 'c9' }, global: { plugins: [i18n], stubs } })
    await (w.vm as any).ensureSelectedLabels()
    expect(getSpy).toHaveBeenCalledWith('category', 'c9', expect.objectContaining({}))
  })

  it('merges a preselected-but-absent id into displayOptions using labelById', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'c9', name: 'Archived' })
    const w = mount(RelationPicker, { props: { relation, modelValue: 'c9' }, global: { plugins: [i18n], stubs } })
    await flushPromises() // mount: loadOptions (empty) + ensureSelectedLabels settle
    const disp = (w.vm as any).displayOptions
    expect(disp).toHaveLength(1)
    expect(disp[0]).toEqual({ id: 'c9', label: 'Archived', raw: {} })
  })

  it('ignores a stale load that resolves after a newer one (latest-wins)', async () => {
    setupStores()
    const listSpy = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mount(RelationPicker, { props: { relation, modelValue: null }, global: { plugins: [i18n], stubs } })
    await flushPromises() // let the mount load settle first
    let resolveStale!: (v: { data: Record<string, unknown>[]; total: number }) => void
    let resolveFresh!: (v: { data: Record<string, unknown>[]; total: number }) => void
    const stale = new Promise<{ data: Record<string, unknown>[]; total: number }>((r) => (resolveStale = r))
    const fresh = new Promise<{ data: Record<string, unknown>[]; total: number }>((r) => (resolveFresh = r))
    listSpy.mockReturnValueOnce(stale as never).mockReturnValueOnce(fresh as never)
    const pStale = (w.vm as any).loadOptions() // older token
    const pFresh = (w.vm as any).loadOptions() // newer token supersedes
    resolveFresh({ data: [{ id: 'new', name: 'New' }], total: 1 })
    await pFresh
    resolveStale({ data: [{ id: 'old', name: 'Old' }], total: 1 }) // stale resolves last
    await pStale
    await flushPromises()
    expect((w.vm as any).options).toHaveLength(1)
    expect((w.vm as any).options[0].id).toBe('new')
  })

  // show-clear was a PrimeVue Select prop; ui/select has no equivalent, so the ability to unset a
  // relation only survives if we render the control ourselves.
  it('offers a clear control on the single-select branch', () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mountPicker({ modelValue: 'x' })
    expect(w.find('[data-testid="relation-clear"]').exists()).toBe(true)
  })

  it('offers no clear control when nothing is selected', () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mountPicker({ modelValue: null })
    expect(w.find('[data-testid="relation-clear"]').exists()).toBe(false)
  })

  it('clearing emits null', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mountPicker({ modelValue: 'x' })
    await w.get('[data-testid="relation-clear"]').trigger('click')
    expect(w.emitted('update:modelValue')?.[0]).toEqual([null])
  })

  it('gives the clear button an explicit type="button" so it cannot submit ItemForm.vue\'s <form>', () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mountPicker({ modelValue: 'x' })
    expect(w.get('[data-testid="relation-clear"]').attributes('type')).toBe('button')
  })

  it('takes a plain key on the tree branch', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mountPicker({ tree: true, modelValue: null })
    const vm = w.vm as unknown as { onTreeChange: (k: string | null) => void }
    vm.onTreeChange('n1')
    await w.vm.$nextTick()
    expect(w.emitted('update:modelValue')?.[0]).toEqual(['n1'])
  })

  // jsdom cannot drive reka's TreeItem selection directly (Popover content is teleported and its
  // own selection path is exercised by TreeSelect.test.ts already), but VTU can emit from the real
  // vendored child, which runs RelationPicker's actual template listener rather than a call
  // straight into the handler function.
  it("wires TreeSelect's real update:modelValue emit through to the picker's own emit", async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mountPicker({ tree: true, modelValue: null }, live)
    await flushPromises()
    await w.findComponent({ name: 'TreeSelect' }).vm.$emit('update:modelValue', 'n1')
    expect(w.emitted('update:modelValue')?.[0]).toEqual(['n1'])
  })

  it('passes a plain key (not a keyed-object) as the tree model-value, including through a later prop change', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mountPicker({ tree: true, modelValue: 'n1' }, live)
    await flushPromises()
    expect(w.findComponent({ name: 'TreeSelect' }).props('modelValue')).toBe('n1')
    await w.setProps({ modelValue: 'n2' })
    expect(w.findComponent({ name: 'TreeSelect' }).props('modelValue')).toBe('n2')
  })

  it('passes null (not an empty keyed-object) as the tree model-value when nothing is selected', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mountPicker({ tree: true, modelValue: null }, live)
    await flushPromises()
    expect(w.findComponent({ name: 'TreeSelect' }).props('modelValue')).toBe(null)
  })

  it('passes disabled through to the tree branch', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mountPicker({ tree: true, modelValue: null, disabled: true }, live)
    await flushPromises()
    expect(w.findComponent({ name: 'TreeSelect' }).props('disabled')).toBe(true)
  })

  // TreeSelect has no `field` of its own to read a label from (form/TreeSelect.vue takes a plain
  // `label` prop for exactly this reason) — without passing the relation's own label, two tree
  // relation fields on the same form would announce identically.
  it("gives the tree branch's TreeSelect the relation's own label, and no placeholder override", async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mountPicker({ tree: true, modelValue: null }, live)
    await flushPromises()
    const tree = w.findComponent({ name: 'TreeSelect' })
    expect(tree.props('label')).toBe(relation.label)
    expect(tree.props('placeholder')).toBeUndefined()
  })

  // Real click-driven: reka's ComboboxItem selects on a genuine `click` (unlike ui/select's
  // SelectItem, which only responds to pointerdown/pointerup), so jsdom can drive the actual
  // production interaction on the rendered option directly.
  it('emits the clicked option value through the real combobox on the single-select branch', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [{ id: 'c1', name: 'Tech' }], total: 1 })
    const w = mountPicker({ modelValue: null }, live)
    await flushPromises()
    await open(w)
    await w.get('[role="option"]').trigger('click')
    expect(w.emitted('update:modelValue')?.[0]).toEqual(['c1'])
  })

  it('reflects the incoming single-select model on the trigger, including after the model changes', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({
      data: [{ id: 'c1', name: 'Tech' }, { id: 'c2', name: 'Widget' }], total: 2,
    })
    const w = mountPicker({ modelValue: 'c1' }, live)
    await flushPromises()
    expect(w.get('[data-slot="combobox-trigger"]').text()).toContain('Tech')
    await w.setProps({ modelValue: 'c2' })
    expect(w.get('[data-slot="combobox-trigger"]').text()).toContain('Widget')
    expect(w.get('[data-slot="combobox-trigger"]').text()).not.toContain('Tech')
  })

  // The chip/trigger-text assertions above read this component's own computed, not reka's
  // internal Combobox state, so they cannot tell controlled apart from reka's non-reactive
  // `passive` (uncontrolled) mode — `aria-selected` is set from the root's own model instead, and
  // is checked again after `setProps` because reka reads `passive: props.modelValue === void 0`
  // once at setup, so an initial-render-only assertion cannot distinguish a live prop from a value
  // that only happened to be right at mount.
  it('reflects the incoming single-select model as aria-selected on the options, including after the model changes', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({
      data: [{ id: 'c1', name: 'Tech' }, { id: 'c2', name: 'Widget' }], total: 2,
    })
    const w = mountPicker({ modelValue: 'c1' }, live)
    await flushPromises()
    await open(w)
    const items = w.findAll('[role="option"]')
    expect(items[0].attributes('aria-selected')).toBe('true')
    expect(items[1].attributes('aria-selected')).toBe('false')

    await w.setProps({ modelValue: 'c2' })
    expect(items[0].attributes('aria-selected')).toBe('false')
    expect(items[1].attributes('aria-selected')).toBe('true')
  })

  // reka's ComboboxRoot defaults `resetSearchTermOnSelect`/`resetSearchTermOnBlur` to true, and
  // ComboboxInput seeds its own value from the *root's* modelValue the moment it mounts (its
  // Presence-gated content only exists once the popup opens) — with no `displayValue` override, a
  // scalar single-select model gets stringified straight into the search box. Because that box is
  // bound to this component's own `search` ref, the stringified id flows back through
  // `@update:model-value`, poisoning `search` with a raw id and firing a bogus server query that
  // filters the option list down to just the already-selected row — making it impossible to pick
  // anything else until the user notices and manually clears it.
  it('does not poison search with the selected id when the popup opens with a value already selected', async () => {
    setupStores()
    const listSpy = vi.spyOn(itemsApi, 'list').mockResolvedValue({
      data: [{ id: 'c1', name: 'Tech' }, { id: 'c2', name: 'Widget' }], total: 2,
    })
    const w = mountPicker({ modelValue: 'c1' }, live)
    await flushPromises() // mount load settles under real timers
    listSpy.mockClear()
    vi.useFakeTimers()
    try {
      await open(w)
      await vi.advanceTimersByTimeAsync(300) // past the debounce that a poisoned `search` would trigger
      expect((w.vm as any).search).toBe('')
      expect(listSpy).not.toHaveBeenCalled()
    } finally {
      vi.useRealTimers()
    }
  })

  // Pins the one line this task actually had to change: the debounce/latest-wins tests above only
  // ever assign `(w.vm as any).search = ...` directly, which would stay green even if
  // `@update:model-value` were deleted from `<ComboboxInput>` entirely. This drives a real `input`
  // event on the rendered search box instead.
  it('wires a real input event on the single-select search box through to search, debounced into one request', async () => {
    setupStores()
    const listSpy = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [{ id: 'c1', name: 'Tech' }], total: 1 })
    const w = mountPicker({ modelValue: null }, live)
    await flushPromises()
    listSpy.mockClear()
    await open(w)
    vi.useFakeTimers()
    try {
      const input = w.get('[data-slot="command-input"]')
      ;(input.element as HTMLInputElement).value = 'tec'
      await input.trigger('input')
      expect((w.vm as any).search).toBe('tec')
      expect(listSpy).not.toHaveBeenCalled()
      await vi.advanceTimersByTimeAsync(300)
      expect(listSpy).toHaveBeenCalledTimes(1)
      expect(listSpy).toHaveBeenCalledWith('category', expect.objectContaining({ search: 'tec' }))
    } finally {
      vi.useRealTimers()
    }
  })

  it('folds the current value into the single-select trigger\'s accessible name', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [{ id: 'c1', name: 'Tech' }], total: 1 })
    const w = mountPicker({ modelValue: 'c1' }, live)
    await flushPromises()
    expect(w.get('[data-slot="combobox-trigger"]').attributes('aria-label')).toBe(`${relation.label}: Tech`)
  })

  it('uses the CJK pair separator in zh-TW for the single-select accessible name', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [{ id: 'c1', name: 'Tech' }], total: 1 })
    const w = mountPicker({ modelValue: 'c1' }, liveZh)
    await flushPromises()
    expect(w.get('[data-slot="combobox-trigger"]').attributes('aria-label'))
      .toBe(`${relation.label}${zhTW.fields.namePairSeparator}Tech`)
  })

  it('genuinely disables the single-select trigger and its clear button', () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mountPicker({ modelValue: 'x', disabled: true }, live)
    expect(w.get('[data-slot="combobox-trigger"]').attributes('disabled')).toBe('')
    expect(w.get('[data-testid="relation-clear"]').attributes('disabled')).toBeDefined()
  })

  it('shows the real localized clear label under zh-TW', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mountPicker({ modelValue: 'x' }, liveZh)
    await flushPromises()
    expect(w.get('[data-testid="relation-clear"]').attributes('aria-label')).toBe(zhTW.fields.clear)
  })

  // No selected value here (unlike the clear-label test above): a selected id with no matching
  // real option is itself merged into displayOptions as a fallback row, which would always leave
  // one item present and never trigger ComboboxEmpty.
  it('shows the real localized search placeholder and empty-state text under zh-TW', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mountPicker({ modelValue: null }, liveZh)
    await flushPromises()
    await open(w)
    expect(w.get('[data-slot="command-input"]').attributes('placeholder')).toBe(zhTW.fields.searchOptions)
    expect(w.get('[data-slot="combobox-empty"]').text()).toBe(zhTW.fields.noOptions)
  })

  // `loading` is part of the exposed contract (defineExpose) precisely because it is real state a
  // consumer could act on — it must not become write-only. While a load is in flight and no
  // options are rendered yet, the combobox's own empty-state area is the only place with room to
  // say so without adding new markup.
  it('shows loading text in the empty-state area while options are in flight, under zh-TW', async () => {
    setupStores()
    let resolveList!: (v: { data: Record<string, unknown>[]; total: number }) => void
    const pending = new Promise<{ data: Record<string, unknown>[]; total: number }>((r) => (resolveList = r))
    vi.spyOn(itemsApi, 'list').mockReturnValue(pending as never)
    const w = mountPicker({ modelValue: null }, liveZh)
    await open(w)
    expect(w.get('[data-slot="combobox-empty"]').text()).toBe(zhTW.common.loading)
    resolveList({ data: [], total: 0 })
    await flushPromises()
    expect(w.get('[data-slot="combobox-empty"]').text()).toBe(zhTW.fields.noOptions)
  })

  it('appends a clicked option immutably on the multi-select branch', async () => {
    setupStores()
    const before = ['c1']
    vi.spyOn(itemsApi, 'list').mockResolvedValue({
      data: [{ id: 'c1', name: 'Tech' }, { id: 'c2', name: 'Widget' }], total: 2,
    })
    const w = mountPicker({ multiple: true, modelValue: before }, live)
    await flushPromises()
    await open(w)
    const options = w.findAll('[role="option"]')
    await options[1].trigger('click') // Widget
    expect(w.emitted('update:modelValue')?.[0]).toEqual([['c1', 'c2']])
    expect(before).toEqual(['c1'])
  })

  it('removes a value via its chip remove button on the multi-select branch', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({
      data: [{ id: 'c1', name: 'Tech' }, { id: 'c2', name: 'Widget' }], total: 2,
    })
    const w = mountPicker({ multiple: true, modelValue: ['c1', 'c2'] }, live)
    await flushPromises()
    const removeTech = w.get('[aria-label="Remove Tech"]')
    expect(removeTech.attributes('type')).toBe('button')
    await removeTech.trigger('click')
    expect(w.emitted('update:modelValue')?.[0]).toEqual([['c2']])
  })

  it('reflects the incoming multi-select model on the chip row, including after the model changes', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({
      data: [{ id: 'c1', name: 'Tech' }, { id: 'c2', name: 'Widget' }], total: 2,
    })
    const w = mountPicker({ multiple: true, modelValue: ['c1'] }, live)
    await flushPromises()
    expect(w.text()).toContain('Tech')
    expect(w.text()).not.toContain('Widget')
    await w.setProps({ modelValue: ['c2'] })
    expect(w.text()).not.toContain('Tech')
    expect(w.text()).toContain('Widget')
  })

  // Same mode-check as the single-select branch: the chip row above reads this component's own
  // computed, never reka's internal Combobox state, so it cannot distinguish controlled from
  // `passive` mode. `aria-selected` is reka's own render, driven by the root's `multiple`
  // model-value via its array-aware `valueComparator`.
  it('reflects the incoming multi-select model as aria-selected on the options, including after the model changes', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({
      data: [{ id: 'c1', name: 'Tech' }, { id: 'c2', name: 'Widget' }], total: 2,
    })
    const w = mountPicker({ multiple: true, modelValue: ['c1'] }, live)
    await flushPromises()
    await open(w)
    const items = w.findAll('[role="option"]')
    expect(items[0].attributes('aria-selected')).toBe('true')
    expect(items[1].attributes('aria-selected')).toBe('false')

    await w.setProps({ modelValue: ['c2'] })
    expect(items[0].attributes('aria-selected')).toBe('false')
    expect(items[1].attributes('aria-selected')).toBe('true')
  })

  it('folds the selected count into the multi-select trigger\'s accessible name', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({
      data: [{ id: 'c1', name: 'Tech' }, { id: 'c2', name: 'Widget' }], total: 2,
    })
    const w = mountPicker({ multiple: true, modelValue: ['c1', 'c2'] }, live)
    await flushPromises()
    expect(w.get('[data-slot="combobox-trigger"]').attributes('aria-label'))
      .toBe(`${relation.label}, ${en.fields.selectedCount.replace('{n}', '2')}`)
  })

  it('uses the CJK list separator in zh-TW for the multi-select accessible name', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({
      data: [{ id: 'c1', name: 'Tech' }, { id: 'c2', name: 'Widget' }], total: 2,
    })
    const w = mountPicker({ multiple: true, modelValue: ['c1', 'c2'] }, liveZh)
    await flushPromises()
    expect(w.get('[data-slot="combobox-trigger"]').attributes('aria-label'))
      .toBe(`${relation.label}${zhTW.fields.nameListSeparator}${zhTW.fields.selectedCount.replace('{n}', '2')}`)
  })

  it('genuinely disables the multi-select trigger and every chip remove button', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({
      data: [{ id: 'c1', name: 'Tech' }], total: 1,
    })
    const w = mountPicker({ multiple: true, modelValue: ['c1'], disabled: true }, live)
    await flushPromises()
    expect(w.get('[data-slot="combobox-trigger"]').attributes('disabled')).toBe('')
    expect(w.get('[aria-label="Remove Tech"]').attributes('disabled')).toBeDefined()
  })

  // reka's resetSearchTerm() takes the `multiple` branch here (rootContext.multiple.value is true
  // because <Combobox multiple> is set), which unconditionally resets to '' regardless of what the
  // model holds — the single-select id-stringification bug above cannot occur on this branch.
  // Asserted with a preselected value, mirroring the single-select reproduction above, so this
  // isn't just "never tested the failing shape".
  it('does not poison search when the popup opens with values already selected on the multi-select branch', async () => {
    setupStores()
    const listSpy = vi.spyOn(itemsApi, 'list').mockResolvedValue({
      data: [{ id: 'c1', name: 'Tech' }, { id: 'c2', name: 'Widget' }], total: 2,
    })
    const w = mountPicker({ multiple: true, modelValue: ['c1', 'c2'] }, live)
    await flushPromises()
    listSpy.mockClear()
    vi.useFakeTimers()
    try {
      await open(w)
      await vi.advanceTimersByTimeAsync(300)
      expect((w.vm as any).search).toBe('')
      expect(listSpy).not.toHaveBeenCalled()
    } finally {
      vi.useRealTimers()
    }
  })

  it('wires a real input event on the multi-select search box through to search, debounced into one request', async () => {
    setupStores()
    const listSpy = vi.spyOn(itemsApi, 'list').mockResolvedValue({
      data: [{ id: 'c1', name: 'Tech' }, { id: 'c2', name: 'Widget' }], total: 2,
    })
    const w = mountPicker({ multiple: true, modelValue: [] }, live)
    await flushPromises()
    listSpy.mockClear()
    await open(w)
    vi.useFakeTimers()
    try {
      const input = w.get('[data-slot="command-input"]')
      ;(input.element as HTMLInputElement).value = 'wid'
      await input.trigger('input')
      expect((w.vm as any).search).toBe('wid')
      expect(listSpy).not.toHaveBeenCalled()
      await vi.advanceTimersByTimeAsync(300)
      expect(listSpy).toHaveBeenCalledTimes(1)
      expect(listSpy).toHaveBeenCalledWith('category', expect.objectContaining({ search: 'wid' }))
    } finally {
      vi.useRealTimers()
    }
  })
})
