import { describe, it, expect, vi, beforeEach } from 'vitest'
import { reactive } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en'
import zhTW from '@/locales/zh-TW'
import CollectionListView from './CollectionListView.vue'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { useLanguageStore } from '../stores/languageStore'
import { itemsApi } from '../api/itemsApi'

const pushMock = vi.fn()
const mockRoute = reactive({ params: { name: 'article' } })
vi.mock('vue-router', () => ({
  useRoute: () => mockRoute,
  useRouter: () => ({ push: pushMock }),
}))
vi.mock('../api/itemsApi', () => ({ itemsApi: { list: vi.fn(), remove: vi.fn(), restore: vi.fn() } }))
vi.mock('../api/schemaApi', () => ({ schemaApi: { getAll: vi.fn(), get: vi.fn() } }))
vi.mock('../api/loginLocksApi', () => ({ loginLocksApi: { query: vi.fn().mockResolvedValue([]), clear: vi.fn() } }))
vi.mock('@/composables/useConfirm', () => ({ useConfirm: () => ({ require: vi.fn() }) }))

function makeI18n(locale: 'en' | 'zh-TW') {
  return createI18n({ legacy: false, locale, fallbackLocale: 'en', messages: { en, 'zh-TW': zhTW } })
}

function seed(): void {
  const schema = useSchemaStore()
  schema.collections = [{
    name: 'article', label: 'Article', defaultDisplayField: 'title', relations: [],
    fields: [{ name: 'title', label: 'Title', interface: 'text', required: false, searchable: false,
      sortable: true, readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false }],
  }]
  schema.loaded = true
  const lang = useLanguageStore()
  lang.load = vi.fn().mockResolvedValue(undefined)
  lang.languages = [{ code: 'en', name: 'English', isDefault: true }]
  useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
  vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ id: '1', title: 'Hello' }], total: 1 })
}

async function mountView(locale: 'en' | 'zh-TW') {
  const w = mount(CollectionListView, {
    global: { plugins: [makeI18n(locale)], stubs: { teleport: true }, renderStubDefaultSlot: true },
  })
  await flushPromises()
  return w
}

describe('CollectionListView actions column label', () => {
  beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks(); seed() })

  it('names the actions column in the header and the column menu (zh-TW)', async () => {
    const w = await mountView('zh-TW')
    expect(zhTW.collectionList.actions).toBe('操作')
    const headers = w.findAll('th').map((th) => th.text())
    expect(headers).toContain(zhTW.collectionList.actions)
    await w.get('[data-testid="column-visibility-trigger"]').trigger('click')
    await flushPromises()
    const items = w.findAll('[role="menuitemcheckbox"]').map((el) => el.text())
    expect(items).toContain(zhTW.collectionList.actions)
    expect(items).not.toContain('actions')
  })

  it('names the actions column in English', async () => {
    const w = await mountView('en')
    expect(w.findAll('th').map((th) => th.text())).toContain(en.collectionList.actions)
  })
})
