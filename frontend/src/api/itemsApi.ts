import { apiClient } from './apiClient'
import { buildListQuery, type FilterSpec } from '../lib/buildListQuery'

export type DeletedMode = 'exclude' | 'only' | 'with'

export type ListOptions = {
  page: number
  rows: number
  sort?: string
  search?: string
  filter?: FilterSpec
  locale?: string
  deleted?: DeletedMode
  deep?: string[]
}
export type ListResult = { data: Record<string, unknown>[]; total: number }

type ListEnvelope = { data: Record<string, unknown>[]; meta?: { total: number } }

export type RevisionInfo = {
  revisionNumber: number
  operation: string
  createdAt: string
  createdBy: string | null
}
export type RevisionDetail = RevisionInfo & { snapshot: unknown }

export const itemsApi = {
  async list(collection: string, opts: ListOptions): Promise<ListResult> {
    const params = buildListQuery(
      opts.page, opts.rows, opts.sort, opts.search, opts.filter, opts.locale, opts.deleted, opts.deep,
    )
    const qs = new URLSearchParams(params).toString()
    const path = qs ? `/items/${collection}?${qs}` : `/items/${collection}`
    const res = await apiClient.getRaw<ListEnvelope>(path)
    return { data: res.data, total: res.meta?.total ?? res.data.length }
  },

  async get(
    collection: string,
    id: string,
    opts?: { locale?: string; deep?: string[] },
  ): Promise<Record<string, unknown>> {
    const params = new URLSearchParams()
    if (opts?.locale) params.set('locale', opts.locale)
    if (opts?.deep && opts.deep.length) params.set('deep', opts.deep.join(','))
    const qs = params.toString()
    return apiClient.get<Record<string, unknown>>(`/items/${collection}/${id}${qs ? `?${qs}` : ''}`)
  },
  async create(collection: string, payload: Record<string, unknown>): Promise<Record<string, unknown>> {
    return apiClient.post<Record<string, unknown>>(`/items/${collection}`, payload)
  },
  async update(collection: string, id: string, payload: Record<string, unknown>): Promise<Record<string, unknown>> {
    return apiClient.put<Record<string, unknown>>(`/items/${collection}/${id}`, payload)
  },
  async remove(collection: string, id: string, opts?: { purge?: boolean }): Promise<void> {
    const qs = opts?.purge ? '?purge=true' : ''
    await apiClient.delete<void>(`/items/${collection}/${id}${qs}`)
  },
  async restore(collection: string, id: string): Promise<Record<string, unknown>> {
    return apiClient.post<Record<string, unknown>>(`/items/${collection}/${id}/restore`)
  },
  async listRevisions(collection: string, id: string): Promise<RevisionInfo[]> {
    return apiClient.get<RevisionInfo[]>(`/items/${collection}/${id}/revisions`)
  },
  async getRevision(collection: string, id: string, n: number): Promise<RevisionDetail> {
    return apiClient.get<RevisionDetail>(`/items/${collection}/${id}/revisions/${n}`)
  },
  async revert(collection: string, id: string, n: number): Promise<Record<string, unknown>> {
    return apiClient.post<Record<string, unknown>>(`/items/${collection}/${id}/revisions/${n}/revert`)
  },
}
