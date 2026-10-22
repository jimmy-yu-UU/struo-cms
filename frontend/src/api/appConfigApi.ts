import { apiClient } from './apiClient'

export type OidcProviderInfo = {
  id: string
  displayName: string
  // Name looked up in lib/ssoIcons.ts; empty, null or unknown falls back to a generic key icon.
  icon: string | null
}

export type AppConfig = {
  // Enabled providers only, in the order the login page shows them.
  oidcProviders: OidcProviderInfo[]
  brandName: string
  brandLogoUrl: string | null
  // Server-owned password policy minimum. The client validates against it and sizes the admin
  // password generator from it, so it is never hardcoded on this side.
  passwordMinLength: number
}

export function getAppConfig(): Promise<AppConfig> {
  return apiClient.get<AppConfig>('/config')
}
