import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '../../locales/en'
import AccessTokenPanel from './AccessTokenPanel.vue'
import { usersApi } from '../../api/usersApi'
import { ApiError } from '../../api/apiClient'
import { formatDateTime } from '../../lib/formatDateTime'
import { DialogClose, DialogContent } from 'reka-ui'
import { Dialog } from '@/components/ui/dialog'

vi.mock('../../api/usersApi', () => ({
  usersApi: { getAccessTokenInfo: vi.fn(), generateAccessToken: vi.fn(), revokeAccessToken: vi.fn() },
}))
const toastAdd = vi.fn()
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ add: toastAdd }) }))
const confirmRequire = vi.fn()
vi.mock('@/composables/useConfirm', () => ({ useConfirm: () => ({ require: confirmRequire }) }))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountPanel() {
  return mount(AccessTokenPanel, {
    props: { userId: 'u1' },
    // reka's DialogPortal is itself named Teleport and collides with VTU's own `teleport` stub,
    // which would otherwise drop the whole dialog body.
    global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
  })
}

const NO_TOKEN = { hasToken: false, createdAt: null, lastUsedAt: null }
const HAS_TOKEN = { hasToken: true, createdAt: '2026-10-01T08:30:00Z', lastUsedAt: null }
const SECRET = 'S3cr3t-token-value'

function button(w: ReturnType<typeof mountPanel>, label: string) {
  return w.findAll('button').find((b) => b.text() === label)
}

