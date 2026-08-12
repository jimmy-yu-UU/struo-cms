import { test, expect } from '../fixtures'
import { type Page } from '@playwright/test'

// Live gate: revision history + revert.
//
// `article` is the sample's only [CmsCollection(Revisions = true)] collection, so every save
// captures a new revision INSIDE the write transaction (Struo.Application.Query.ItemService):
// revision #1 = the state right after the initial create, revision #2 = the state right after the
// first update. RevisionHistoryDrawer lists newest-first; selecting a row loads its full snapshot
// (RevisionSnapshotView) and, when the caller can write, offers "Revert to this revision".
// ItemFormView.onReverted() deliberately re-fetches the full item after a revert rather than
// trusting the POST /revert response (which omits translations/relations), so this spec's core
// assertion is that the FORM's Title field — not just a toast — shows the pre-edit value after
// revert, plus an independent API confirmation.

const EMAIL = process.env.E2E_EMAIL ?? 'admin@admin.com'
const PASSWORD = process.env.E2E_PASSWORD ?? 'admin'
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
// Status is a Select (role="combobox" trigger + role="option" overlay), not a text input.
async function chooseStatus(page: Page, optionLabel: 'Draft' | 'Published'): Promise<void> {
  const field = fieldByLabel(page, 'Status')
  await field.getByRole('combobox').click()
  await page.getByRole('option', { name: optionLabel }).click()
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
  await page.getByRole('row', { has: page.getByText(title, { exact: true }) }).getByRole('button', { name: 'Edit' }).click()
  await expect(page).toHaveURL(/\/collections\/article\/[0-9a-fA-F-]+$/)
  await expect(titleInput(page)).toHaveValue(title)
}

async function apiGet(page: Page, id: string): Promise<{ title: string }> {
  const res = await page.request.get(`${API}/api/items/article/${id}`)
  expect(res.ok(), `GET article ${id} -> ${res.status()}`).toBeTruthy()
  const body = await res.json()
  return { title: body.data.translations.en.title as string }
}

// The History trigger (ItemFormView.vue's PageHeader action bar), gated `!isCreate && meta.revisions`.
function historyButton(page: Page) {
  return page.getByRole('button', { name: 'History' })
}
// A revision list row: RevisionHistoryDrawer renders `<button class="rev-item">` per revision with a
// `#{n}` badge span — scope by that exact text so #1 never matches #10+ (not a concern here with only
// a couple of revisions, but keeps the selector honest).
function revisionRow(page: Page, n: number) {
  return page.locator('.rev-item', { has: page.getByText(`#${n}`, { exact: true }) })
}
function revertButton(page: Page) {
  return page.getByRole('button', { name: 'Revert to this revision' })
}
// RevisionHistoryDrawer still renders its own PrimeVue <ConfirmDialog group="revisions"> (its own
// migration is a later slice). ItemFormView no longer mounts an unscoped <ConfirmDialog> of its
// own — that confirm now goes through the app-wide ConfirmHost instead — so this group scoping no
// longer prevents a double-fire against anything on this page; it is simply this dialog's own
// PrimeVue instance, independent of ConfirmHost.
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

test('reverting to an earlier revision restores its Title in the form and on the server', async ({ page }) => {
  await login(page)
  const original = `E2E Revision Original ${STAMP}`
  const edited = `${original} EDITED`
  const id = await createAndOpen(page, original) // revision #1 (create): Title = original

  // Edit the Title and save -> revision #2 (update): Title = edited.
  await titleInput(page).fill(edited)
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page).toHaveURL(/\/collections\/article$/)

  // Reopen by the now-current (edited) Title.
  await openByTitle(page, edited)

  await historyButton(page).click()
  await expect(revisionRow(page, 2)).toBeVisible()
  await expect(revisionRow(page, 1)).toBeVisible()

  // Select the OLDER revision (#1, the original create snapshot) and revert to it.
  await revisionRow(page, 1).click()
  await expect(revertButton(page)).toBeVisible()
  await revertButton(page).click()
  await expect(revertConfirmDialog(page)).toBeVisible()
  // Targets RevisionHistoryDrawer's own PrimeVue ConfirmDialog, not ConfirmHost — its accept
  // label is still PrimeVue's default ("Yes"), unrelated to ItemFormView's confirm.require().
  await revertConfirmDialog(page).getByRole('button', { name: 'Yes' }).click()

  // onReverted() re-fetches the full item (NOT the bare /revert response) and re-populates the form:
  // the Title field must show the ORIGINAL value again, proving the revert (and the re-fetch) worked.
  await expect(revertConfirmDialog(page)).toHaveCount(0)
  await expect(titleInput(page)).toHaveValue(original)

  // Independent server-side confirmation: the live row's Title actually reverted, not just the form.
  expect((await apiGet(page, id)).title).toBe(original)
})
