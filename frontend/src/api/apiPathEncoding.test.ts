import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { itemsApi } from './itemsApi'
import { filesApi } from './filesApi'
import { rbacApi } from './rbacApi'
import { usersApi } from './usersApi'
import { schemaApi } from './schemaApi'
import { loginLocksApi } from './loginLocksApi'
import { ApiError } from './apiClient'

const GUID = '0b6f6a2e-7c1d-4f7e-9a53-0c5d2b1e9f10'
const PURGE_ID = `${GUID}?purge=true`
const TRAVERSAL_ID = 'x/../../user/22222222-2222-2222-2222-222222222222'
const ENC_PURGE = `${GUID}%3Fpurge%3Dtrue`
const ENC_TRAVERSAL = 'x%2F..%2F..%2Fuser%2F22222222-2222-2222-2222-222222222222'

let fetchMock: ReturnType<typeof vi.fn>

beforeEach(() => {
  fetchMock = vi.fn(async () => new Response('{"data":{}}', { status: 200 }))
  vi.stubGlobal('fetch', fetchMock)
})
afterEach(() => vi.unstubAllGlobals())

function sentUrls(): string[] {
  return fetchMock.mock.calls.map((c) => String(c[0]))
}

describe('hostile ids reach the network as one encoded segment', () => {
  const calls: Array<[string, (id: string) => Promise<unknown>, (enc: string) => string]> = [
    ['itemsApi.get', (id) => itemsApi.get('article', id), (e) => `/api/items/article/${e}`],
    ['itemsApi.update', (id) => itemsApi.update('article', id, {}), (e) => `/api/items/article/${e}`],
    ['itemsApi.remove', (id) => itemsApi.remove('article', id), (e) => `/api/items/article/${e}`],
    ['itemsApi.restore', (id) => itemsApi.restore('article', id), (e) => `/api/items/article/${e}/restore`],
    ['itemsApi.listRevisions', (id) => itemsApi.listRevisions('article', id), (e) => `/api/items/article/${e}/revisions`],
    ['itemsApi.getRevision', (id) => itemsApi.getRevision('article', id, 3), (e) => `/api/items/article/${e}/revisions/3`],
    ['itemsApi.revert', (id) => itemsApi.revert('article', id, 3), (e) => `/api/items/article/${e}/revisions/3/revert`],
    ['filesApi.remove', (id) => filesApi.remove(id), (e) => `/api/files/${e}`],
    ['filesApi.restore', (id) => filesApi.restore(id), (e) => `/api/files/${e}/restore`],
    ['rbacApi.getRolePermissions', (id) => rbacApi.getRolePermissions(id), (e) => `/api/roles/${e}/permissions`],
    ['rbacApi.putRolePermissions', (id) => rbacApi.putRolePermissions(id, []), (e) => `/api/roles/${e}/permissions`],
    ['rbacApi.getEffectivePermissions', (id) => rbacApi.getEffectivePermissions(id),
      (e) => `/api/users/${e}/effective-permissions`],
    ['usersApi.getAccessTokenInfo', (id) => usersApi.getAccessTokenInfo(id), (e) => `/api/users/${e}/access-token`],
    ['usersApi.generateAccessToken', (id) => usersApi.generateAccessToken(id), (e) => `/api/users/${e}/access-token`],
    ['usersApi.revokeAccessToken', (id) => usersApi.revokeAccessToken(id), (e) => `/api/users/${e}/access-token`],
    ['usersApi.changePassword', (id) => usersApi.changePassword(id, { newPassword: 'n' }), (e) => `/api/users/${e}/password`],
    ['schemaApi.get', (id) => schemaApi.get(id), (e) => `/api/schema/${e}`],
  ]

  it.each(calls)('%s with a query-looking id', async (_name, call, expected) => {
    await call(PURGE_ID)
    expect(sentUrls()).toEqual([expected(ENC_PURGE)])
  })

  it.each(calls)('%s with a traversal-looking id', async (_name, call, expected) => {
    await call(TRAVERSAL_ID)
    expect(sentUrls()).toEqual([expected(ENC_TRAVERSAL)])
  })

  it('itemsApi encodes the collection segment too', async () => {
    await itemsApi.get('a/../b', GUID)
    expect(sentUrls()).toEqual([`/api/items/a%2F..%2Fb/${GUID}`])
  })

  it('filesApi.contentUrl encodes the id', () => {
    expect(filesApi.contentUrl(PURGE_ID)).toBe(`/api/files/${ENC_PURGE}/content`)
  })
})

