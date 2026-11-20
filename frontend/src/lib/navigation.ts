// Whether a router navigation completed. router.push and replace resolve with a failure value, or
// reject, when the navigation is cancelled, refused by a guard or fails to load its page; either
// way the caller stays where it is and decides what to do next.
export async function navigationCompleted(go: () => Promise<unknown>): Promise<boolean> {
  try {
    return (await go()) == null
  } catch {
    return false
  }
}
