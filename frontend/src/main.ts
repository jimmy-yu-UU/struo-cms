import { createApp } from 'vue'
import { createPinia } from 'pinia'
import PrimeVue from 'primevue/config'
import ConfirmationService from 'primevue/confirmationservice'
import Aura from '@primeuix/themes/aura'
import 'primeicons/primeicons.css'
import App from './App.vue'
import router from './router'
import { apiClient } from './api/apiClient'
import { useAuthStore } from './stores/authStore'

const app = createApp(App)
const pinia = createPinia()
app.use(pinia)
app.use(PrimeVue, { theme: { preset: Aura } })
app.use(ConfirmationService)

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
