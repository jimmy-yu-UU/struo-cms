import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '../../locales/en'
import EffectivePermissionsPanel from './EffectivePermissionsPanel.vue'
import { useSchemaStore } from '../../stores/schemaStore'
import { rbacApi, type EffectivePermissions } from '../../api/rbacApi'

vi.mock('../../api/rbacApi', () => ({ rbacApi: { getEffectivePermissions: vi.fn() } }))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
const grants = (name: string): EffectivePermissions => ({
  isSuperAdmin: false, permissions: { [name]: { read: true, write: false, delete: false } },
})

describe('EffectivePermissionsPanel reload ordering', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    useSchemaStore().collections = [
      { name: 'article', label: 'Article', fields: [], relations: [] },
      { name: 'page', label: 'Page', fields: [], relations: [] },
    ] as any
  })

  function setup() {
    const pending: Array<{ resolve: (v: EffectivePermissions) => void; reject: (e: unknown) => void }> = []
    vi.mocked(rbacApi.getEffectivePermissions).mockImplementation(
      () => new Promise<EffectivePermissions>((resolve, reject) => { pending.push({ resolve, reject }) }),
    )
    const w = mount(EffectivePermissionsPanel, { props: { userId: 'u1' }, global: { plugins: [i18n] } })
    return { w, pending }
  }

  it('renders the newest reload when an earlier one resolves last', async () => {
    const { w, pending } = setup()
    void (w.vm as any).reload()
    pending[1].resolve(grants('page'))
    await flushPromises()
    pending[0].resolve(grants('article'))
    await flushPromises()
    expect(w.text()).toContain('Page')
    expect(w.text()).not.toContain('Article')
  })

  it('ignores a stale failure that arrives after a newer success', async () => {
    const { w, pending } = setup()
    void (w.vm as any).reload()
    pending[1].resolve(grants('page'))
    await flushPromises()
    pending[0].reject(new Error('boom'))
    await flushPromises()
    expect(w.text()).toContain('Page')
    expect(w.text()).not.toContain(en.rbac.effectiveLoadFailed)
  })
})
