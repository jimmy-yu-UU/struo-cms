import { test, expect } from '../fixtures'
import { type Page } from '@playwright/test'

const EMAIL = process.env.E2E_EMAIL ?? 'admin@admin.com'
const PASSWORD = process.env.E2E_PASSWORD ?? 'admin'
const STAMP = process.env.E2E_STAMP ?? 'e2e'

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
function translatableFieldByLabel(page: Page, label: string) {
  return page.locator('.field:visible', { has: page.getByText(labelMatch(label)) })
}
async function chooseStatus(page: Page, optionLabel: 'Draft' | 'Published'): Promise<void> {
  const field = page.locator('.field', { has: page.getByText(labelMatch('Status')) })
  await field.getByRole('combobox').click()
  await page.getByRole('option', { name: optionLabel }).click()
}
// A DataTable row scoped by its visible title cell.
function rowByTitle(page: Page, title: string) {
  return page.getByRole('row', { has: page.getByText(title, { exact: true }) })
}
// Search filters apply on an explicit press (or Enter), never on keystroke.
async function searchByField(page: Page, fieldLabel: string, value: string): Promise<void> {
  await page.getByRole('button', { name: 'Add condition' }).click()
  await page.getByRole('combobox', { name: 'Field' }).click()
  await page.getByRole('option', { name: fieldLabel, exact: true }).click()
  await page.getByRole('textbox', { name: 'Value' }).fill(value)
  await page.getByRole('button', { name: 'Search' }).click()
}

test('soft-delete an article, see it in trash, restore, then purge', async ({ page }) => {
  await login(page)
  const title = `E2E Trash ${STAMP}`

  // Create.
  await page.goto('/collections/article')
  await page.getByRole('button', { name: 'New' }).click()
  await chooseStatus(page, 'Draft')
  await translatableFieldByLabel(page, 'Title').locator('input').fill(title)
  // Body is a non-required RichText (TipTap) field — skip it; Title is the only required translatable field.
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page).toHaveURL(/\/collections\/article$/)

  // Populated dev DB → the new row may not be on list page 1. Title is searchable, so
  // filter to isolate it. The applied filter persists across the Active/Trash switch.
  await searchByField(page, 'Title', title)
  await expect(page.getByText(title, { exact: true })).toBeVisible()
  // The list transitions unfiltered (up to a full page of rows) → filtered once Search is
  // pressed. Acting during that transition lets a rowByTitle() action fire against the
  // mid-re-render DataTable and hit a strict-mode ambiguity. The stamp is unique, so wait for the
  // list to settle to exactly the one matching row before any inline row action.
  await expect(page.locator('tbody tr')).toHaveCount(1)

  // Soft-delete from the Active list (inline action). CollectionListView's row actions go
  // through the store-backed ConfirmHost, not PrimeVue's ConfirmDialog — its default accept label
  // (no explicit acceptLabel passed by lib/deleteAction.ts) is common.confirm = "Confirm", not
  // PrimeVue's "Yes". ItemFormView's own delete/unsaved-guard dialogs elsewhere in this suite
  // still say "Yes"/"No".
  await rowByTitle(page, title).getByRole('button', { name: 'Delete', exact: true }).click()
  await page.getByRole('button', { name: 'Confirm' }).click()
  await expect(page.getByText(title, { exact: true })).toHaveCount(0)

  // Switch to Trash — the row is there.
  await page.getByText('Trash', { exact: true }).click()
  await expect(page.getByText(title, { exact: true })).toBeVisible()

  // Restore — leaves the trash.
  await rowByTitle(page, title).getByRole('button', { name: 'Restore', exact: true }).click()
  await expect(page.getByText(title, { exact: true })).toHaveCount(0)

  // Back to Active — it's live again.
  await page.getByText('Active', { exact: true }).click()
  await expect(page.getByText(title, { exact: true })).toBeVisible()

  // Soft-delete again, then purge it from Trash.
  await rowByTitle(page, title).getByRole('button', { name: 'Delete', exact: true }).click()
  await page.getByRole('button', { name: 'Confirm' }).click()
  await page.getByText('Trash', { exact: true }).click()
  await expect(page.getByText(title, { exact: true })).toBeVisible()
  await rowByTitle(page, title).getByRole('button', { name: 'Delete permanently', exact: true }).click()
  await page.getByRole('button', { name: 'Confirm' }).click()
  await expect(page.getByText(title, { exact: true })).toHaveCount(0)
})
