import { defineStore } from 'pinia'
import { getAppConfig } from '../api/appConfigApi'
import { updateBranding, type BrandingUpdate } from '../api/settingsApi'

export const useAppConfigStore = defineStore('appConfig', {
  state: () => ({
    oidcEnabled: false,
    brandName: 'StruoCMS',
    brandLogoUrl: null as string | null,
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
