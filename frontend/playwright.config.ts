import { defineConfig } from '@playwright/test'

export default defineConfig({
  // Single worker: the specs share one Vite dev server and one seeded admin account. The dev
  // server transforms each Vue SFC/module on demand on first request; several Playwright workers
  // navigating at once all trigger that cold compile simultaneously and can starve the single
  // dev-server process past a normal navigation timeout. Serializing is a small cost for a suite
  // this size and removes the flakiness entirely.
  workers: 1,
  // baseURL deliberately stays on localhost (not 127.0.0.1): the specs need cookie-host parity
  // with E2E_API, which also targets localhost — changing just one side would break auth cookies.
  use: { baseURL: 'http://localhost:5173', trace: 'on-first-retry' },
  webServer: {
    command: 'pnpm dev',
    // Vite itself is pinned to the IPv4 loopback (see vite.config.ts); poll that same address
    // rather than localhost so this readiness check can't resolve to a different interface.
    url: 'http://127.0.0.1:5173',
    reuseExistingServer: true,
    timeout: 60_000,
  },
  projects: [
    // Framework-only: runs against the default template, no content collections required.
    { name: 'core', testDir: './e2e', testIgnore: '**/e2e/sample/**' },
    // Requires the Blog sample to be opted in — see docs/guide/en/16-sample-walkthrough.md.
    { name: 'sample', testDir: './e2e/sample' },
  ],
})
