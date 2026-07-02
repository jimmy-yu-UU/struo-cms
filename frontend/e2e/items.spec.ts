import { test, expect, type Page } from '@playwright/test'

const EMAIL = process.env.E2E_EMAIL ?? 'admin@struo.local'
const PASSWORD = process.env.E2E_PASSWORD ?? 'change-me-please'
// Caller passes a unique value per run so create-then-delete is self-cleaning
// even if two runs overlap (e.g. CI + a local run against the same DB).
const STAMP = process.env.E2E_STAMP ?? 'e2e'

async function login(page: Page): Promise<void> {
  await page.goto('/')
  await expect(page).toHaveURL(/\/login$/)
  await page.fill('input[type="email"]', EMAIL)
  await page.fill('input[type="password"]', PASSWORD)
  await page.click('button[type="submit"]')
  await expect(page).toHaveURL(/\/$/)
}

// ItemForm.vue renders each editable field inside a `.field` wrapper with a
// plain (unlinked — no `for`/`id` pair on the rendered PrimeVue control)
// `<label>`, so `getByLabel()` cannot resolve these controls. Scope by the
// `.field` container that has the matching label text instead.
function fieldByLabel(page: Page, label: string) {
  return page.locator('.field', { has: page.getByText(label, { exact: true }) })
}

// Article.Status is [CmsField(Interface = FieldInterface.Select)] with
// CmsOptions("draft:Draft", "published:Published") -> FieldInput renders a
// PrimeVue <Select> (a role="combobox" trigger + role="listbox"/"option"
// overlay), NOT a text input, so it cannot be `.fill()`ed. Click the trigger,
// then click the option by its visible label.
async function chooseStatus(page: Page, optionLabel: 'Draft' | 'Published'): Promise<void> {
  const field = fieldByLabel(page, 'Status')
  await field.getByRole('combobox').click()
  await page.getByRole('option', { name: optionLabel }).click()
}

test('create, edit, then delete an article', async ({ page }) => {
  await login(page)

  // Browse to the article collection, then create.
  await page.goto('/collections/article')
  await expect(page).toHaveURL(/\/collections\/article$/)
  await page.getByRole('button', { name: 'New' }).click()
  await expect(page).toHaveURL(/\/collections\/article\/new$/)

  // Shared field (Status, a Select) + default-locale required translatable
  // fields (Title/Body live under the first — default-locale — Tabs panel,
  // which is active by default per ItemForm.vue's `activeLocale` ref).
  const title = `E2E Title ${STAMP}`
  await chooseStatus(page, 'Draft')
  await fieldByLabel(page, 'Title').locator('input').fill(title)
  await fieldByLabel(page, 'Body').locator('textarea').fill('E2E body content.')
  await page.getByRole('button', { name: 'Save' }).click()

  // Back on the list; the new row is present (Title is a scalar, non-system
  // field so selectListColumns() includes it as a column).
  await expect(page).toHaveURL(/\/collections\/article$/)
  await expect(page.getByText(title)).toBeVisible()

  // Open it, edit the shared field, save.
  await page.getByText(title).click()
  await expect(page).toHaveURL(/\/collections\/article\/[^/]+$/)
  await chooseStatus(page, 'Published')
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page).toHaveURL(/\/collections\/article$/)

  // Delete it.
  await page.getByText(title).click()
  await page.getByRole('button', { name: 'Delete' }).click()
  // PrimeVue's default locale (@primevue/core config) sets acceptLabel = "Yes";
  // ItemFormView.vue's confirm.require() doesn't override it.
  await page.getByRole('button', { name: 'Yes' }).click()
  await expect(page).toHaveURL(/\/collections\/article$/)
  await expect(page.getByText(title)).toHaveCount(0)
})
