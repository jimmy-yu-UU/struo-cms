import { apiClient } from './apiClient'
import { apiPath, withQuery } from './apiPath'
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
  sourceRevisionNumber?: number | null
}
export type RevisionDetail = RevisionInfo & { snapshot: unknown }

export const itemsApi = {
  // async so that a path apiPath refuses is a rejected promise, as for any other failed request.
  async list(collection: string, opts: ListOptions): Promise<ListResult> {
    const { page, rows, ...query } = opts
    const params = buildListQuery(page, rows, query)
    const res = await apiClient.getRaw<ListEnvelope>(withQuery(apiPath('items', collection), new URLSearchParams(params)))
    return { data: res.data, total: res.meta?.total ?? res.data.length }
  },

  async get(
    collection: string,
    id: string,
    opts?: { locale?: string; deep?: string[] },
  ): Promise<Record<string, unknown>> {
    const params = new URLSearchParams()
    if (opts?.locale) params.set('locale', opts.locale)
    if (opts?.deep?.length) params.set('deep', opts.deep.join(','))
    return apiClient.get<Record<string, unknown>>(withQuery(apiPath('items', collection, id), params))
  },
  async create(collection: string, payload: Record<string, unknown>): Promise<Record<string, unknown>> {
    return apiClient.post<Record<string, unknown>>(apiPath('items', collection), payload)
  },
  async update(collection: string, id: string, payload: Record<string, unknown>): Promise<Record<string, unknown>> {
    return apiClient.put<Record<string, unknown>>(apiPath('items', collection, id), payload)
  },
  async remove(collection: string, id: string, opts?: { purge?: boolean }): Promise<void> {
    const params = new URLSearchParams()
    if (opts?.purge) params.set('purge', 'true')
    await apiClient.delete<void>(withQuery(apiPath('items', collection, id), params))
  },
  async restore(collection: string, id: string): Promise<Record<string, unknown>> {
    return apiClient.post<Record<string, unknown>>(apiPath('items', collection, id, 'restore'))
  },
  async listRevisions(collection: string, id: string): Promise<RevisionInfo[]> {
    return apiClient.get<RevisionInfo[]>(apiPath('items', collection, id, 'revisions'))
  },
  async getRevision(collection: string, id: string, n: number): Promise<RevisionDetail> {
    return apiClient.get<RevisionDetail>(apiPath('items', collection, id, 'revisions', n))
  },
  async revert(collection: string, id: string, n: number, version?: number): Promise<Record<string, unknown>> {
    const params = new URLSearchParams()
    if (version !== undefined) params.set('version', String(version))
    return apiClient.post<Record<string, unknown>>(
      withQuery(apiPath('items', collection, id, 'revisions', n, 'revert'), params))
  },
}
