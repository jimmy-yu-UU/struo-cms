import { apiClient } from './apiClient'
import type { CollectionMeta } from '../types/schema'

export const schemaApi = {
  getAll(): Promise<CollectionMeta[]> {
    return apiClient.get<CollectionMeta[]>('/schema')
  },
  get(name: string): Promise<CollectionMeta> {
    return apiClient.get<CollectionMeta>(`/schema/${name}`)
  },
}
