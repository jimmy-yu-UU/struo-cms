import { existsSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { defineConfig } from '@playwright/test'

// Present only while `e2e/sample/` exists; deleting it removes the project.
const sampleSpecsDir = fileURLToPath(new URL('./e2e/sample', import.meta.url))
const hasSampleSpecs = existsSync(sampleSpecsDir)

// Set E2E_BASE_URL to run against an already-deployed stack (e.g. the compose admin at
// http://localhost:8080); no dev server is started then. Unset, the specs run against `pnpm dev`.
const externalBaseURL = process.env.E2E_BASE_URL

export default defineConfig({
  // Single worker: the specs share one Vite dev server and one seeded admin account. The dev
  // server transforms each Vue SFC/module on demand on first request; several Playwright workers
  // navigating at once all trigger that cold compile simultaneously and can starve the single
  // dev-server process past a normal navigation timeout. Serializing is a small cost for a suite
  // this size and removes the flakiness entirely.
  workers: 1,
  // baseURL defaults to localhost (not 127.0.0.1): the specs need cookie-host parity with E2E_API,
  // which also targets localhost — E2E_BASE_URL and E2E_API must use the same host, or auth
  // cookies are not shared.
  use: { baseURL: externalBaseURL ?? 'http://localhost:5173', trace: 'on-first-retry' },
  webServer: externalBaseURL
    ? undefined
    : {
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
    // Present only while `e2e/sample/` exists. Requires the Blog sample to be opted in — see
    // docs/guide/en/23-sample-walkthrough.md.
    ...(hasSampleSpecs ? [{ name: 'sample', testDir: './e2e/sample' }] : []),
  ],
})
