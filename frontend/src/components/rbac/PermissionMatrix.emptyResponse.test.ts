import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '../../locales/en'
import PermissionMatrix from './PermissionMatrix.vue'
import { useSchemaStore } from '../../stores/schemaStore'
import { apiClient } from '../../api/apiClient'

// The real rbacApi over a stubbed client: a save the server committed with no body must not read as a failure.
vi.mock('../../api/apiClient', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/apiClient')>()),
  apiClient: { get: vi.fn(), putEnveloped: vi.fn() },
}))

const toastAdd = vi.fn()
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ add: toastAdd }) }))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

describe('PermissionMatrix when the save answers without a body', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    useSchemaStore().collections = [{ name: 'article', label: 'Article', group: 'Content', fields: [], relations: [] }] as never
    vi.mocked(apiClient.get).mockResolvedValue([])
  })

  it('keeps what is on screen, reports success and hands no version to the form', async () => {
    vi.mocked(apiClient.putEnveloped).mockResolvedValue({ data: undefined, meta: undefined })
    const w = mount(PermissionMatrix, {
      props: { roleId: 'r1', isSuperAdminRole: false, version: 3 }, global: { plugins: [i18n] },
    })
    await flushPromises()
    const vm = w.vm as unknown as { toggle: (n: string, k: string, v: boolean) => void; save: () => Promise<boolean>; dirty: boolean }
    vm.toggle('article', 'write', true)
    expect(await vm.save()).toBe(true)
    expect(vm.dirty).toBe(false)
    expect(w.emitted('saved')).toBeUndefined()
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'success' }))
    expect(w.find('[aria-label="Article — Write"]').attributes('data-state')).toBe('checked')
  })
})
