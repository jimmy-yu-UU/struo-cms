import type { Router } from 'vue-router'
import type { useAuthStore } from '../stores/authStore'
import { resetSessionStores } from '../stores/resetSessionStores'

type AuthStore = ReturnType<typeof useAuthStore>

// Reaction to a 401 from any API call. A 401 while signed in means the session ended server-side:
// flag it (so unsaved-changes guards let the navigation through), drop the user, and send the
// browser to login carrying the page it was on. Unsaved edits are dropped by design.
// Without a user, a 401 is an ordinary failed request (wrong password, the bootstrap /auth/me) and
// only needs to land on the login page.
export async function handleUnauthorized(auth: AuthStore, router: Router): Promise<void> {
  if (auth.loggingOut || auth.sessionExpired) return
  const current = router.currentRoute.value
  if (auth.user === null) {
    if (current.name !== 'login') await router.push({ name: 'login' })
    return
  }
  auth.sessionExpired = true
  auth.user = null
  try {
    await router.push({ name: 'login', query: { redirect: current.fullPath, reason: 'expired' } })
  } finally {
    // After the navigation, so views still on screen do not re-render against emptied stores.
    resetSessionStores()
  }
}
