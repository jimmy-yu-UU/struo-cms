import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ApiClient, ApiError } from './apiClient'

function mockFetch(status: number, body: unknown) {
  return vi.fn().mockResolvedValue({
    status,
    ok: status >= 200 && status < 300,
    headers: { get: () => null },
    json: async () => body,
    text: async () => JSON.stringify(body),
  } as unknown as Response)
}

describe('ApiClient', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('unwraps the data envelope on success', async () => {
    vi.stubGlobal('fetch', mockFetch(200, { success: true, data: { id: 'u1' } }))
    const c = new ApiClient('/api')
    const result = await c.get<{ id: string }>('/auth/me')
    expect(result).toEqual({ id: 'u1' })
  })

  it('unwraps data by branching on the success discriminator', async () => {
    vi.stubGlobal('fetch', mockFetch(200, { success: true, data: { id: 'u1' } }))
    const result = await new ApiClient('/api').get<{ id: string }>('/auth/me')
    expect(result).toEqual({ id: 'u1' })
  })

  it('returns a null data payload as null (not the whole envelope)', async () => {
    vi.stubGlobal('fetch', mockFetch(200, { success: true, data: null }))
    const result = await new ApiClient('/api').get('/auth/me')
    expect(result).toBeNull()
  })

  it('falls back to data ?? payload for a non-enveloped JSON response', async () => {
    vi.stubGlobal('fetch', mockFetch(200, { data: { id: 'legacy' } }))
    const result = await new ApiClient('/api').get('/auth/me')
    expect(result).toEqual({ id: 'legacy' })
  })

  it('sends credentials: include', async () => {
    const f = mockFetch(200, { success: true, data: {} })
    vi.stubGlobal('fetch', f)
    await new ApiClient('/api').get('/auth/me')
    expect(f).toHaveBeenCalledWith('/api/auth/me', expect.objectContaining({ credentials: 'include' }))
  })

  it('throws an ApiError carrying status/code/message from the error envelope', async () => {
    vi.stubGlobal('fetch', mockFetch(401, { success: false, error: { code: 'UNAUTHORIZED', message: 'Invalid credentials.' } }))
    const c = new ApiClient('/api')
    const err = await c.get('/auth/me').catch((e) => e) as ApiError
    expect(err).toBeInstanceOf(ApiError)
    expect(err).toBeInstanceOf(Error)
    expect(err.status).toBe(401)
    expect(err.code).toBe('UNAUTHORIZED')
    expect(err.message).toBe('Invalid credentials.')
    expect(err.details).toBeUndefined()
  })

  it('carries VALIDATION details on the thrown ApiError', async () => {
    vi.stubGlobal('fetch', mockFetch(400, {
      success: false,
      error: { code: 'VALIDATION', message: 'One or more validation errors occurred.', details: [{ field: 'email', message: 'Email is required.' }] },
    }))
    const err = await new ApiClient('/api').post('/auth/login', {}).catch((e) => e) as ApiError
    expect(err).toBeInstanceOf(ApiError)
    expect(err.code).toBe('VALIDATION')
    expect(err.details).toEqual([{ field: 'email', message: 'Email is required.' }])
  })

  it('falls back to a default message on a non-JSON error body', async () => {
    const c = new ApiClient('/api')
    globalThis.fetch = vi.fn().mockResolvedValue({
      status: 500, ok: false,
      headers: { get: () => null },
      json: async () => { throw new Error('not json') },
      text: async () => 'oops',
    } as unknown as Response)
    const err = await c.get('/x').catch((e) => e) as ApiError
    expect(err).toBeInstanceOf(ApiError)
    expect(err.status).toBe(500)
    expect(err.message).toBe('Request failed (500)')
    expect(err.code).toBeUndefined()
  })

  it('invokes the unauthorized handler on 401', async () => {
    vi.stubGlobal('fetch', mockFetch(401, { success: false, error: { code: 'UNAUTHORIZED', message: 'x' } }))
    const c = new ApiClient('/api')
    const onUnauth = vi.fn()
    c.setUnauthorizedHandler(onUnauth)
    await expect(c.get('/auth/me')).rejects.toThrow()
    expect(onUnauth).toHaveBeenCalledOnce()
  })

  it('getRaw returns the whole success envelope including meta', async () => {
    vi.stubGlobal('fetch', mockFetch(200, { success: true, data: [{ id: '1' }], meta: { total: 42, limit: 25, offset: 0 } }))
    const c = new ApiClient('/api')
    const result = await c.getRaw<{ data: unknown[]; meta: { total: number } }>('/items/article')
    expect(result).toEqual({ success: true, data: [{ id: '1' }], meta: { total: 42, limit: 25, offset: 0 } })
  })

  it('put unwraps the data envelope', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ success: true, data: { id: '1' } }), { status: 200 }))
    const c = new ApiClient('/api')
    await expect(c.put('/items/article/1', { x: 1 })).resolves.toEqual({ id: '1' })
  })

  it('delete tolerates 204 no-content', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    const c = new ApiClient('/api')
    await expect(c.delete('/items/article/1')).resolves.toBeUndefined()
  })

  it('put throws an ApiError with the server code on non-2xx', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ success: false, error: { code: 'BAD_USER_INPUT', message: 'nope' } }), { status: 400 }))
    const err = await new ApiClient('/api').put('/x', {}).catch((e) => e) as ApiError
    expect(err).toBeInstanceOf(ApiError)
    expect(err.code).toBe('BAD_USER_INPUT')
    expect(err.message).toBe('nope')
  })

  // Mutations carry the CSRF header; safe GETs do not.
  it('attaches the CSRF header on mutations', async () => {
    const f = mockFetch(200, { success: true, data: {} })
    vi.stubGlobal('fetch', f)
    await new ApiClient('/api').post('/items/article', { x: 1 })
    const headers = f.mock.calls[0][1].headers as Record<string, string>
    expect(headers['X-Struo-CSRF']).toBe('1')
  })

  it('does not attach the CSRF header on GET', async () => {
    const f = mockFetch(200, { success: true, data: {} })
    vi.stubGlobal('fetch', f)
    await new ApiClient('/api').get('/auth/me')
    const headers = (f.mock.calls[0][1].headers ?? {}) as Record<string, string>
    expect(headers['X-Struo-CSRF']).toBeUndefined()
  })

  it('exposes Retry-After as retryAfterSeconds on the thrown ApiError', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ success: false, error: { code: 'TOO_MANY_REQUESTS', message: 'nope' } }), {
        status: 429,
        headers: { 'Content-Type': 'application/json', 'Retry-After': '42' },
      }),
    ))
    const c = new ApiClient('/api')

    await expect(c.get('/whatever')).rejects.toMatchObject({
      status: 429,
      code: 'TOO_MANY_REQUESTS',
      retryAfterSeconds: 42,
    })
  })

  it('leaves retryAfterSeconds undefined when Retry-After is absent', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ success: false, error: { code: 'BAD_USER_INPUT', message: 'nope' } }), {
        status: 400,
        headers: { 'Content-Type': 'application/json' },
      }),
    ))
    const c = new ApiClient('/api')

    const err = await c.get('/whatever').catch((e) => e) as ApiError
    expect(err).toBeInstanceOf(ApiError)
    expect(err.retryAfterSeconds).toBeUndefined()
  })

  it('ignores a non-numeric Retry-After rather than storing NaN', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ success: false, error: { code: 'TOO_MANY_REQUESTS', message: 'nope' } }), {
        status: 429,
        headers: { 'Content-Type': 'application/json', 'Retry-After': 'Wed, 21 Oct 2015 07:28:00 GMT' },
      }),
    ))
    const c = new ApiClient('/api')

    const err = await c.get('/whatever').catch((e) => e) as ApiError
    expect(err).toBeInstanceOf(ApiError)
    expect(err.retryAfterSeconds).toBeUndefined()
  })
})
