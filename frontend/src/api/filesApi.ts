import { apiClient } from './apiClient'

const API_BASE = import.meta.env.VITE_API_BASE_URL || '/api'

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
    const qs = opts?.purge ? '?purge=true' : ''
    await apiClient.delete<void>(`/files/${id}${qs}`)
  },
  async restore(id: string): Promise<void> {
    await apiClient.post<void>(`/files/${id}/restore`)
  },
  contentUrl(id: string): string {
    return `${API_BASE}/files/${id}/content`
  },
}
