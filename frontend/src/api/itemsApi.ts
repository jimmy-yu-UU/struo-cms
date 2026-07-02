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
}
