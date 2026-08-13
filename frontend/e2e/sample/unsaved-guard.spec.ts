import { test, expect } from '../fixtures'
import { type Page } from '@playwright/test'

// Live gate: dirty-state leave guard.
//
// ItemFormView snapshots the model after load; onBeforeRouteLeave compares the current model and,
// when dirty, prompts the app-wide ConfirmHost ("Unsaved changes"). Cancel keeps the user on the
// form; Confirm lets the navigation proceed. An untouched form (incl. a loaded RichText/TipTap body)
// must NOT prompt, and a successful Save must navigate without prompting.

const EMAIL = process.env.E2E_EMAIL ?? 'admin@admin.com'
const PASSWORD = process.env.E2E_PASSWORD ?? 'admin'
const STAMP = process.env.E2E_STAMP ?? 'e2e'
// See conflict.spec.ts: localhost (not 127.0.0.1) so page.request carries the app's auth cookie.
const API = process.env.E2E_API ?? 'http://localhost:5221'

const GUARD_HEADER = 'Unsaved changes'

// Tracks the item created by the current test so afterEach can purge it from the right collection.
let created: { collection: string; id: string } | undefined

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
function fieldByLabel(page: Page, label: string) {
  return page.locator('.field', { has: page.getByText(labelMatch(label)) })
}
function translatableFieldByLabel(page: Page, label: string) {
  return page.locator('.field:visible', { has: page.getByText(labelMatch(label)) })
}
async function chooseStatus(page: Page, optionLabel: 'Draft' | 'Published'): Promise<void> {
  const field = fieldByLabel(page, 'Status')
  await field.getByRole('combobox').click()
  await page.getByRole('option', { name: optionLabel }).click()
}
// Search filters apply on an explicit press (or Enter), never on keystroke.
async function searchByField(page: Page, fieldLabel: string, value: string): Promise<void> {
  await page.getByRole('button', { name: 'Add condition' }).click()
  await page.getByRole('combobox', { name: 'Field' }).click()
  await page.getByRole('option', { name: fieldLabel, exact: true }).click()
  await page.getByRole('textbox', { name: 'Value' }).fill(value)
  await page.getByRole('button', { name: 'Search' }).click()
}

// Create an article WITH a RichText body (so the reopened form exercises TipTap load), then open it.
// Returns the item id and leaves the page on the freshly-loaded (clean) edit form.
async function createAndOpen(page: Page, title: string): Promise<string> {
  await page.goto('/collections/article/new')
  await chooseStatus(page, 'Draft')
  await translatableFieldByLabel(page, 'Title').locator('input').fill(title)
  // Body is a RichText (TipTap) contenteditable — click and type (fill() does not work on it).
  const body = translatableFieldByLabel(page, 'Body').locator('.ProseMirror')
  await body.click()
  await page.keyboard.type('E2E guard body content.')
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page).toHaveURL(/\/collections\/article$/)

  await searchByField(page, 'Title', title)
  await expect(page.getByText(title, { exact: true })).toBeVisible()
  // The collection list has no row-click navigation — open via the row's explicit
  // Edit action instead. Wait for the filtered search to settle to the single matching row first
  // (trash.spec.ts idiom) — otherwise the row locator can transiently match the still-unfiltered page.
  await expect(page.locator('tbody tr')).toHaveCount(1)
  await page.getByRole('row', { has: page.getByText(title, { exact: true }) }).getByRole('button', { name: 'Edit' }).click()
  await expect(page).toHaveURL(/\/collections\/article\/[0-9a-fA-F-]+$/)
  // Wait until init() finished loading (Title populated) so the dirty baseline is captured against
  // the fully-loaded model, not a half-initialised one.
  await expect(translatableFieldByLabel(page, 'Title').locator('input')).toHaveValue(title)
  const id = page.url().match(/\/collections\/article\/([0-9a-fA-F-]+)$/)![1]
  created = { collection: 'article', id }
  return id
}

// Create a `category` (single required text field) via the UI, then open it. Used by the save test:
// article's UI update is blocked by the pre-existing empty-DateTime bug (see conflict.spec.ts /
// gate-e2e-report.md), whereas category round-trips cleanly.
async function createAndOpenCategory(page: Page, name: string): Promise<string> {
  await page.goto('/collections/category/new')
  await fieldByLabel(page, 'Name').locator('input').fill(name)
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page).toHaveURL(/\/collections\/category$/)

  await searchByField(page, 'Name', name)
  await expect(page.getByText(name, { exact: true })).toBeVisible()
  // The collection list has no row-click navigation — open via the row's explicit
  // Edit action instead. Wait for the filtered search to settle to the single matching row first
  // (trash.spec.ts idiom) — otherwise the row locator can transiently match the still-unfiltered page.
  await expect(page.locator('tbody tr')).toHaveCount(1)
  await page.getByRole('row', { has: page.getByText(name, { exact: true }) }).getByRole('button', { name: 'Edit' }).click()
  await expect(page).toHaveURL(/\/collections\/category\/[0-9a-fA-F-]+$/)
  await expect(fieldByLabel(page, 'Name').locator('input')).toHaveValue(name)
  const id = page.url().match(/\/collections\/category\/([0-9a-fA-F-]+)$/)![1]
  created = { collection: 'category', id }
  return id
}

