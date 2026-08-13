import { test, expect } from './fixtures'
import { type Page } from '@playwright/test'

// Live gate: Site Settings branding editor (/settings, super-admin only) — save-then-persist,
// and the same unsaved-changes leave guard pattern as ItemFormView
// (SettingsView.vue's guardLeave() reuses ItemFormView's lib/formDirty.ts unsavedConfirm copy).
//
// CRITICAL: `site_settings` is a SINGLETON row shared by the whole dev DB — every write here
// mutates state every other spec/run also sees (e.g. the login page's brand mark, the topbar).
// afterEach unconditionally PUTs the ORIGINAL branding back via the authenticated API so this
// spec never leaks a stamped brand name into the shared environment.

const EMAIL = process.env.E2E_EMAIL ?? 'admin@admin.com'
const PASSWORD = process.env.E2E_PASSWORD ?? 'admin'
const STAMP = process.env.E2E_STAMP ?? 'e2e'
// See sample/conflict.spec.ts: localhost (not 127.0.0.1) so page.request carries the app's auth cookie.
const API = process.env.E2E_API ?? 'http://localhost:5221'

const GUARD_HEADER = 'Unsaved changes'

type Branding = { brandName: string; logoFileId: string | null }

// Captured by whichever test mutates branding; restored (and cleared) in afterEach. `undefined`
// means "nothing pending" — either no test has run yet, or the previous one already restored it.
let original: Branding | undefined

async function login(page: Page): Promise<void> {
  await page.goto('/')
  await expect(page).toHaveURL(/\/login$/)
  await page.fill('input[type="email"]', EMAIL)
  await page.fill('input[type="password"]', PASSWORD)
  await page.click('button[type="submit"]')
  await expect(page).toHaveURL(/\/$/)
}

function labelMatch(label: string): RegExp {
  return new RegExp(`^${label.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\*?$`)
}
// SettingsView.vue's fields use a plain (unlinked) `<label class="settings-field">`, same convention
// as ItemForm.vue's `.field` — scope by the wrapper that has the matching label text.
function fieldByLabel(page: Page, label: string) {
  return page.locator('.settings-field', { has: page.getByText(labelMatch(label)) })
}
function brandNameInput(page: Page) {
  return fieldByLabel(page, 'Site name').locator('input')
}
function saveButton(page: Page) {
  // SettingsView.vue's Save button carries data-test="save"; Playwright's getByTestId defaults to the
  // data-testid attribute (and the config sets no testIdAttribute), so match the attribute directly.
  return page.locator('[data-test="save"]')
}
function guardDialog(page: Page) {
  return page.getByRole('alertdialog').filter({ hasText: GUARD_HEADER })
}

// Reads the LIVE persisted branding via the anonymous GET /api/config endpoint (the same one the
// login page / topbar consume) — bypasses the SPA's Pinia store entirely, so a positive assertion
// against it proves durability, not just an in-memory echo of what we just PUT.
async function apiGetBranding(page: Page): Promise<{ brandName: string; brandLogoUrl: string | null }> {
  const res = await page.request.get(`${API}/api/config`)
  expect(res.ok(), `GET /api/config -> ${res.status()}`).toBeTruthy()
  // Unwrap the API envelope: EnvelopeResultFilter wraps every response as { success, data:{...} }.
  // Reading res.json() directly yielded { brandName: undefined }, which corrupted captureOriginal()
  // and made afterEach's restore PUT a blank name (400, swallowed) — leaking the stamped brand name
  // into the shared singleton row.
  const body = await res.json()
  return body.data
}

// Snapshots the current persisted branding as the {brandName, logoFileId} shape PUT /settings/
// branding accepts (SettingsView.vue recovers logoFileId from the config's logo URL the same way).
async function captureOriginal(page: Page): Promise<Branding> {
  const cfg = await apiGetBranding(page)
  const m = cfg.brandLogoUrl?.match(/\/api\/files\/([^/]+)\/content/)
  return { brandName: cfg.brandName, logoFileId: m ? m[1] : null }
}

test.afterEach(async ({ page }) => {
  if (!original) return
  await page.request
    .put(`${API}/api/settings/branding`, {
      headers: { 'X-Struo-CSRF': '1', 'Content-Type': 'application/json' },
      data: original,
    })
    .catch(() => undefined)
  original = undefined
})

test('saving a new brand name persists across a full reload', async ({ page }) => {
  await login(page)
  await page.goto('/settings')
  await expect(page).toHaveURL(/\/settings$/)

  original = await captureOriginal(page)
  const newName = `E2E Brand ${STAMP}`

  await brandNameInput(page).fill(newName)
  await saveButton(page).click()
  await expect(page.getByText('Settings saved', { exact: true })).toBeVisible()

  // A full reload re-runs main.ts's bootstrap (auth.fetchCurrentUser + appConfig.load) from scratch,
  // so SettingsView's initial value can only come from a fresh GET, not leftover Pinia state.
  await page.reload()
  await expect(page).toHaveURL(/\/settings$/)
  await expect(brandNameInput(page)).toHaveValue(newName)

  // Independent confirmation via the anonymous config endpoint: the singleton row itself changed.
  expect((await apiGetBranding(page)).brandName).toBe(newName)
})

test('navigating away with an unsaved edit prompts the guard; reject keeps the edit and the page', async ({ page }) => {
  await login(page)
  await page.goto('/settings')
  await expect(page).toHaveURL(/\/settings$/)

  // Nothing is ever saved in this test, but capture + restore unconditionally anyway (safety net —
  // see file header) rather than special-casing the "read-only" test.
  original = await captureOriginal(page)
  const edited = `${original.brandName} EDITED ${STAMP}`
  await brandNameInput(page).fill(edited)

  // Navigate away (SPA route change -> onBeforeRouteLeave -> the leave guard prompts). Use the
  // breadcrumb "Dashboard" home crumb (AppBreadcrumb's vendored Breadcrumb, a router command), not
  // the sidebar (which is an <aside>, no <nav>). Scoped to the breadcrumb's own aria-label="breadcrumb"
  // nav landmark (src/components/ui/breadcrumb/Breadcrumb.vue) so it stays unambiguous.
  await page.getByRole('navigation', { name: /breadcrumb/i }).getByText('Dashboard', { exact: true }).click()
  await expect(guardDialog(page)).toBeVisible()

  // Reject -> stays on /settings, edit preserved, no navigation happened. ConfirmHost renders
  // the reject button with the shared common.cancel label ("Cancel").
  await guardDialog(page).getByRole('button', { name: 'Cancel' }).click()
  await expect(guardDialog(page)).toHaveCount(0)
  await expect(page).toHaveURL(/\/settings$/)
  await expect(brandNameInput(page)).toHaveValue(edited)
})
