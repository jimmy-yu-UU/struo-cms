import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '../../locales/en'
import RoleCapabilitiesPanel from './RoleCapabilitiesPanel.vue'
import { rbacApi } from '../../api/rbacApi'

vi.mock('../../api/rbacApi', () => ({
  rbacApi: { listCapabilities: vi.fn(), getRoleCapabilities: vi.fn(), putRoleCapabilities: vi.fn() },
}))
const toastAdd = vi.fn()
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ add: toastAdd }) }))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

const CATALOG = [
  { code: 'login.unlock', labelKey: 'capabilities.login.unlock.label', descriptionKey: 'capabilities.login.unlock.description' },
  { code: 'fork.thing', labelKey: 'capabilities.fork.thing.label', descriptionKey: 'capabilities.fork.thing.description' },
]

function mountPanel() {
  return mount(RoleCapabilitiesPanel, { props: { roleId: 'r1' }, global: { plugins: [i18n] } })
}
const boxes = (w: ReturnType<typeof mountPanel>) => w.findAll('[data-slot="checkbox"]')
const saveButton = (w: ReturnType<typeof mountPanel>) =>
  w.findAll('button').find((b) => b.text() === en.rbac.capabilitiesSave)!

describe('RoleCapabilitiesPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(rbacApi.listCapabilities).mockResolvedValue(CATALOG)
    vi.mocked(rbacApi.getRoleCapabilities).mockResolvedValue(['login.unlock'])
    vi.mocked(rbacApi.putRoleCapabilities).mockImplementation(async (_id, codes) => codes)
  })

  it('lists every catalog entry with its translated label and description, ticking the held ones', async () => {
    const w = mountPanel()
    await flushPromises()
    expect(rbacApi.getRoleCapabilities).toHaveBeenCalledWith('r1')
    expect(w.text()).toContain(en.capabilities.login.unlock.label)
    expect(w.text()).toContain(en.capabilities.login.unlock.description)
    expect(boxes(w)).toHaveLength(2)
    expect(boxes(w)[0].attributes('data-state')).toBe('checked')
    expect(boxes(w)[1].attributes('data-state')).toBe('unchecked')
  })

  it('falls back to the code when the locale has no label for a capability', async () => {
    const w = mountPanel()
    await flushPromises()
    expect(w.text()).toContain('fork.thing')
  })

  it('keeps Save disabled until a box changes, then PUTs the full selection', async () => {
    const w = mountPanel()
    await flushPromises()
    expect(saveButton(w).attributes('disabled')).toBeDefined()

    await boxes(w)[1].trigger('click')
    expect(saveButton(w).attributes('disabled')).toBeUndefined()

    await saveButton(w).trigger('click')
    await flushPromises()
    expect(rbacApi.putRoleCapabilities).toHaveBeenCalledWith('r1', ['fork.thing', 'login.unlock'])
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'success', summary: en.rbac.capabilitiesSaved }))
    expect(saveButton(w).attributes('disabled')).toBeDefined()
  })

  it('can clear every capability', async () => {
    const w = mountPanel()
    await flushPromises()
    await boxes(w)[0].trigger('click')
    await saveButton(w).trigger('click')
    await flushPromises()
    expect(rbacApi.putRoleCapabilities).toHaveBeenCalledWith('r1', [])
  })

  it('reports a failed save and stays dirty', async () => {
    vi.mocked(rbacApi.putRoleCapabilities).mockRejectedValue(new Error('boom'))
    const w = mountPanel()
    await flushPromises()
    await boxes(w)[1].trigger('click')
    await saveButton(w).trigger('click')
    await flushPromises()
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error', summary: en.rbac.capabilitiesSaveFailed }))
    expect((w.vm as unknown as { dirty: boolean }).dirty).toBe(true)
  })

  it('shows a load error instead of the list when the catalog cannot be read', async () => {
    vi.mocked(rbacApi.listCapabilities).mockRejectedValue(new Error('x'))
    const w = mountPanel()
    await flushPromises()
    expect(w.text()).toContain(en.rbac.capabilitiesLoadFailed)
    expect(boxes(w)).toHaveLength(0)
  })

  it('says so when the catalog is empty', async () => {
    vi.mocked(rbacApi.listCapabilities).mockResolvedValue([])
    const w = mountPanel()
    await flushPromises()
    expect(w.text()).toContain(en.rbac.capabilitiesEmpty)
  })
})
