import { defineStore } from 'pinia'
import { getAppConfig } from '../api/appConfigApi'
import { updateBranding, type BrandingUpdate } from '../api/settingsApi'
import { applyUiLocaleConfig } from '../i18n/uiLocaleConfig'
import { CATALOG_LOCALES, type UiLocale } from '../locales'

export const useAppConfigStore = defineStore('appConfig', {
  state: () => ({
    oidcEnabled: false,
    brandName: 'StruoCMS',
    brandLogoUrl: null as string | null,
    // Matches PasswordPolicyOptions.MinLength's server-side default; only takes effect when
    // /api/config is unreachable, since load() otherwise overwrites it with the server's value.
    passwordMinLength: 8,
    // Every bundled catalog, first one default — what the SPA offers when /api/config is unreachable.
    uiLocales: [...CATALOG_LOCALES] as UiLocale[],
    uiDefaultLocale: CATALOG_LOCALES[0] as UiLocale,
  }),
  getters: {
    brandInitial: (state): string => (state.brandName.charAt(0).toUpperCase() ?? ''),
  },
  actions: {
    async load(): Promise<void> {
      try {
        const cfg = await getAppConfig()
        this.oidcEnabled = cfg.oidcEnabled
        this.brandName = cfg.brandName
        this.brandLogoUrl = cfg.brandLogoUrl
        this.passwordMinLength = cfg.passwordMinLength
        const ui = applyUiLocaleConfig(cfg.uiLocales, cfg.uiDefaultLocale)
        this.uiLocales = ui.enabled
        this.uiDefaultLocale = ui.defaultLocale
      } catch {
        // config unavailable — keep safe defaults; branding/SSO degrade, password login still works
      }
    },
    async saveBranding(body: BrandingUpdate): Promise<void> {
      const res = await updateBranding(body)
      this.brandName = res.brandName
      this.brandLogoUrl = res.brandLogoUrl
    },
  },
})
