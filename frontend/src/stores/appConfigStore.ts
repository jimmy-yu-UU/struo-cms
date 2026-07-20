import { defineStore } from 'pinia'
import { getAppConfig } from '../api/appConfigApi'

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
  },
})
