import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import RelatedList from './RelatedList.vue'
import { itemsApi } from '../../api/itemsApi'
import { useSchemaStore } from '../../stores/schemaStore'
import { useLanguageStore } from '../../stores/languageStore'
import type { RelationMeta } from '../../types/schema'

vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en: { fields: { noRelatedItems: 'None' } } } })
const relation: RelationMeta = {
  name: 'articles', label: 'Articles', kind: 'oneToMany', targetCollection: 'article', interface: 'relatedList',
  foreignKey: 'CategoryId', displayTemplate: null, editable: false, selfReferencing: false,
}

type ListResult = { data: Array<{ id: string }>; total: number }

describe('RelatedList load ordering', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
    useSchemaStore().get = vi.fn().mockReturnValue(undefined) as never
    useLanguageStore().languages = [{ code: 'en', name: 'English', isDefault: true }]
  })

  function setup() {
    const pending: Array<{ resolve: (r: ListResult) => void; reject: (e: unknown) => void }> = []
    vi.spyOn(itemsApi, 'list').mockImplementation(
      () => new Promise<ListResult>((resolve, reject) => { pending.push({ resolve, reject }) }) as never,
    )
    const w = mount(RelatedList, { props: { relation, parentId: 'p1' }, global: { stubs: { DataTable: true, DataTablePagination: true }, plugins: [i18n] } })
    return { pending, vm: w.vm as any }
  }

  it('keeps the newest page when an earlier load resolves last', async () => {
    const { pending, vm } = setup() // pending[0] is the mount load
    void vm.load()
    pending[1].resolve({ data: [{ id: 'new' }], total: 1 })
    await flushPromises()
    pending[0].resolve({ data: [{ id: 'old' }, { id: 'old2' }], total: 2 })
    await flushPromises()
    expect(vm.rows.map((r: { id: string }) => r.id)).toEqual(['new'])
    expect(vm.total).toBe(1)
  })

  it('a stale load neither clears loading nor reports an error', async () => {
    const { pending, vm } = setup()
    void vm.load()
    pending[0].reject(new Error('stale boom'))
    await flushPromises()
    expect(vm.error).toBe('')
    expect(vm.loading).toBe(true)
    pending[1].resolve({ data: [], total: 0 })
    await flushPromises()
    expect(vm.loading).toBe(false)
  })
})
