import { defineConfig } from '@playwright/test'

export default defineConfig({
  // Single worker: the specs share one Vite dev server and one seeded admin account. The dev
  // server transforms each Vue SFC/module on demand on first request; several Playwright workers
  // navigating at once all trigger that cold compile simultaneously and can starve the single
  // dev-server process past a normal navigation timeout. Serializing is a small cost for a suite
  // this size and removes the flakiness entirely.
  workers: 1,
  use: { baseURL: 'http://localhost:5173', trace: 'on-first-retry' },
  webServer: {
    command: 'pnpm dev',
    url: 'http://localhost:5173',
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
