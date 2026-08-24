import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import ChangePasswordDialog from './ChangePasswordDialog.vue'
import { usersApi } from '@/api/usersApi'
import { ApiError } from '@/api/apiClient'
import { useAuthStore } from '@/stores/authStore'
import { useAppConfigStore } from '@/stores/appConfigStore'
import en from '@/locales/en'

// Mocked the same way MediaDetailDialog.test.ts mocks it: useToast's real backend is vue-sonner,
// which has no jsdom-friendly surface worth exercising here -- only the { severity, summary }
// shape this component hands it matters.
const toastAdd = vi.fn()
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ add: toastAdd }) }))

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

// Mirrors vue-i18n's own {token} interpolation well enough to build the exact expected string from
// the real locale module, so these assertions fail if en.ts's wording changes without also breaking
// if the {min}/{seconds} placeholder happens to move around in the sentence.
function interpolate(template: string, params: Record<string, unknown>): string {
  return template.replace(/\{(\w+)\}/g, (_, key: string) => String(params[key]))
}

function mountDialog(targetUserId: string) {
  return mount(ChangePasswordDialog, {
    props: { open: true, targetUserId },
    global: {
      plugins: [i18n],
      // reka's DialogPortal is itself named Teleport and collides with VTU's own `teleport` stub,
      // which would otherwise drop the whole dialog body -- same fix as MediaDetailDialog.test.ts.
      stubs: { teleport: true },
      renderStubDefaultSlot: true,
    },
  })
}

// The DOM order of the password <input>s is stable and asserted on directly (current, then new,
// then confirm, current only present in self mode) rather than by some other selector, matching the
// index-based convention MediaDetailDialog.test.ts already uses for its own Input fields.
async function fillAndSubmit(w: ReturnType<typeof mountDialog>, values: string[]): Promise<void> {
  const inputs = w.findAll('input')
  for (let i = 0; i < values.length; i++) await inputs[i].setValue(values[i])
  await w.get('form').trigger('submit')
  await flushPromises()
}

