import { defineConfig } from '@playwright/test'

export default defineConfig({
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