// Click a collection link in the sidebar: a real <nav aria-label="Main navigation"> landmark
// wraps the nav groups (no bare "the sidebar" element to select by tag/class), and collection
// links render as plain buttons (SidebarNavItem), not a PrimeVue PanelMenu. Groups mount expanded
// by default (TheSidebar isOpen = open[group] ?? true), so the leaf is always visible on a fresh
// mount.
async function navSidebar(page: Page, label: string): Promise<void> {
  const sidebar = page.getByRole('navigation', { name: 'Main navigation' })
  const item = sidebar.getByRole('button', { name: label, exact: true })
  await expect(item).toBeVisible()
  await item.click()
}

function guardDialog(page: Page) {
  return page.getByRole('alertdialog').filter({ hasText: GUARD_HEADER })
}

test.afterEach(async ({ page }) => {
  if (!created) return
  await page.request
    .delete(`${API}/api/items/${created.collection}/${created.id}?purge=true`, {
      headers: { 'X-Struo-CSRF': '1' },
    })
    .catch(() => undefined)
  created = undefined
})

test('editing then navigating away prompts; reject stays and preserves edits, accept proceeds', async ({ page }) => {
  await login(page)
  const title = `E2E Guard Dirty ${STAMP}`
  await createAndOpen(page, title)

  // Make the form dirty.
  const titleInput = translatableFieldByLabel(page, 'Title').locator('input')
  await titleInput.fill(`${title} edited`)

  // Navigate via the sidebar -> guard prompts.
  await navSidebar(page, 'Category')
  await expect(guardDialog(page)).toBeVisible()

  // Reject -> stay on the form, edit preserved, still the same article URL.
  // This dialog is ItemFormView's own guardLeave() confirm, which resolves through the local
  // confirmStore/ConfirmHost — its Cancel button defaults to common.cancel ("Cancel"), not
  // PrimeVue's "No".
  await page.getByRole('button', { name: 'Cancel' }).click()
  await expect(guardDialog(page)).toHaveCount(0)
  await expect(page).toHaveURL(/\/collections\/article\/[0-9a-fA-F-]+$/)
  await expect(titleInput).toHaveValue(`${title} edited`)

  // Navigate again and accept -> navigation proceeds.
  await navSidebar(page, 'Category')
  await expect(guardDialog(page)).toBeVisible()
  // Same ConfirmHost dialog; its accept button defaults to common.confirm ("Confirm").
  await page.getByRole('button', { name: 'Confirm' }).click()
  await expect(page).toHaveURL(/\/collections\/category$/)
})

test('an untouched edit form (incl. loaded RichText body) navigates without a guard prompt', async ({ page }) => {
  await login(page)
  const title = `E2E Guard Clean ${STAMP}`
  await createAndOpen(page, title)

  // Touch nothing. Navigate away.
  await navSidebar(page, 'Category')

  // No guard dialog must appear; navigation must proceed. If the dialog shows, the untouched form
  // (RichText load) is falsely dirty — a REAL finding, captured with a screenshot.
  if (await guardDialog(page).isVisible().catch(() => false)) {
    await page.screenshot({ path: 'test-results/unsaved-guard-false-dirty-untouched.png', fullPage: true })
    throw new Error('FINDING: untouched edit form triggered the Unsaved-changes guard (false-dirty)')
  }
  await expect(page).toHaveURL(/\/collections\/category$/)
})

test('a successful Save navigates without a guard prompt', async ({ page }) => {
  await login(page)
  const name = `E2E Guard Save ${STAMP}`
  // Category rather than article — a clean update path (see createAndOpenCategory).
  await createAndOpenCategory(page, name)

  // Edit then Save -> app navigates to the list with no prompt (baseline re-captured before nav).
  await fieldByLabel(page, 'Name').locator('input').fill(`${name} saved`)
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page).toHaveURL(/\/collections\/category$/)
  await expect(page.getByText(GUARD_HEADER)).toHaveCount(0)
})

test('Escape dismisses the guard (user stays); a later navigation re-triggers it', async ({ page }) => {
  await login(page)
  const title = `E2E Guard Esc ${STAMP}`
  await createAndOpen(page, title)

  await translatableFieldByLabel(page, 'Title').locator('input').fill(`${title} esc`)

  await navSidebar(page, 'Category')
  await expect(guardDialog(page)).toBeVisible()

  // Escape should dismiss (reject) the dialog and keep the user on the form.
  await page.keyboard.press('Escape')
  await expect(guardDialog(page)).toHaveCount(0)
  await expect(page).toHaveURL(/\/collections\/article\/[0-9a-fA-F-]+$/)

  // Guard is still functional: navigating again re-triggers it.
  await navSidebar(page, 'Category')
  await expect(guardDialog(page)).toBeVisible()
  // Clean up navigation state by accepting. ConfirmHost's accept button defaults to
  // common.confirm ("Confirm").
  await page.getByRole('button', { name: 'Confirm' }).click()
  await expect(page).toHaveURL(/\/collections\/category$/)
})
