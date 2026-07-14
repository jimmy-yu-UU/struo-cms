import { test, expect, type Page } from '@playwright/test'

const EMAIL = process.env.E2E_EMAIL ?? 'admin@struo.local'
const PASSWORD = process.env.E2E_PASSWORD ?? 'change-me-please'
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
  // filter to isolate it. The search state persists across the Active/Trash switch.
  await page.getByPlaceholder('Search').fill(title)
  await expect(page.getByText(title, { exact: true })).toBeVisible()

  // Soft-delete from the Active list (inline action). Confirm = "Yes".
  await rowByTitle(page, title).getByRole('button', { name: 'Delete', exact: true }).click()
  await page.getByRole('button', { name: 'Yes' }).click()
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
  await page.getByRole('button', { name: 'Yes' }).click()
  await page.getByText('Trash', { exact: true }).click()
  await expect(page.getByText(title, { exact: true })).toBeVisible()
  await rowByTitle(page, title).getByRole('button', { name: 'Delete permanently', exact: true }).click()
  await page.getByRole('button', { name: 'Yes' }).click()
  await expect(page.getByText(title, { exact: true })).toHaveCount(0)
})
