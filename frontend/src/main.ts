import { createApp } from 'vue'
import { createPinia } from 'pinia'
import PrimeVue from 'primevue/config'
import ConfirmationService from 'primevue/confirmationservice'
import ToastService from 'primevue/toastservice'
import 'primeicons/primeicons.css'
import './assets/theme.css'
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
  document.title = appConfig.brandName
  app.use(router)
  app.mount('#app')
})
