import { describe, it, expect, vi, beforeEach } from 'vitest'
import { filesApi } from './filesApi'
import { apiClient } from './apiClient'

describe('filesApi', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('upload posts multipart with part name "file" and returns metadata', async () => {
    const spy = vi.spyOn(apiClient, 'postForm').mockResolvedValue({
      id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 10, width: 2, height: 2, status: 'published',
    } as never)
    const file = new File(['x'], 'a.png', { type: 'image/png' })
    const meta = await filesApi.upload(file)
    expect(meta.id).toBe('f1')
    const [path, form] = spy.mock.calls[0]
    expect(path).toBe('/files')
    expect((form as FormData).get('file')).toBe(file)
  })

  it('remove deletes by id', async () => {
    const spy = vi.spyOn(apiClient, 'delete').mockResolvedValue(undefined as never)
    await filesApi.remove('f1')
    expect(spy).toHaveBeenCalledWith('/files/f1')
  })

  it('contentUrl builds the content path', () => {
    expect(filesApi.contentUrl('f1')).toMatch(/\/files\/f1\/content$/)
  })
})
