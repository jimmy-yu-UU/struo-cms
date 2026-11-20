import { apiClient } from './apiClient'
import { apiPath } from './apiPath'
import type { CollectionMeta } from '../types/schema'

export const schemaApi = {
  getAll(): Promise<CollectionMeta[]> {
    return apiClient.get<CollectionMeta[]>('/schema')
  },
  async get(name: string): Promise<CollectionMeta> {
    return apiClient.get<CollectionMeta>(apiPath('schema', name))
  },
}
