import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '../../locales/en'
import LoginLockPanel from './LoginLockPanel.vue'
import { loginLocksApi } from '../../api/loginLocksApi'
import { ApiError } from '../../api/apiClient'

vi.mock('../../api/loginLocksApi', () => ({
  loginLocksApi: { get: vi.fn(), clear: vi.fn() },
}))
const toastAdd = vi.fn()
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ add: toastAdd }) }))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountPanel(email = 'a@x.test', heading = false) {
  return mount(LoginLockPanel, { props: { email, heading }, attachTo: document.body, global: { plugins: [i18n] } })
}
const unlockButton = (w: ReturnType<typeof mountPanel>) =>
  w.findAll('button').find((b) => b.text() === en.loginLocks.unlock)

describe('LoginLockPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(loginLocksApi.get).mockResolvedValue({ locked: false, retryAfterSeconds: 0 })
    vi.mocked(loginLocksApi.clear).mockResolvedValue(undefined)
  })

  it('reads the state of the given email on mount', async () => {
    mountPanel('a@x.test')
    await flushPromises()
    expect(loginLocksApi.get).toHaveBeenCalledWith('a@x.test')
  })

  it('says the email is not locked and offers no unlock button', async () => {
    const w = mountPanel()
    await flushPromises()
    expect(w.text()).toContain(en.loginLocks.notLocked)
    expect(unlockButton(w)).toBeUndefined()
  })

  it('shows the remaining time in seconds under a minute', async () => {
    vi.mocked(loginLocksApi.get).mockResolvedValue({ locked: true, retryAfterSeconds: 45 })
    const w = mountPanel()
    await flushPromises()
    expect(w.text()).toContain('45 s')
    expect(unlockButton(w)).toBeDefined()
  })

  it('rounds the remaining time up to whole minutes from a minute on', async () => {
    vi.mocked(loginLocksApi.get).mockResolvedValue({ locked: true, retryAfterSeconds: 61 })
    const w = mountPanel()
    await flushPromises()
    expect(w.text()).toContain('2 min')
  })

  it('unlocks, confirms with a toast and re-reads the state', async () => {
    vi.mocked(loginLocksApi.get)
      .mockResolvedValueOnce({ locked: true, retryAfterSeconds: 120 })
      .mockResolvedValueOnce({ locked: false, retryAfterSeconds: 0 })
    const w = mountPanel()
    await flushPromises()
    await unlockButton(w)!.trigger('click')
    await flushPromises()
    expect(loginLocksApi.clear).toHaveBeenCalledWith('a@x.test')
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'success', summary: en.loginLocks.unlocked }))
    expect(w.text()).toContain(en.loginLocks.notLocked)
  })

  it('announces results in a polite live region and moves focus to it after an unlock', async () => {
    vi.mocked(loginLocksApi.get)
      .mockResolvedValueOnce({ locked: true, retryAfterSeconds: 120 })
      .mockResolvedValueOnce({ locked: false, retryAfterSeconds: 0 })
    const w = mountPanel()
    const status = w.get('output')
    expect(status.attributes('aria-live')).toBe('polite')
    await flushPromises()
    expect(status.text()).toContain('2 min')

    const button = unlockButton(w)!
    ;(button.element as HTMLElement).focus()
    await button.trigger('click')
    await flushPromises()
    expect(unlockButton(w)).toBeUndefined()
    expect(status.text()).toContain(en.loginLocks.notLocked)
    expect(document.activeElement).toBe(status.element)
    expect(document.activeElement).not.toBe(document.body)
  })

  it('maps a 403 on unlock to the super-admin-only message', async () => {
    vi.mocked(loginLocksApi.get).mockResolvedValue({ locked: true, retryAfterSeconds: 120 })
    vi.mocked(loginLocksApi.clear).mockRejectedValue(new ApiError(403, 'x'))
    const w = mountPanel()
    await flushPromises()
    await unlockButton(w)!.trigger('click')
    await flushPromises()
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error', summary: en.loginLocks.superAdminOnly }))
  })

  it('reports a failed unlock and keeps the button', async () => {
    vi.mocked(loginLocksApi.get).mockResolvedValue({ locked: true, retryAfterSeconds: 120 })
    vi.mocked(loginLocksApi.clear).mockRejectedValue(new ApiError(500, 'x'))
    const w = mountPanel()
    await flushPromises()
    await unlockButton(w)!.trigger('click')
    await flushPromises()
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error', summary: en.loginLocks.unlockFailed }))
    expect(unlockButton(w)).toBeDefined()
  })

  it.each([
    [403, 'notPermitted'],
    [400, 'badEmail'],
    [500, 'loadFailed'],
  ])('maps a %i read failure to its own message', async (status, key) => {
    vi.mocked(loginLocksApi.get).mockRejectedValue(new ApiError(status, 'x'))
    const w = mountPanel()
    await flushPromises()
    expect(w.text()).toContain((en.loginLocks as Record<string, string>)[key])
    expect(unlockButton(w)).toBeUndefined()
  })

  it('reads again when the email prop changes', async () => {
    const w = mountPanel('a@x.test')
    await flushPromises()
    await w.setProps({ email: 'b@x.test' })
    await flushPromises()
    expect(loginLocksApi.get).toHaveBeenLastCalledWith('b@x.test')
  })

  it('renders the section heading only when asked', async () => {
    const without = mountPanel('a@x.test', false)
    await flushPromises()
    expect(without.find('h2').exists()).toBe(false)
    const withHeading = mountPanel('a@x.test', true)
    await flushPromises()
    expect(withHeading.get('h2').text()).toBe(en.loginLocks.title)
  })

  it('has nothing to read for a blank email', async () => {
    const w = mountPanel('')
    await flushPromises()
    expect(loginLocksApi.get).not.toHaveBeenCalled()
    expect(w.text()).not.toContain(en.loginLocks.notLocked)
  })
})
