import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ApiClient } from './apiClient'

function mockFetch(status: number, body: unknown) {
  return vi.fn().mockResolvedValue({
    status,
    ok: status >= 200 && status < 300,
    json: async () => body,
    text: async () => JSON.stringify(body),
  } as unknown as Response)
}

describe('ApiClient', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('unwraps the data envelope on success', async () => {
    vi.stubGlobal('fetch', mockFetch(200, { data: { id: 'u1' } }))
    const c = new ApiClient('/api')
    const result = await c.get<{ id: string }>('/auth/me')
    expect(result).toEqual({ id: 'u1' })
  })

  it('sends credentials: include', async () => {
    const f = mockFetch(200, { data: {} })
    vi.stubGlobal('fetch', f)
    await new ApiClient('/api').get('/auth/me')
    expect(f).toHaveBeenCalledWith('/api/auth/me', expect.objectContaining({ credentials: 'include' }))
  })

  it('throws error.message on failure', async () => {
    vi.stubGlobal('fetch', mockFetch(401, { error: { message: 'Invalid credentials.' } }))
    const c = new ApiClient('/api')
    await expect(c.get('/auth/me')).rejects.toThrow('Invalid credentials.')
  })

  it('invokes the unauthorized handler on 401', async () => {
    vi.stubGlobal('fetch', mockFetch(401, { error: { message: 'x' } }))
    const c = new ApiClient('/api')
    const onUnauth = vi.fn()
    c.setUnauthorizedHandler(onUnauth)
    await expect(c.get('/auth/me')).rejects.toThrow()
    expect(onUnauth).toHaveBeenCalledOnce()
  })
})
