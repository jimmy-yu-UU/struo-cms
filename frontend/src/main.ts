import { createApp, watch } from 'vue'
import { createPinia } from 'pinia'
import PrimeVue from 'primevue/config'
import ConfirmationService from 'primevue/confirmationservice'
import ToastService from 'primevue/toastservice'
import 'primeicons/primeicons.css'
// tokens.css first: it brings in Tailwind (including preflight). theme.css follows so the
// remaining hand-written PrimeVue-era rules still win during the coexistence period.
import './assets/tokens.css'
import './assets/theme.css'
// vue-sonner's lib/index.js never imports its own stylesheet, so without this the toaster
// (mounted in Task 10) would render completely unstyled.
import 'vue-sonner/style.css'
import App from './App.vue'
import router from './router'
import { apiClient } from './api/apiClient'
import { useAuthStore } from './stores/authStore'
import { StruoPreset } from './theme/preset'
import { i18n } from './i18n'
import { useThemeStore } from './stores/themeStore'
import { useUiLocaleStore } from './stores/uiLocaleStore'
import { resolveInitialUiLocale } from './theme/resolveInitialUiLocale'
import { useAppConfigStore } from './stores/appConfigStore'

const app = createApp(App)
const pinia = createPinia()
app.use(pinia)
app.use(PrimeVue, { theme: { preset: StruoPreset, options: { darkModeSelector: '.app-dark' } } })
app.use(i18n)
app.use(ConfirmationService)
app.use(ToastService)

useThemeStore(pinia).apply()
useUiLocaleStore(pinia).set(resolveInitialUiLocale())

const auth = useAuthStore(pinia)
apiClient.setUnauthorizedHandler(() => {
  auth.user = null
  if (router.currentRoute.value.name !== 'login') router.push({ name: 'login' })
})

// Resolve the public app config (branding + oidc) and any existing session before mount,
// so the brand renders without a flash. Neither rejection blocks mounting.
const appConfig = useAppConfigStore(pinia)
Promise.allSettled([auth.fetchCurrentUser(), appConfig.load()]).finally(() => {
  // Keep the tab title in sync with the brand name reactively: it changes at runtime when a
  // super-admin edits branding (Site Settings), not only at bootstrap. `immediate` sets it now.
  watch(() => appConfig.brandName, (name) => { document.title = name }, { immediate: true })
  app.use(router)
  app.mount('#app')
})
