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
  messages: { en: { fields: { noRelatedItems: 'No related items.' } } },
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
    // DataTablePagination emits two independent events (update:page / update:pageSize) instead of
    // PrimeVue's single { page, rows } payload, so onPage is now a plain page-number setter.
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
})