describe('segments that would collapse a path send no request', () => {
  const calls: Array<[string, () => Promise<unknown>]> = [
    ['itemsApi.get ..', () => itemsApi.get('article', '..')],
    ['itemsApi.get .', () => itemsApi.get('article', '.')],
    ['itemsApi.get empty id', () => itemsApi.get('article', '')],
    ['itemsApi.remove ..', () => itemsApi.remove('article', '..')],
    ['itemsApi.list ..', () => itemsApi.list('..', { page: 0, rows: 10 })],
    ['filesApi.remove ..', () => filesApi.remove('..')],
    ['rbacApi.getRolePermissions ..', () => rbacApi.getRolePermissions('..')],
    ['usersApi.getAccessTokenInfo ..', () => usersApi.getAccessTokenInfo('..')],
    ['schemaApi.get ..', () => schemaApi.get('..')],
  ]

  it.each(calls)('%s rejects with 404 NOT_FOUND and never calls fetch', async (_name, call) => {
    const err = await call().catch((e: unknown) => e)
    expect(err).toBeInstanceOf(ApiError)
    expect((err as ApiError).status).toBe(404)
    expect((err as ApiError).code).toBe('NOT_FOUND')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('filesApi.contentUrl throws for ..', () => {
    expect(() => filesApi.contentUrl('..')).toThrow(ApiError)
  })
})

describe('ordinary ids produce the same URLs as string interpolation did', () => {
  it('items, files, roles, users, schema and login locks', async () => {
    await itemsApi.get('article', GUID, { locale: 'en', deep: ['a', 'b'] })
    await itemsApi.get('article', GUID)
    await itemsApi.list('article', { page: 1, rows: 10, sort: '-createdAt', search: 'x' })
    await itemsApi.list('article', { page: 0, rows: 25 })
    await itemsApi.create('article', {})
    await itemsApi.update('article', GUID, {})
    await itemsApi.remove('article', GUID)
    await itemsApi.remove('article', GUID, { purge: true })
    await itemsApi.restore('article', GUID)
    await itemsApi.listRevisions('article', GUID)
    await itemsApi.getRevision('article', GUID, 4)
    await itemsApi.revert('article', GUID, 4)
    await itemsApi.revert('article', GUID, 4, 9)
    await filesApi.remove(GUID)
    await filesApi.remove(GUID, { purge: true })
    await filesApi.restore(GUID)
    await rbacApi.getRolePermissions(GUID)
    await rbacApi.getEffectivePermissions(GUID)
    await rbacApi.getEffectivePermissions(GUID, ['r1', 'r2'])
    await rbacApi.getEffectivePermissions(GUID, [])
    await usersApi.getAccessTokenInfo(GUID)
    await schemaApi.get('mediafolder')
    await loginLocksApi.clear('a+b@c.d')
    expect(sentUrls()).toEqual([
      `/api/items/article/${GUID}?locale=en&deep=a%2Cb`,
      `/api/items/article/${GUID}`,
      '/api/items/article?limit=10&offset=10&sort=-createdAt&search=x',
      '/api/items/article?limit=25&offset=0',
      '/api/items/article',
      `/api/items/article/${GUID}`,
      `/api/items/article/${GUID}`,
      `/api/items/article/${GUID}?purge=true`,
      `/api/items/article/${GUID}/restore`,
      `/api/items/article/${GUID}/revisions`,
      `/api/items/article/${GUID}/revisions/4`,
      `/api/items/article/${GUID}/revisions/4/revert`,
      `/api/items/article/${GUID}/revisions/4/revert?version=9`,
      `/api/files/${GUID}`,
      `/api/files/${GUID}?purge=true`,
      `/api/files/${GUID}/restore`,
      `/api/roles/${GUID}/permissions`,
      `/api/users/${GUID}/effective-permissions`,
      `/api/users/${GUID}/effective-permissions?roles=r1%2Cr2`,
      `/api/users/${GUID}/effective-permissions?roles=`,
      `/api/users/${GUID}/access-token`,
      '/api/schema/mediafolder',
      '/api/login-locks?email=a%2Bb%40c.d',
    ])
    expect(filesApi.contentUrl(GUID)).toBe(`/api/files/${GUID}/content`)
  })

  it('effective-permissions roles are one URLSearchParams value (commas encoded)', async () => {
    await rbacApi.getEffectivePermissions(GUID, ['a&b', 'c'])
    expect(sentUrls()).toEqual([`/api/users/${GUID}/effective-permissions?roles=a%26b%2Cc`])
  })
})
