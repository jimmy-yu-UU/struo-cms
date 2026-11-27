import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import RelatedList from './RelatedList.vue'
import { itemsApi } from '../../api/itemsApi'
import { useSchemaStore } from '../../stores/schemaStore'
import { useLanguageStore } from '../../stores/languageStore'
import type { RelationMeta, CollectionMeta } from '../../types/schema'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: {
    fields: { noRelatedItems: 'No related items.' }, common: { loading: 'Loading…' },
    collectionList: { range: 'Showing {from}–{to} of {total}' },
  } },
})

const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
}))

const targetMeta: CollectionMeta = {
  name: 'article',
  label: 'Article',
  defaultDisplayField: null,
  fields: [
    { name: 'title', label: 'Title', interface: 'text', required: false, searchable: false, sortable: false,
      readOnly: false, hidden: false, translatable: true, sort: 0, isSystem: false },
  ],
  relations: [],
}

const relation: RelationMeta = {
  name: 'articles',
  label: 'Articles',
  kind: 'oneToMany',
  targetCollection: 'article',
  interface: 'relatedList',
  foreignKey: 'CategoryId',
  displayTemplate: '{Title}',
  editable: false,
  selfReferencing: false,
}

const stubs = {
  DataTable: true,
  DataTablePagination: true,
}

function setupStores() {
  const schema = useSchemaStore()
  schema.get = vi.fn().mockReturnValue(targetMeta) as never
  const lang = useLanguageStore()
  lang.languages = [{ code: 'en', name: 'English', isDefault: true }]
  return { schema, lang }
}

