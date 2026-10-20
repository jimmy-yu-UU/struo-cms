import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import CreateUserDialog from './CreateUserDialog.vue'
import { usersApi } from '@/api/usersApi'
import { ApiError } from '@/api/apiClient'
import { useAppConfigStore } from '@/stores/appConfigStore'
import en from '@/locales/en'

const toastAdd = vi.fn()
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ add: toastAdd }) }))
vi.mock('@/api/usersApi', () => ({ usersApi: { create: vi.fn() } }))

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

function mountDialog() {
  return mount(CreateUserDialog, {
    props: { open: true },
    global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
  })
}

// DOM order: email, display name, password, confirm password.
async function fillAndSubmit(w: ReturnType<typeof mountDialog>, values: string[]): Promise<void> {
  const inputs = w.findAll('input')
  for (let i = 0; i < values.length; i++) await inputs[i].setValue(values[i])
  await w.get('form').trigger('submit')
  await flushPromises()
}

describe('CreateUserDialog', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.mocked(usersApi.create).mockReset()
    toastAdd.mockClear()
    useAppConfigStore().passwordMinLength = 8
  })

  it('renders email, name, password and confirmation fields', async () => {
    const w = mountDialog()
    await flushPromises()
    expect(w.findAll('input')).toHaveLength(4)
    expect(w.text()).toContain(en.createUser.title)
  })

  it('requires an email', async () => {
    const w = mountDialog()
    await fillAndSubmit(w, ['', '', 'longenough1', 'longenough1'])
    expect(usersApi.create).not.toHaveBeenCalled()
    expect(w.text()).toContain(en.createUser.emailRequired)
  })

  it('rejects a short password and a mismatched confirmation', async () => {
    const w = mountDialog()
    await fillAndSubmit(w, ['a@b.c', '', 'short', 'short'])
    expect(w.text()).toContain('at least 8')
    await fillAndSubmit(w, ['a@b.c', '', 'longenough1', 'different123'])
    expect(w.text()).toContain(en.password.mismatch)
    expect(usersApi.create).not.toHaveBeenCalled()
  })

  it('posts the trimmed payload and emits created then closes', async () => {
    vi.mocked(usersApi.create).mockResolvedValue({ id: 'new-id', email: 'a@b.c', name: 'Ann' })
    const w = mountDialog()
    await fillAndSubmit(w, [' a@b.c ', ' Ann ', 'longenough1', 'longenough1'])
    expect(usersApi.create).toHaveBeenCalledExactlyOnceWith({ email: 'a@b.c', name: 'Ann', password: 'longenough1' })
    expect(w.emitted('created')?.[0]).toEqual([{ id: 'new-id', email: 'a@b.c', name: 'Ann' }])
    expect(w.emitted('update:open')?.[0]).toEqual([false])
  })

  it('omits an empty display name', async () => {
    vi.mocked(usersApi.create).mockResolvedValue({ id: 'x', email: 'a@b.c' })
    const w = mountDialog()
    await fillAndSubmit(w, ['a@b.c', '', 'longenough1', 'longenough1'])
    const [body] = vi.mocked(usersApi.create).mock.calls[0]!
    expect('name' in body).toBe(false)
  })

  it('shows a duplicate email (409) on the email field and stays open', async () => {
    vi.mocked(usersApi.create).mockRejectedValue(new ApiError(409, 'Email already in use.', 'CONFLICT'))
    const w = mountDialog()
    await fillAndSubmit(w, ['a@b.c', '', 'longenough1', 'longenough1'])
    expect(w.text()).toContain(en.createUser.emailTaken)
    expect(w.emitted('created')).toBeUndefined()
    expect(w.emitted('update:open')).toBeUndefined()
  })

  it('shows the server message for a password-policy rejection (400)', async () => {
    vi.mocked(usersApi.create).mockRejectedValue(new ApiError(400, 'Password is too common.', 'BAD_USER_INPUT'))
    const w = mountDialog()
    await fillAndSubmit(w, ['a@b.c', '', 'longenough1', 'longenough1'])
    expect(w.text()).toContain('Password is too common.')
    expect(w.emitted('created')).toBeUndefined()
  })

  it('toasts an unexpected failure', async () => {
    vi.mocked(usersApi.create).mockRejectedValue(new Error('boom'))
    const w = mountDialog()
    await fillAndSubmit(w, ['a@b.c', '', 'longenough1', 'longenough1'])
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error', summary: en.createUser.failed }))
  })
})
