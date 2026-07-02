import { apiClient } from './apiClient'
import type { LanguageInfo } from '../types/schema'

export const languagesApi = {
  getEnabled(): Promise<LanguageInfo[]> {
    return apiClient.get<LanguageInfo[]>('/languages')
  },
}
