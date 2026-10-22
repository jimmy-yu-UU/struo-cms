import { createLatestWins } from './latestWins'

export type GuardedLoader = {
  // Invalidates every load still in flight: when it settles it changes nothing.
  invalidate(): void
  // Runs one load. A result, error or settle that arrives after invalidate() (or after a newer
  // load started) is discarded.
  run<T>(
    fetch: () => Promise<T>,
    handlers: { apply(result: T): void, fail(e: unknown): void, settle(): void },
  ): Promise<void>
}

export function createGuardedLoader(): GuardedLoader {
  const latest = createLatestWins()
  return {
    invalidate(): void {
      latest.next()
    },
    async run(fetch, { apply, fail, settle }): Promise<void> {
      const token = latest.next()
      try {
        const result = await fetch()
        if (!latest.isCurrent(token)) return
        apply(result)
      } catch (e) {
        if (!latest.isCurrent(token)) return
        fail(e)
      } finally {
        if (latest.isCurrent(token)) settle()
      }
    },
  }
}
