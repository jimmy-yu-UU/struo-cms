// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

// Scoped to LoginView.vue alone -- deliberately NOT the rest of src/. components/rbac/,
// components/revisions/, SettingsView.vue and theme.css itself still consume --legacy-* tokens;
// converting those is separate, unstarted work (this plan's task 8.5). This guard overlaps
// tests/legacyTokens.test.ts (which only forbids the RENAMED post-migration names, not the
// --legacy-* prefix itself) and becomes redundant once every --legacy-* consumer is gone and
// theme.css's --legacy-* block is deleted -- delete it at that point instead of keeping a check
// with nothing left to catch.
const LOGIN_VIEW = fileURLToPath(new URL('../src/views/LoginView.vue', import.meta.url))

describe('login view legacy token conversion', () => {
  it('LoginView.vue consumes no --legacy- token', () => {
    const css = readFileSync(LOGIN_VIEW, 'utf8')
    expect(css).not.toMatch(/var\(--legacy-/)
  })
})
