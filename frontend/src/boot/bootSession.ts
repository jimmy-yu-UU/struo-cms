type BootAuth = {
  user: object | null
  connectionError: boolean
  fetchCurrentUser: () => Promise<void>
}

// Resolves the session before the app mounts. A 401 simply means signed out and boot goes on to
// the login page. When /auth/me cannot be reached the session state is unknown, so boot stops on
// the connection-error screen: each retry (and only a retry) re-runs the check.
export async function bootSession(
  auth: BootAuth,
  loadConfig: () => Promise<unknown>,
  waitForRetry: () => Promise<void>,
): Promise<void> {
  await Promise.allSettled([auth.fetchCurrentUser(), loadConfig()])
  while (auth.connectionError && !auth.user) {
    await waitForRetry()
    await Promise.allSettled([auth.fetchCurrentUser(), loadConfig()])
  }
}
