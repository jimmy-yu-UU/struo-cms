import { apiClient } from './apiClient'
import { buildListQuery } from '../lib/buildListQuery'

export type ListOptions = { page: number; rows: number; sort?: string; search?: string }
export type ListResult = { data: Record<string, unknown>[]; total: number }

type ListEnvelope = { data: Record<string, unknown>[]; meta: { total: number } }

export const itemsApi = {
  async list(collection: string, opts: ListOptions): Promise<ListResult> {
    const params = buildListQuery(opts.page, opts.rows, opts.sort, opts.search)
    const qs = new URLSearchParams(params).toString()
    const path = qs ? `/items/${collection}?${qs}` : `/items/${collection}`
    const res = await apiClient.getRaw<ListEnvelope>(path)
    return { data: res.data, total: res.meta.total }
  },

  async get(collection: string, id: string, opts?: { locale?: string }): Promise<Record<string, unknown>> {
    const qs = opts?.locale ? `?locale=${encodeURIComponent(opts.locale)}` : ''
    return apiClient.get<Record<string, unknown>>(`/items/${collection}/${id}${qs}`)
  },
  async create(collection: string, payload: Record<string, unknown>): Promise<Record<string, unknown>> {
    return apiClient.post<Record<string, unknown>>(`/items/${collection}`, payload)
  },
  async update(collection: string, id: string, payload: Record<string, unknown>): Promise<Record<string, unknown>> {
    return apiClient.put<Record<string, unknown>>(`/items/${collection}/${id}`, payload)
  },
  async remove(collection: string, id: string): Promise<void> {
    await apiClient.delete<void>(`/items/${collection}/${id}`)
  },
}
