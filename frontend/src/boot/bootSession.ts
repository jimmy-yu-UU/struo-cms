type BootAuth = {
  user: object | null
  connectionError: boolean
  fetchCurrentUser: () => Promise<void>
}

type RetryScreen = {
  waitForRetry: () => Promise<void>
  setBusy: (busy: boolean) => void
  close: () => void
}

// Resolves the session before the app mounts. A 401 simply means signed out and boot goes on to
// the login page. When /auth/me cannot be reached the session state is unknown, so boot stops on
// the connection-error screen: each retry (and only a retry) re-runs the check, with the screen
// kept up and marked busy meanwhile, and closed once when boot can proceed.
export async function bootSession(
  auth: BootAuth,
  loadConfig: () => Promise<unknown>,
  screen: RetryScreen,
): Promise<void> {
  await Promise.allSettled([auth.fetchCurrentUser(), loadConfig()])
  try {
    while (auth.connectionError && !auth.user) {
      await screen.waitForRetry()
      screen.setBusy(true)
      try {
        await Promise.allSettled([auth.fetchCurrentUser(), loadConfig()])
      } finally {
        screen.setBusy(false)
      }
    }
  } finally {
    screen.close()
  }
}
