import { createApp, watch } from 'vue'
import { createPinia } from 'pinia'
// tokens.css first: it brings Tailwind in. theme.css's rules are unlayered, so they outrank
// every Tailwind layer on the same element whatever the import order.
import './assets/tokens.css'
import './assets/theme.css'
// vue-sonner's lib/index.js never imports its own stylesheet, so without this the toaster
// would render completely unstyled.
import 'vue-sonner/style.css'
import App from './App.vue'
import router from './router'
import { installChunkLoadRecovery } from './router/chunkLoadRecovery'
import { handleUnauthorized } from './router/sessionExpiry'
import { apiClient } from './api/apiClient'
import { useAuthStore } from './stores/authStore'
import { i18n } from './i18n'
import { useThemeStore } from './stores/themeStore'
import { useUiLocaleStore } from './stores/uiLocaleStore'
import { useAppConfigStore } from './stores/appConfigStore'
import { bootSession } from './boot/bootSession'
import { createConnectionScreenHost } from './boot/connectionScreenHost'

const app = createApp(App)
const pinia = createPinia()
app.use(pinia)
app.use(i18n)

useThemeStore(pinia).apply()

const auth = useAuthStore(pinia)
apiClient.setUnauthorizedHandler(() => { void handleUnauthorized(auth, router) })

// Resolve the public app config (branding + oidc) and any existing session before mount,
// so the brand renders without a flash. A config rejection does not block mounting; an unreachable
// /auth/me holds boot on the connection-error screen until a retry gets an answer.
const appConfig = useAppConfigStore(pinia)

const connectionScreen = createConnectionScreenHost(app._context, document.getElementById('app')!)

await bootSession(auth, () => appConfig.load(), connectionScreen)
useUiLocaleStore(pinia).init()
// Keep the tab title in sync with the brand name reactively: it changes at runtime when a
// super-admin edits branding (Site Settings), not only at bootstrap. `immediate` sets it now.
watch(() => appConfig.brandName, (name) => { document.title = name }, { immediate: true })
installChunkLoadRecovery(router)
app.use(router)
app.mount('#app')