describe('ChangePasswordDialog', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
    toastAdd.mockClear()
    const auth = useAuthStore()
    auth.user = { id: 'self-id', isSuperAdmin: false, permissions: {} }
    Object.defineProperty(navigator, 'clipboard', {
      value: { writeText: vi.fn().mockResolvedValue(undefined) },
      configurable: true,
    })
  })

  it('asks for the current password when the target is the signed-in user', async () => {
    const w = mountDialog('self-id')
    await flushPromises()
    const inputs = w.findAll('input')
    expect(inputs).toHaveLength(3)
    expect(inputs[0].attributes('autocomplete')).toBe('current-password')
    expect(w.text()).toContain(en.password.current)
  })

  it('asks for the current password when a super-admin opens their own row (isSelf never consults isSuperAdmin)', async () => {
    const auth = useAuthStore()
    auth.user = { id: 'self-id', isSuperAdmin: true, permissions: {} }
    const w = mountDialog('self-id')
    await flushPromises()
    const inputs = w.findAll('input')
    expect(inputs).toHaveLength(3)
    expect(inputs[0].attributes('autocomplete')).toBe('current-password')
    expect(w.text()).toContain(en.password.current)
  })

  it('does not ask for the current password when the target is someone else', async () => {
    const w = mountDialog('other-id')
    await flushPromises()
    const inputs = w.findAll('input')
    expect(inputs).toHaveLength(2)
    expect(inputs.some((i) => i.attributes('autocomplete') === 'current-password')).toBe(false)
    const buttons = w.findAll('button')
    expect(buttons.some((b) => b.text() === en.password.generate)).toBe(true)
  })

  it('blocks submit in self mode when the current password is left blank', async () => {
    // The endpoint rate-limits per authenticated user and treats a blank currentPassword
    // identically to a wrong one, so this must be caught locally rather than spent as one of a
    // small number of attempts.
    const spy = vi.spyOn(usersApi, 'changePassword')
    const w = mountDialog('self-id')
    await flushPromises()
    await fillAndSubmit(w, ['', 'newpassword1', 'newpassword1'])
    expect(spy).not.toHaveBeenCalled()
    expect(w.text()).toContain(en.password.currentRequired)
  })

  it('blocks submit when the two new-password fields differ', async () => {
    const spy = vi.spyOn(usersApi, 'changePassword')
    const w = mountDialog('other-id')
    await flushPromises()
    await fillAndSubmit(w, ['abcdefgh', 'abcdefgi'])
    expect(spy).not.toHaveBeenCalled()
    expect(w.text()).toContain(en.password.mismatch)
  })

  it('blocks submit when the new password is shorter than passwordMinLength', async () => {
    const appConfig = useAppConfigStore()
    appConfig.passwordMinLength = 12
    const spy = vi.spyOn(usersApi, 'changePassword')
    const w = mountDialog('other-id')
    await flushPromises()
    const eleven = 'a'.repeat(11)
    await fillAndSubmit(w, [eleven, eleven])
    expect(spy).not.toHaveBeenCalled()
    expect(w.text()).toContain(interpolate(en.password.tooShort, { min: 12 }))
  })

  it('sends currentPassword in self mode and omits it in admin mode', async () => {
    const spy = vi.spyOn(usersApi, 'changePassword').mockResolvedValue(undefined)

    const wSelf = mountDialog('self-id')
    await flushPromises()
    await fillAndSubmit(wSelf, ['oldpassword1', 'newpassword1', 'newpassword1'])
    expect(spy).toHaveBeenCalledTimes(1)
    expect(spy.mock.calls[0][0]).toBe('self-id')
    // toStrictEqual (unlike toEqual/toHaveBeenCalledWith's non-strict `equals`) treats a key that is
    // absent as different from one present with value undefined, so this also proves currentPassword
    // is really there, not merely tolerated.
    expect(spy.mock.calls[0][1]).toStrictEqual({ newPassword: 'newpassword1', currentPassword: 'oldpassword1' })
    wSelf.unmount()

    spy.mockClear()
    const wAdmin = mountDialog('other-id')
    await flushPromises()
    await fillAndSubmit(wAdmin, ['newpassword1', 'newpassword1'])
    expect(spy).toHaveBeenCalledTimes(1)
    const adminBody = spy.mock.calls[0][1]
    expect('currentPassword' in adminBody).toBe(false)
    expect(adminBody).toStrictEqual({ newPassword: 'newpassword1' })
  })

  it('puts INVALID_CURRENT_PASSWORD on the current-password field, not in a toast', async () => {
    vi.spyOn(usersApi, 'changePassword').mockRejectedValue(new ApiError(400, 'x', 'INVALID_CURRENT_PASSWORD'))
    const w = mountDialog('self-id')
    await flushPromises()
    await fillAndSubmit(w, ['wrongcurrent', 'newpassword1', 'newpassword1'])
    expect(w.text()).toContain(en.password.invalidCurrent)
    expect(toastAdd).not.toHaveBeenCalled()
  })

  it('puts a policy BAD_USER_INPUT on the new-password field', async () => {
    // BAD_USER_INPUT covers every PasswordPolicy.Validate rejection, not only "too short" -- e.g. a
    // too-long password, whose MaxLength policy value is deliberately never published to this
    // client (see PasswordPolicy.cs), so the server's own message is the only place that reason
    // lives. Asserting a fixed "too short" cause here regardless of the server's actual message
    // would be exactly the defect this test exists to catch: a wrong message next to a too-long
    // password. The field must show the server's own e.message, not a hardcoded assumed cause.
    const serverMessage = 'Password must be at most 128 characters.'
    vi.spyOn(usersApi, 'changePassword').mockRejectedValue(new ApiError(400, serverMessage, 'BAD_USER_INPUT'))
    const w = mountDialog('other-id')
    await flushPromises()
    await fillAndSubmit(w, ['newpassword1', 'newpassword1'])
    expect(w.find('[role="alert"]').exists()).toBe(true)
    expect(w.text()).toContain(serverMessage)
    expect(toastAdd).not.toHaveBeenCalled()
  })

  it('falls back to the composed too-short message on a BAD_USER_INPUT with no server message', async () => {
    vi.spyOn(usersApi, 'changePassword').mockRejectedValue(new ApiError(400, '', 'BAD_USER_INPUT'))
    const w = mountDialog('other-id')
    await flushPromises()
    await fillAndSubmit(w, ['newpassword1', 'newpassword1'])
    expect(w.text()).toContain(interpolate(en.password.tooShort, { min: 8 }))
  })

  it('shows NO_LOCAL_PASSWORD, FORBIDDEN and NOT_FOUND as toasts', async () => {
    const cases: Array<[string, string]> = [
      ['NO_LOCAL_PASSWORD', en.password.noLocalPassword],
      ['FORBIDDEN', en.password.forbidden],
      ['NOT_FOUND', en.password.notFound],
    ]
    for (const [code, message] of cases) {
      toastAdd.mockClear()
      vi.spyOn(usersApi, 'changePassword').mockRejectedValueOnce(new ApiError(400, 'x', code))
      const w = mountDialog('other-id')
      await flushPromises()
      await fillAndSubmit(w, ['newpassword1', 'newpassword1'])
      expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error', summary: message }))
      w.unmount()
    }
  })

  it('shows the wait time on a 429', async () => {
    vi.spyOn(usersApi, 'changePassword').mockRejectedValue(new ApiError(429, 'x', 'TOO_MANY_REQUESTS', undefined, 30))
    const w = mountDialog('other-id')
    await flushPromises()
    await fillAndSubmit(w, ['newpassword1', 'newpassword1'])
    const expected = interpolate(en.errors.tooManyRequestsWithWait, { seconds: 30 })
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error', summary: expected }))
  })

  it('generates a password into the field and copies it to the clipboard', async () => {
    const w = mountDialog('other-id')
    await flushPromises()
    const generateButton = w.findAll('button').find((b) => b.text() === en.password.generate)
    expect(generateButton).toBeTruthy()
    await generateButton!.trigger('click')
    await flushPromises()
    const inputs = w.findAll('input')
    const newValue = (inputs[0].element as HTMLInputElement).value
    expect(newValue.length).toBeGreaterThanOrEqual(20)
    expect((inputs[1].element as HTMLInputElement).value).toBe(newValue)
    expect(navigator.clipboard.writeText).toHaveBeenCalledWith(newValue)
  })

  it('resets fields and errors when the dialog closes and reopens', async () => {
    // Named global constraint of this slice, and two later tasks mount this dialog -- a stale
    // attempt (typed passwords, a shown field error) must never survive a close/reopen cycle.
    vi.spyOn(usersApi, 'changePassword').mockRejectedValue(new ApiError(400, 'x', 'INVALID_CURRENT_PASSWORD'))
    const w = mountDialog('self-id')
    await flushPromises()
    await fillAndSubmit(w, ['wrongcurrent', 'newpassword1', 'newpassword1'])
    expect(w.text()).toContain(en.password.invalidCurrent)

    await w.setProps({ open: false })
    await w.setProps({ open: true })
    await flushPromises()

    const inputs = w.findAll('input')
    expect(inputs.map((i) => (i.element as HTMLInputElement).value)).toEqual(['', '', ''])
    expect(w.text()).not.toContain(en.password.invalidCurrent)
  })

  it('shows a toast when the clipboard write rejects, instead of failing silently', async () => {
    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new Error('denied'))
    const w = mountDialog('other-id')
    await flushPromises()
    const generateButton = w.findAll('button').find((b) => b.text() === en.password.generate)!
    await generateButton.trigger('click')
    await flushPromises()
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error', summary: en.password.copyFailed }))
  })
})