describe('RelatedList', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
    push.mockReset()
  })

  it('filters by foreignKey _eq parentId', async () => {
    setupStores()
    const listSpy = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [{ id: 'a1', title: 'Hello' }], total: 1 })
    const w = mount(RelatedList, { props: { relation, parentId: 'p1' }, global: { stubs, plugins: [i18n] } })
    await (w.vm as any).load()
    expect(listSpy).toHaveBeenCalledWith(
      'article',
      expect.objectContaining({ filter: { categoryId: { op: '_eq', value: 'p1' } } }),
    )
    expect((w.vm as any).rows).toHaveLength(1)
    expect((w.vm as any).total).toBe(1)
  })

  it('filters through the relation back to the parent when the child declares one', async () => {
    const { schema } = setupStores()
    schema.get = vi.fn().mockReturnValue({
      ...targetMeta,
      relations: [{
        name: 'category', label: 'Category', kind: 'manyToOne', targetCollection: 'category', interface: 'dropdown',
        foreignKey: 'categoryId', editable: true, selfReferencing: false,
      }],
    }) as never
    const listSpy = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mount(RelatedList, {
      props: { relation, parentId: 'p1', parentCollection: 'category' }, global: { stubs, plugins: [i18n] },
    })
    await (w.vm as any).load()
    expect(listSpy).toHaveBeenCalledWith(
      'article',
      expect.objectContaining({ filter: { 'category.id': { op: '_eq', value: 'p1' } } }),
    )
  })

  it('filters by the key when the child declares no relation back to the parent', async () => {
    setupStores()
    const listSpy = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mount(RelatedList, {
      props: { relation, parentId: 'p1', parentCollection: 'category' }, global: { stubs, plugins: [i18n] },
    })
    await (w.vm as any).load()
    expect(listSpy).toHaveBeenCalledWith(
      'article',
      expect.objectContaining({ filter: { categoryId: { op: '_eq', value: 'p1' } } }),
    )
  })

  it('does not query when parentId is absent (create mode)', async () => {
    setupStores()
    const listSpy = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mount(RelatedList, { props: { relation, parentId: undefined }, global: { stubs, plugins: [i18n] } })
    await (w.vm as any).load()
    expect(listSpy).not.toHaveBeenCalled()
  })

  it('sets error message when the load fails', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockRejectedValue(new Error('boom'))
    const w = mount(RelatedList, { props: { relation, parentId: 'p1' }, global: { stubs, plugins: [i18n] } })
    await (w.vm as any).load()
    expect((w.vm as any).error).toBe('boom')
    expect((w.vm as any).loading).toBe(false)
  })

  it('feeds DataTable a single label column and the row set', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({
      data: [{ id: 'a1', title: 'Hello' }, { id: 'a2', title: 'World' }],
      total: 2,
    })
    const w = mount(RelatedList, { props: { relation, parentId: 'p1' }, global: { stubs, plugins: [i18n] } })
    await flushPromises()
    const table = w.findComponent({ name: 'DataTable' })
    expect(table.props('rows')).toHaveLength(2)
    expect((table.props('columns') as { id: string }[]).map((c) => c.id)).toEqual(['label'])
    // No sorting: this list has never had a sortable column and the backend query it builds does
    // not carry a sort param, so offering a clickable header would sort nothing.
    expect(table.props('state')).toMatchObject({ sort: [], page: 0 })
  })

  it('turns a pagination page event into a reload', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mount(RelatedList, { props: { relation, parentId: 'p1' }, global: { stubs, plugins: [i18n] } })
    await flushPromises()
    vi.mocked(itemsApi.list).mockClear()
    w.findComponent({ name: 'DataTablePagination' }).vm.$emit('update:page', 2)
    await flushPromises()
    expect(itemsApi.list).toHaveBeenCalledWith(
      'article',
      expect.objectContaining({ page: 2, rows: 10 }),
    )
  })

  // update:page-size only ever fires from DataTablePagination's rows-per-page control, which the
  // component keeps unrendered via :show-page-size-selector="false" -- unreachable from the real
  // UI today, but still a live listener on the emitted event, so it is worth pinning directly.
  it('turns a pagination page-size event into a reload, reset to page 0', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mount(RelatedList, { props: { relation, parentId: 'p1' }, global: { stubs, plugins: [i18n] } })
    await flushPromises()
    ;(w.vm as any).onPage(2)
    await flushPromises()
    vi.mocked(itemsApi.list).mockClear()
    w.findComponent({ name: 'DataTablePagination' }).vm.$emit('update:pageSize', 25)
    await flushPromises()
    expect(itemsApi.list).toHaveBeenCalledWith(
      'article',
      expect.objectContaining({ page: 0, rows: 25 }),
    )
  })

  // DataTable has no row-click event, so the label cell itself must carry navigation. Mounting
  // the real DataTable, rather than stubbing it,
  // exercises the actual cell render function and the actual DOM click through Button's
  // real onClick -- a reordered h() prop, a renamed handler, or a dropped .original would show up
  // here even though every other test in this file stubs DataTable out.
  it('clicking the row label button navigates to the target record', async () => {
    setupStores()
    // title is translatable (targetMeta above), so resolveDisplayLabel reads it via
    // translations.en rather than the bare `title` property.
    vi.spyOn(itemsApi, 'list').mockResolvedValue({
      data: [{ id: 'a1', translations: { en: { title: 'Hello' } } }],
      total: 1,
    })
    const w = mount(RelatedList, {
      props: { relation, parentId: 'p1' },
      global: { stubs: { DataTablePagination: true }, plugins: [i18n] },
    })
    await flushPromises()
    const button = w.get('[data-testid="related-row"]')
    expect(button.text()).toBe('Hello')
    // A bare <button> defaults to type="submit"; this renders inside ItemForm's <form>, where that
    // default would turn a row click into a full record save.
    expect(button.attributes('type')).toBe('button')
    await button.trigger('click')
    expect(push).toHaveBeenCalledWith({ name: 'collection-item', params: { name: 'article', id: 'a1' } })
  })

  it('shows no range text before the first total arrives, and the range after it', async () => {
    setupStores()
    let resolveList: (v: { data: never[]; total: number }) => void = () => {}
    vi.spyOn(itemsApi, 'list').mockReturnValue(new Promise((r) => { resolveList = r }))
    const w = mount(RelatedList, {
      props: { relation, parentId: 'p1' },
      global: { stubs: { DataTable: true }, plugins: [i18n] },
    })
    const pager = () => w.findComponent({ name: 'DataTablePagination' })
    expect(pager().props('ready')).toBe(false)
    expect(w.text()).not.toContain('Showing')
    resolveList({ data: [], total: 7 })
    await flushPromises()
    expect(pager().props('ready')).toBe(true)
    expect(w.text()).toContain('Showing 1–7 of 7')
  })

  it('stays not ready when the first load fails', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockRejectedValue(new Error('boom'))
    const w = mount(RelatedList, { props: { relation, parentId: 'p1' }, global: { stubs, plugins: [i18n] } })
    await flushPromises()
    expect(w.findComponent({ name: 'DataTablePagination' }).props('ready')).toBe(false)
  })

  it('is not ready, and reloads, when the parent changes', async () => {
    setupStores()
    const list = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 3 })
    const w = mount(RelatedList, { props: { relation, parentId: 'p1' }, global: { stubs, plugins: [i18n] } })
    await flushPromises()
    expect(w.findComponent({ name: 'DataTablePagination' }).props('ready')).toBe(true)
    let resolveNext: (v: { data: never[]; total: number }) => void = () => {}
    list.mockReturnValue(new Promise((r) => { resolveNext = r }))
    await w.setProps({ parentId: 'p2' })
    expect(w.findComponent({ name: 'DataTablePagination' }).props('ready')).toBe(false)
    expect(list).toHaveBeenLastCalledWith('article', expect.objectContaining({
      filter: { categoryId: { op: '_eq', value: 'p2' } }, page: 0,
    }))
    resolveNext({ data: [], total: 9 })
    await flushPromises()
    expect(w.findComponent({ name: 'DataTablePagination' }).props('ready')).toBe(true)
  })

  it('clears the previous parent rows and total when the parent changes', async () => {
    setupStores()
    const list = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [{ id: 'a1', title: 'One' }], total: 1 })
    const w = mount(RelatedList, { props: { relation, parentId: 'p1' }, global: { stubs, plugins: [i18n] } })
    await flushPromises()
    expect((w.vm as unknown as { rows: unknown[] }).rows).toHaveLength(1)
    list.mockReturnValue(new Promise(() => {}))
    await w.setProps({ parentId: 'p2' })
    expect((w.vm as unknown as { rows: unknown[] }).rows).toHaveLength(0)
    expect((w.vm as unknown as { total: number }).total).toBe(0)
  })

  it('ignores a response for the previous parent after the parent changes, even to an empty parent', async () => {
    setupStores()
    let resolveOld: (v: { data: { id: string; title: string }[]; total: number }) => void = () => {}
    vi.spyOn(itemsApi, 'list').mockReturnValue(new Promise((r) => { resolveOld = r }))
    const w = mount(RelatedList, { props: { relation, parentId: 'p1' }, global: { stubs, plugins: [i18n] } })
    await w.setProps({ parentId: undefined })
    resolveOld({ data: [{ id: 'a1', title: 'Old' }], total: 1 })
    await flushPromises()
    expect((w.vm as unknown as { rows: unknown[] }).rows).toHaveLength(0)
    expect((w.vm as unknown as { total: number }).total).toBe(0)
  })

  it('is loading from the first render, so the table never flashes its empty message', async () => {
    setupStores()
    let resolveList: (v: { data: never[]; total: number }) => void = () => {}
    vi.spyOn(itemsApi, 'list').mockReturnValue(new Promise((r) => { resolveList = r }))
    const w = mount(RelatedList, { props: { relation, parentId: 'p1' }, global: { stubs, plugins: [i18n] } })
    expect(w.findComponent({ name: 'DataTable' }).props('loading')).toBe(true)
    resolveList({ data: [], total: 0 })
    await flushPromises()
    expect(w.findComponent({ name: 'DataTable' }).props('loading')).toBe(false)
  })
})
