import { apiClient } from './apiClient'

export type AppConfig = {
  oidcEnabled: boolean
  brandName: string
  brandLogoUrl: string | null
}

export function getAppConfig(): Promise<AppConfig> {
  return apiClient.get<AppConfig>('/config')
}
