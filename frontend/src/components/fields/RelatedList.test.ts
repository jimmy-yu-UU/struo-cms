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
  messages: { en: { fields: { noRelatedItems: 'No related items.' }, common: { loading: 'Loading…' } } },
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

  it('onPage updates paging state and reloads', async () => {
    setupStores()
    const listSpy = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mount(RelatedList, { props: { relation, parentId: 'p1' }, global: { stubs, plugins: [i18n] } })
    await (w.vm as any).load()
    listSpy.mockClear()
    // onPage takes a plain page number: DataTablePagination emits update:page and update:pageSize
    // as two independent numeric events, never a combined { page, rows } payload.
    await (w.vm as any).onPage(2)
    expect(listSpy).toHaveBeenCalledWith('article', expect.objectContaining({ page: 2, rows: 10 }))
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

  // update:page-size only ever fires from DataTablePagination's rows-per-page <select>, which the
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

  // The migration's whole point is this button: DataTable has no row-click event, so the label
  // cell itself must carry navigation. Mounting the real (plan-1) DataTable, rather than stubbing
  // it, exercises the actual cell render function and the actual DOM click through Button's
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
})
