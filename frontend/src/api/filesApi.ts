import { apiClient } from './apiClient'
import { apiPath, withQuery } from './apiPath'

export type FileMeta = {
  id: string
  fileName: string
  contentType: string
  size: number
  width: number | null
  height: number | null
  status: string
  folderId: string | null
}

export const filesApi = {
  async upload(file: File, folderId?: string | null): Promise<FileMeta> {
    const form = new FormData()
    form.append('file', file)
    if (folderId) form.append('folderId', folderId)
    return apiClient.postForm<FileMeta>('/files', form)
  },
  async remove(id: string, opts?: { purge?: boolean }): Promise<void> {
    const params = new URLSearchParams()
    if (opts?.purge) params.set('purge', 'true')
    await apiClient.delete<void>(withQuery(apiPath('files', id), params))
  },
  async restore(id: string): Promise<void> {
    await apiClient.post<void>(apiPath('files', id, 'restore'))
  },
  contentUrl(id: string): string {
    return apiClient.urlFor(apiPath('files', id, 'content'))
  },
}
