import { apiClient } from './apiClient'
import { apiPath } from './apiPath'
import type { CollectionMeta } from '../types/schema'

export const schemaApi = {
  getAll(): Promise<CollectionMeta[]> {
    return apiClient.get<CollectionMeta[]>('/schema')
  },
  // async so that a path apiPath refuses is a rejected promise, as for any other failed request.
  async get(name: string): Promise<CollectionMeta> {
    return apiClient.get<CollectionMeta>(apiPath('schema', name))
  },
}
