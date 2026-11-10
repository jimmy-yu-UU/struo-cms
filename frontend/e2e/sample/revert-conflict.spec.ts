import { test, expect } from '../fixtures'
import { type Page } from '@playwright/test'
import { e2ePassword } from '../credentials'
import { chooseStatus } from './chooseStatus'

// Live gate: a revert that hits 409 VERSION_CONFLICT.
//
// Another editor changes the item after the form loaded, then the form's user reverts to an earlier
// revision. The drawer's revert is rejected (409), ItemFormView shows its dedicated notice with a
// reload action only, and the server keeps the other editor's value: the UI never steers the user
// into saving over it. Same collection and helpers as revisions.spec.ts (`article`, Revisions = true).

const EMAIL = process.env.E2E_EMAIL ?? 'admin@admin.com'
const PASSWORD = e2ePassword()
const STAMP = process.env.E2E_STAMP ?? 'e2e'
// Backend origin for out-of-band API calls. See conflict.spec.ts: localhost (not 127.0.0.1) so
// page.request carries the app's auth cookie (host-only, ignores port).
const API = process.env.E2E_API ?? 'http://localhost:5221'

let createdId: string | undefined

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
// Translatable fields (Title/Body) live inside ItemForm.vue's non-lazy <Tabs>: every locale's
// TabPanel stays mounted (only `display:none` toggled), so an unscoped `.field` match resolves to
// one wrapper per locale (strict-mode violation). Scope to `:visible` so only the active
// (default-locale) tab's field matches. See conflict.spec.ts for the full rationale.
function translatableFieldByLabel(page: Page, label: string) {
  return page.locator('.field:visible', { has: page.getByText(labelMatch(label)) })
}
function titleInput(page: Page) {
  return translatableFieldByLabel(page, 'Title').locator('input')
}

// Create an article via the UI, then open it so the edit form (and its version token) is loaded.
// Published At is left blank on purpose — an empty DateTime must serialise to null, not "";
// a regression here would 400 the Save before the revision logic is ever exercised.
async function createAndOpen(page: Page, title: string): Promise<string> {
  await page.goto('/collections/article/new')
  await chooseStatus(page, 'Draft')
  await titleInput(page).fill(title)
  // Body is a RichText (TipTap) contenteditable — click and type (fill() does not work on it).
  const body = translatableFieldByLabel(page, 'Body').locator('.ProseMirror')
  await body.click()
  await page.keyboard.type('E2E revisions body content.')
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page).toHaveURL(/\/collections\/article$/)

  await openByTitle(page, title)
  const id = page.url().match(/\/collections\/article\/([0-9a-fA-F-]+)$/)![1]
  createdId = id
  return id
}

// Search filters apply on an explicit press (or Enter), never on keystroke.
async function searchByField(page: Page, fieldLabel: string, value: string): Promise<void> {
  await page.getByRole('button', { name: 'Add condition' }).click()
  await page.getByRole('combobox', { name: 'Field' }).click()
  await page.getByRole('option', { name: fieldLabel, exact: true }).click()
  await page.getByRole('textbox', { name: 'Value' }).fill(value)
  await page.getByRole('button', { name: 'Search' }).click()
}

// Populated dev DB + pagination: isolate the row by its searchable Title, then open it and wait
// for init()'s async GET to populate the form before any field interaction.
async function openByTitle(page: Page, title: string): Promise<void> {
  await searchByField(page, 'Title', title)
  await expect(page.getByText(title, { exact: true })).toBeVisible()
  // The collection list has no row-click navigation — open via the row's explicit
  // Edit action instead. Wait for the filtered search to settle to the single matching row first
  // (trash.spec.ts idiom) — otherwise the row locator can transiently match the still-unfiltered page.
  await expect(page.locator('tbody tr')).toHaveCount(1)
  await page.getByRole('row').filter({ has: page.getByText(title, { exact: true }) }).getByRole('button', { name: 'Edit' }).click()
  await expect(page).toHaveURL(/\/collections\/article\/[0-9a-fA-F-]+$/)
  await expect(titleInput(page)).toHaveValue(title)
}

