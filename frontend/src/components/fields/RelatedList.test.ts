import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import RelatedList from './RelatedList.vue'
import { itemsApi } from '../../api/itemsApi'
import { useSchemaStore } from '../../stores/schemaStore'
import { useLanguageStore } from '../../stores/languageStore'
import type { RelationMeta, CollectionMeta } from '../../types/schema'

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
  Column: true,
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
    const w = mount(RelatedList, { props: { relation, parentId: 'p1' }, global: { stubs } })
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
    const w = mount(RelatedList, { props: { relation, parentId: undefined }, global: { stubs } })
    await (w.vm as any).load()
    expect(listSpy).not.toHaveBeenCalled()
  })

  it('sets error message when the load fails', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockRejectedValue(new Error('boom'))
    const w = mount(RelatedList, { props: { relation, parentId: 'p1' }, global: { stubs } })
    await (w.vm as any).load()
    expect((w.vm as any).error).toBe('boom')
    expect((w.vm as any).loading).toBe(false)
  })

  it('onPage updates paging state and reloads', async () => {
    setupStores()
    const listSpy = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mount(RelatedList, { props: { relation, parentId: 'p1' }, global: { stubs } })
    await (w.vm as any).load()
    listSpy.mockClear()
    await (w.vm as any).onPage({ page: 2, rows: 20 })
    expect(listSpy).toHaveBeenCalledWith('article', expect.objectContaining({ page: 2, rows: 20 }))
  })
})
