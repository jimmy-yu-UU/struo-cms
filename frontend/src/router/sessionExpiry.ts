import { START_LOCATION, type Router } from 'vue-router'
import type { useAuthStore } from '../stores/authStore'
import { loginRedirectQuery } from '../lib/safeRedirect'
import { resetSessionStores } from '../stores/resetSessionStores'

type AuthStore = ReturnType<typeof useAuthStore>

// Reaction to a 401 from any API call. A 401 while signed in means the session ended server-side:
// flag it (so unsaved-changes guards let the navigation through), drop the user, and send the
// browser to login carrying the page it was on. Unsaved edits are dropped by design.
// Without a user, a 401 is an ordinary failed request (wrong password, the bootstrap /auth/me) and
// only needs to land on the login page. Before the router is installed there is no page yet: the
// initial navigation's guard sends the visitor to login with the requested path.
export async function handleUnauthorized(auth: AuthStore, router: Router): Promise<void> {
  if (auth.loggingOut || auth.sessionExpired) return
  const current = router.currentRoute.value
  if (auth.user === null) {
    if (current !== START_LOCATION && current.name !== 'login') await router.push({ name: 'login' })
    return
  }
  auth.markSessionExpired()
  try {
    await router.push({
      name: 'login',
      query: { ...loginRedirectQuery(current.fullPath), reason: 'expired' },
    })
  } finally {
    // After the navigation, so views still on screen do not re-render against emptied stores.
    resetSessionStores()
  }
}