async function apiGet(page: Page, id: string): Promise<{ version: number; title: string }> {
  const res = await page.request.get(`${API}/api/items/article/${id}`)
  expect(res.ok(), `GET article ${id} -> ${res.status()}`).toBeTruthy()
  const body = await res.json()
  return { version: body.data.version as number, title: body.data.translations.en.title as string }
}

// The History trigger (ItemFormView.vue's PageHeader action bar), gated `!isCreate && meta.revisions`.
function historyButton(page: Page) {
  return page.getByRole('button', { name: 'History' })
}
// A revision list row: RevisionHistoryDrawer renders `<button class="rev-item">` per revision with a
// `#{n}` badge span — scope by that exact text so a one-digit revision's badge never matches a
// two-digit one starting with the same digit (not a concern here with only a couple of revisions,
// but keeps the selector honest).
function revisionRow(page: Page, n: number) {
  return page.locator('.rev-item', { has: page.getByText(`#${n}`, { exact: true }) })
}
function revertButton(page: Page) {
  return page.getByRole('button', { name: 'Revert to this revision' })
}
// The revert confirmation routes through the app-wide ConfirmHost (store-backed
// AlertDialog), the same single host every other confirmation on this page uses.
function revertConfirmDialog(page: Page) {
  return page.getByRole('alertdialog').filter({ hasText: 'Confirm revert' })
}

test.afterEach(async ({ page }) => {
  if (!createdId) return
  await page.request
    .delete(`${API}/api/items/article/${createdId}?purge=true`, { headers: { 'X-Struo-CSRF': '1' } })
    .catch(() => undefined)
  createdId = undefined
})

test('a revert rejected with 409 shows the dedicated notice and leaves the other change on the server', async ({ page }) => {
  await login(page)
  const original = `E2E Revert Conflict ${STAMP}`
  const id = await createAndOpen(page, original) // revision #1: Title = original

  // Edit and save -> revision #2, then reopen so the form carries the post-save version.
  const edited = `${original} EDITED`
  await titleInput(page).fill(edited)
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page).toHaveURL(/\/collections\/article$/)
  await openByTitle(page, edited)

  // Another editor saves after our form loaded, so our version is stale.
  const cur = await apiGet(page, id)
  const remoteTitle = `${original} REMOTE`
  const put = await page.request.put(`${API}/api/items/article/${id}`, {
    headers: { 'X-Struo-CSRF': '1', 'Content-Type': 'application/json' },
    data: { translations: { en: { title: remoteTitle } }, version: cur.version },
  })
  expect(put.ok(), `out-of-band PUT -> ${put.status()} ${await put.text()}`).toBeTruthy()

  // Revert to revision #1 from the stale form -> 409.
  await historyButton(page).click()
  await revisionRow(page, 1).click()
  await revertButton(page).click()
  await expect(revertConfirmDialog(page)).toBeVisible()
  await revertConfirmDialog(page).getByRole('button', { name: 'Confirm' }).click()

  // Dedicated notice only: no save-conflict banner, form keeps the loaded value.
  const notice = page.locator('.revert-conflict-banner')
  await expect(notice).toBeVisible()
  await expect(notice).toContainText('Revert failed')
  await expect(page.locator('.conflict-banner')).toHaveCount(0)
  await expect(titleInput(page)).toHaveValue(edited)

  // The server still holds the other editor's value.
  expect((await apiGet(page, id)).title).toBe(remoteTitle)

  // Reload shows the latest server copy and clears the notice.
  await notice.getByRole('button', { name: 'Reload latest' }).click()
  await expect(notice).toHaveCount(0)
  await expect(titleInput(page)).toHaveValue(remoteTitle)
})