describe('AccessTokenPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    confirmRequire.mockResolvedValue(true)
    Object.defineProperty(navigator, 'clipboard', {
      value: { writeText: vi.fn().mockResolvedValue(undefined) },
      configurable: true,
    })
  })

  it('shows the issue and last-use times through the shared formatter', async () => {
    vi.mocked(usersApi.getAccessTokenInfo).mockResolvedValue({
      hasToken: true, createdAt: '2026-10-01T08:30:00Z', lastUsedAt: '2026-10-02T09:45:00Z',
    })
    const w = mountPanel()
    await flushPromises()
    expect(usersApi.getAccessTokenInfo).toHaveBeenCalledWith('u1')
    expect(w.text()).toContain(formatDateTime('2026-10-01T08:30:00Z'))
    expect(w.text()).toContain(formatDateTime('2026-10-02T09:45:00Z'))
  })

  it('says the token has never been used when lastUsedAt is null', async () => {
    vi.mocked(usersApi.getAccessTokenInfo).mockResolvedValue(HAS_TOKEN)
    const w = mountPanel()
    await flushPromises()
    expect(w.text()).toContain(en.accessToken.neverUsed)
  })

  it('says there is no token and shows no times when none is issued', async () => {
    vi.mocked(usersApi.getAccessTokenInfo).mockResolvedValue(NO_TOKEN)
    const w = mountPanel()
    await flushPromises()
    expect(w.text()).toContain(en.accessToken.none)
    expect(w.text()).not.toContain(en.accessToken.neverUsed)
    expect(w.text()).not.toContain(en.accessToken.createdAt)
  })

  it('shows a loading line until the request settles', async () => {
    let resolve!: (v: typeof NO_TOKEN) => void
    vi.mocked(usersApi.getAccessTokenInfo).mockReturnValue(new Promise((r) => { resolve = r }))
    const w = mountPanel()
    expect(w.text()).toContain(en.common.loading)
    resolve(NO_TOKEN)
    await flushPromises()
    expect(w.text()).not.toContain(en.common.loading)
    expect(w.text()).toContain(en.accessToken.none)
  })

  it('reports a load failure instead of a misleading empty state', async () => {
    vi.mocked(usersApi.getAccessTokenInfo).mockRejectedValue(new Error('boom'))
    const w = mountPanel()
    await flushPromises()
    expect(w.text()).toContain(en.accessToken.loadFailed)
    expect(w.text()).not.toContain(en.accessToken.none)
  })

  it('reload() fetches again', async () => {
    vi.mocked(usersApi.getAccessTokenInfo).mockResolvedValueOnce(NO_TOKEN).mockResolvedValueOnce(HAS_TOKEN)
    const w = mountPanel()
    await flushPromises()
    expect(w.text()).toContain(en.accessToken.none)
    await (w.vm as unknown as { reload: () => Promise<void> }).reload()
    await flushPromises()
    expect(w.text()).not.toContain(en.accessToken.none)
    expect(w.text()).toContain(formatDateTime(HAS_TOKEN.createdAt))
  })

  describe('generate and revoke', () => {
    it('offers Generate and no Regenerate or Revoke when the user has no token', async () => {
      vi.mocked(usersApi.getAccessTokenInfo).mockResolvedValue(NO_TOKEN)
      const w = mountPanel()
      await flushPromises()
      expect(button(w, en.accessToken.generate)).toBeDefined()
      expect(button(w, en.accessToken.regenerate)).toBeUndefined()
      expect(button(w, en.accessToken.revoke)).toBeUndefined()
    })

    it('generates without a confirmation, shows the token once with a note, and reloads', async () => {
      vi.mocked(usersApi.getAccessTokenInfo).mockResolvedValueOnce(NO_TOKEN).mockResolvedValueOnce(HAS_TOKEN)
      vi.mocked(usersApi.generateAccessToken).mockResolvedValue({ token: SECRET })
      const w = mountPanel()
      await flushPromises()
      await button(w, en.accessToken.generate)!.trigger('click')
      await flushPromises()
      expect(confirmRequire).not.toHaveBeenCalled()
      expect(usersApi.generateAccessToken).toHaveBeenCalledExactlyOnceWith('u1')
      expect(w.text()).toContain(SECRET)
      expect(w.text()).toContain(en.accessToken.issuedNote)
      expect(usersApi.getAccessTokenInfo).toHaveBeenCalledTimes(2)
      expect(button(w, en.accessToken.regenerate)).toBeDefined()
    })

    it('copies the issued token to the clipboard', async () => {
      vi.mocked(usersApi.getAccessTokenInfo).mockResolvedValue(NO_TOKEN)
      vi.mocked(usersApi.generateAccessToken).mockResolvedValue({ token: SECRET })
      const w = mountPanel()
      await flushPromises()
      await button(w, en.accessToken.generate)!.trigger('click')
      await flushPromises()
      await button(w, en.accessToken.copy)!.trigger('click')
      await flushPromises()
      expect(navigator.clipboard.writeText).toHaveBeenCalledExactlyOnceWith(SECRET)
      expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'success', summary: en.accessToken.copied }))
    })

    it('says so when the clipboard write is refused, and keeps the token visible', async () => {
      vi.mocked(usersApi.getAccessTokenInfo).mockResolvedValue(NO_TOKEN)
      vi.mocked(usersApi.generateAccessToken).mockResolvedValue({ token: SECRET })
      vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new Error('denied'))
      const w = mountPanel()
      await flushPromises()
      await button(w, en.accessToken.generate)!.trigger('click')
      await flushPromises()
      await button(w, en.accessToken.copy)!.trigger('click')
      await flushPromises()
      expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error', summary: en.accessToken.copyFailed }))
      expect(w.text()).toContain(SECRET)
    })

    async function openTokenDialog() {
      vi.mocked(usersApi.getAccessTokenInfo).mockResolvedValue(NO_TOKEN)
      vi.mocked(usersApi.generateAccessToken).mockResolvedValue({ token: SECRET })
      const w = mountPanel()
      await flushPromises()
      await button(w, en.accessToken.generate)!.trigger('click')
      await flushPromises()
      expect(w.html()).toContain(SECRET)
      return w
    }
    const issuedToken = (w: ReturnType<typeof mountPanel>) =>
      (w.vm as unknown as { $: { setupState: { issuedToken: string | null } } }).$.setupState.issuedToken

    async function expectDiscarded(w: ReturnType<typeof mountPanel>) {
      await flushPromises()
      expect(w.html()).not.toContain(SECRET)
      expect(w.text()).not.toContain(en.accessToken.issuedNote)
      expect(issuedToken(w)).toBeNull()
    }

    it('drops the token from the page and from state when the Close button is used', async () => {
      const w = await openTokenDialog()
      expect(issuedToken(w)).toBe(SECRET)
      await w.findAll('button').filter((b) => b.text() === en.common.close).at(-1)!.trigger('click')
      await expectDiscarded(w)
    })

    it('drops the token when the dialog is dismissed through its X', async () => {
      const w = await openTokenDialog()
      await w.findComponent(DialogClose).trigger('click')
      await expectDiscarded(w)
    })

    it('drops the token when the dialog asks to close, as Esc does', async () => {
      const w = await openTokenDialog()
      w.findComponent(Dialog).vm.$emit('update:open', false)
      await expectDiscarded(w)
    })

    it('keeps the token when the user clicks outside the dialog', async () => {
      const w = await openTokenDialog()
      const outside = new Event('pointerdown', { cancelable: true })
      w.findComponent(DialogContent).vm.$emit('interactOutside', outside)
      await flushPromises()
      expect(outside.defaultPrevented).toBe(true)
      expect(w.html()).toContain(SECRET)
      expect(issuedToken(w)).toBe(SECRET)
    })

    it('disables the buttons while a request is in flight and sends one request per click', async () => {
      vi.mocked(usersApi.getAccessTokenInfo).mockResolvedValue(HAS_TOKEN)
      let resolve!: (v: { token: string }) => void
      vi.mocked(usersApi.generateAccessToken).mockReturnValue(new Promise((r) => { resolve = r }))
      const w = mountPanel()
      await flushPromises()
      await button(w, en.accessToken.regenerate)!.trigger('click')
      await flushPromises()
      expect(button(w, en.accessToken.regenerate)!.attributes('disabled')).toBeDefined()
      expect(button(w, en.accessToken.revoke)!.attributes('disabled')).toBeDefined()
      await button(w, en.accessToken.regenerate)!.trigger('click')
      await flushPromises()
      expect(usersApi.generateAccessToken).toHaveBeenCalledTimes(1)
      expect(confirmRequire).toHaveBeenCalledTimes(1)
      resolve({ token: SECRET })
      await flushPromises()
      expect(button(w, en.accessToken.regenerate)!.attributes('disabled')).toBeUndefined()
      expect(button(w, en.accessToken.revoke)!.attributes('disabled')).toBeUndefined()
    })

    it('asks before regenerating, and does nothing when declined', async () => {
      vi.mocked(usersApi.getAccessTokenInfo).mockResolvedValue(HAS_TOKEN)
      confirmRequire.mockResolvedValue(false)
      const w = mountPanel()
      await flushPromises()
      expect(button(w, en.accessToken.generate)).toBeUndefined()
      await button(w, en.accessToken.regenerate)!.trigger('click')
      await flushPromises()
      expect(confirmRequire).toHaveBeenCalledWith(expect.objectContaining({
        header: en.accessToken.regenerateConfirmHeader,
        message: en.accessToken.regenerateConfirmMessage,
      }))
      expect(usersApi.generateAccessToken).not.toHaveBeenCalled()
      expect(w.text()).not.toContain(en.accessToken.issuedNote)
    })

    it('regenerates after the confirmation is accepted', async () => {
      vi.mocked(usersApi.getAccessTokenInfo).mockResolvedValue(HAS_TOKEN)
      vi.mocked(usersApi.generateAccessToken).mockResolvedValue({ token: SECRET })
      const w = mountPanel()
      await flushPromises()
      await button(w, en.accessToken.regenerate)!.trigger('click')
      await flushPromises()
      expect(usersApi.generateAccessToken).toHaveBeenCalledExactlyOnceWith('u1')
      expect(w.text()).toContain(SECRET)
    })

    it('asks before revoking, then deletes the token, reloads and hides the token details', async () => {
      vi.mocked(usersApi.getAccessTokenInfo).mockResolvedValueOnce(HAS_TOKEN).mockResolvedValueOnce(NO_TOKEN)
      vi.mocked(usersApi.revokeAccessToken).mockResolvedValue(undefined)
      const w = mountPanel()
      await flushPromises()
      expect(w.text()).toContain(formatDateTime(HAS_TOKEN.createdAt))
      await button(w, en.accessToken.revoke)!.trigger('click')
      await flushPromises()
      expect(confirmRequire).toHaveBeenCalledWith(expect.objectContaining({
        header: en.accessToken.revokeConfirmHeader, severity: 'danger',
      }))
      expect(usersApi.revokeAccessToken).toHaveBeenCalledExactlyOnceWith('u1')
      expect(w.text()).toContain(en.accessToken.none)
      expect(w.text()).not.toContain(formatDateTime(HAS_TOKEN.createdAt))
      expect(button(w, en.accessToken.revoke)).toBeUndefined()
      expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'success', summary: en.accessToken.revoked }))
    })

    it('does not revoke when the confirmation is declined', async () => {
      vi.mocked(usersApi.getAccessTokenInfo).mockResolvedValue(HAS_TOKEN)
      confirmRequire.mockResolvedValue(false)
      const w = mountPanel()
      await flushPromises()
      await button(w, en.accessToken.revoke)!.trigger('click')
      await flushPromises()
      expect(usersApi.revokeAccessToken).not.toHaveBeenCalled()
      expect(w.text()).toContain(formatDateTime(HAS_TOKEN.createdAt))
    })

    it('toasts a generate failure, shows no token and does not reload', async () => {
      vi.mocked(usersApi.getAccessTokenInfo).mockResolvedValue(NO_TOKEN)
      vi.mocked(usersApi.generateAccessToken).mockRejectedValue(new ApiError(500, 'boom', 'INTERNAL_ERROR'))
      const w = mountPanel()
      await flushPromises()
      await button(w, en.accessToken.generate)!.trigger('click')
      await flushPromises()
      expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error', summary: en.accessToken.generateFailed }))
      expect(w.text()).not.toContain(en.accessToken.issuedNote)
      expect(usersApi.getAccessTokenInfo).toHaveBeenCalledTimes(1)
    })

    it('toasts a revoke failure and keeps the token details', async () => {
      vi.mocked(usersApi.getAccessTokenInfo).mockResolvedValue(HAS_TOKEN)
      vi.mocked(usersApi.revokeAccessToken).mockRejectedValue(new ApiError(403, 'no', 'FORBIDDEN'))
      const w = mountPanel()
      await flushPromises()
      await button(w, en.accessToken.revoke)!.trigger('click')
      await flushPromises()
      expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error', summary: en.accessToken.revokeFailed }))
      expect(w.text()).toContain(formatDateTime(HAS_TOKEN.createdAt))
    })
  })
})
