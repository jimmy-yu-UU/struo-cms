import { apiClient } from './apiClient'

export type AppConfig = {
  oidcEnabled: boolean
  brandName: string
  brandLogoUrl: string | null
  // Server-owned password policy minimum. The client validates against it and sizes the admin
  // password generator from it, so it is never hardcoded on this side.
  passwordMinLength: number
  // Admin-UI locales the server offers (AdminUi:Locales) and its default; the SPA intersects them
  // with the catalogs it ships (see i18n/uiLocaleConfig.ts).
  uiLocales: string[]
  uiDefaultLocale: string
}

export function getAppConfig(): Promise<AppConfig> {
  return apiClient.get<AppConfig>('/config')
}
