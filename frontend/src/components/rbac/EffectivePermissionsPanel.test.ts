import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '../../locales/en'
import EffectivePermissionsPanel from './EffectivePermissionsPanel.vue'
import { useSchemaStore } from '../../stores/schemaStore'
import { rbacApi } from '../../api/rbacApi'

vi.mock('../../api/rbacApi', () => ({
  rbacApi: { getEffectivePermissions: vi.fn() },
}))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountPanel() {
  return mount(EffectivePermissionsPanel, {
    props: { userId: 'u1' },
    global: { plugins: [i18n] },
  })
}

describe('EffectivePermissionsPanel', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    useSchemaStore().collections = [
      { name: 'article', label: 'Article', fields: [], relations: [] },
    ] as any
  })

  it('renders the merged grants with collection labels', async () => {
    vi.mocked(rbacApi.getEffectivePermissions).mockResolvedValue({
      isSuperAdmin: false,
      permissions: { article: { read: true, write: true, delete: false } },
    })
    const w = mountPanel()
    await flushPromises()
    expect(w.text()).toContain('Article')
    expect(rbacApi.getEffectivePermissions).toHaveBeenCalledWith('u1')
  })

  it('super-admin users get a notice instead of a table', async () => {
    vi.mocked(rbacApi.getEffectivePermissions).mockResolvedValue({
      isSuperAdmin: true, permissions: {},
    })
    const w = mountPanel()
    await flushPromises()
    expect(w.find('table').exists()).toBe(false)
    expect(w.text()).toContain('super admin')
  })

  it('reload() refetches', async () => {
    vi.mocked(rbacApi.getEffectivePermissions).mockResolvedValue({
      isSuperAdmin: false, permissions: {},
    })
    const w = mountPanel()
    await flushPromises()
    await (w.vm as any).reload()
    expect(rbacApi.getEffectivePermissions).toHaveBeenCalledTimes(2)
  })
})
