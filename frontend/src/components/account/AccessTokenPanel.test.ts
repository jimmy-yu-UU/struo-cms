import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '../../locales/en'
import AccessTokenPanel from './AccessTokenPanel.vue'
import { usersApi } from '../../api/usersApi'
import { formatDateTime } from '../../lib/formatDateTime'

vi.mock('../../api/usersApi', () => ({
  usersApi: { getAccessTokenInfo: vi.fn() },
}))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountPanel() {
  return mount(AccessTokenPanel, { props: { userId: 'u1' }, global: { plugins: [i18n] } })
}

describe('AccessTokenPanel', () => {
  beforeEach(() => vi.clearAllMocks())

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
    vi.mocked(usersApi.getAccessTokenInfo).mockResolvedValue({
      hasToken: true, createdAt: '2026-10-01T08:30:00Z', lastUsedAt: null,
    })
    const w = mountPanel()
    await flushPromises()
    expect(w.text()).toContain(en.accessToken.neverUsed)
  })

  it('says there is no token and shows no times when none is issued', async () => {
    vi.mocked(usersApi.getAccessTokenInfo).mockResolvedValue({ hasToken: false, createdAt: null, lastUsedAt: null })
    const w = mountPanel()
    await flushPromises()
    expect(w.text()).toContain(en.accessToken.none)
    expect(w.text()).not.toContain(en.accessToken.neverUsed)
    expect(w.text()).not.toContain(en.accessToken.createdAt)
  })

  it('reports a load failure instead of a misleading empty state', async () => {
    vi.mocked(usersApi.getAccessTokenInfo).mockRejectedValue(new Error('boom'))
    const w = mountPanel()
    await flushPromises()
    expect(w.text()).toContain(en.accessToken.loadFailed)
    expect(w.text()).not.toContain(en.accessToken.none)
  })

  it('reload() fetches again, so the view can refresh after a token change', async () => {
    vi.mocked(usersApi.getAccessTokenInfo)
      .mockResolvedValueOnce({ hasToken: false, createdAt: null, lastUsedAt: null })
      .mockResolvedValueOnce({ hasToken: true, createdAt: '2026-10-01T08:30:00Z', lastUsedAt: null })
    const w = mountPanel()
    await flushPromises()
    expect(w.text()).toContain(en.accessToken.none)
    await (w.vm as unknown as { reload: () => Promise<void> }).reload()
    await flushPromises()
    expect(w.text()).not.toContain(en.accessToken.none)
    expect(w.text()).toContain(formatDateTime('2026-10-01T08:30:00Z'))
  })
})
