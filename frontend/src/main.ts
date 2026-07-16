import { createApp } from 'vue'
import { createPinia } from 'pinia'
import PrimeVue from 'primevue/config'
import ConfirmationService from 'primevue/confirmationservice'
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

const app = createApp(App)
const pinia = createPinia()
app.use(pinia)
app.use(PrimeVue, { theme: { preset: StruoPreset, options: { darkModeSelector: '.app-dark' } } })
app.use(i18n)
app.use(ConfirmationService)

useThemeStore(pinia).apply()
useUiLocaleStore(pinia).set(resolveInitialUiLocale())

const auth = useAuthStore(pinia)
apiClient.setUnauthorizedHandler(() => {
  auth.user = null
  if (router.currentRoute.value.name !== 'login') router.push({ name: 'login' })
})

// Resolve any existing session before the router/guard runs, then mount.
auth.fetchCurrentUser().finally(() => {
  app.use(router)
  app.mount('#app')
})
