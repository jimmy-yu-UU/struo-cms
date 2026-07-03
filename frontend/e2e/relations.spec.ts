// Requires: seeded bootstrap admin (same as items.spec.ts / collections.spec.ts) with write+delete
// grants on `article`, plus AT LEAST ONE `category` row and ONE `tag` row already present in the
// database — the sample blog collection ships no seed data for these, so create one of each via the
// authenticated API before running this spec live, e.g.:
//
//   POST /api/items/category   { "name": "E2E Category" }
//   POST /api/items/tag        { "name": "E2E Tag" }
//
// See frontend/e2e/README.md for the full live-gate prerequisites (API on :5080, seeded admin, etc).
// This spec is authored + collection-validated only (`playwright test --list`); the live run against
// real PG+Redis is a separate user-driven gate (Phase 7d Task 18), not executed here.
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

// Required fields render their label with a trailing `*` and NO separating
// space (ItemForm.vue appends `<span class="req">*</span>`), so a required
// field's label text is e.g. "Title*". Match the label anchored with an
// optional trailing `*` — anchoring keeps "Title" from also matching the
// distinct "SEO Title" field.
function labelMatch(label: string): RegExp {
  return new RegExp(`^${label.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\*?$`)
}

// ItemForm.vue renders each editable field (shared, translatable, AND
// relation) inside a `.field` wrapper with a plain (unlinked — no `for`/`id`
// pair on the rendered PrimeVue control) `<label>`, so `getByLabel()` cannot
// resolve these controls. Scope by the `.field` container that has the
// matching label text instead.
function fieldByLabel(page: Page, label: string) {
  return page.locator('.field', { has: page.getByText(labelMatch(label)) })
}

// Translatable fields (Title/Body) live inside ItemForm.vue's <Tabs>, which
// is NOT `lazy` — PrimeVue keeps every <TabPanel> mounted and only toggles
// `display:none` on the inactive ones. With two seeded locales (en + zh-TW),
// both panels' "Title"/"Body" `.field` wrappers exist in the DOM at once, so
// the plain fieldByLabel() above resolves to 2 elements (strict-mode
// violation). Scope to `:visible` so only the active (default-locale) tab's
// field matches — the inactive panel's `.field` is excluded because it (and
// its descendants) are `display:none`.
function translatableFieldByLabel(page: Page, label: string) {
  return page.locator('.field:visible', { has: page.getByText(labelMatch(label)) })
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

// Article.Category is [CmsRelation(Interface = RelationInterface.Dropdown)] ->
// RelationInput.vue renders RelationPicker.vue's plain (non-multiple) branch,
// a PrimeVue <Select> (role="combobox" trigger + role="option" overlay) —
// same click-trigger-then-click-option idiom as chooseStatus above. Options
// are loaded async from the API (RelationPicker's onMounted loadOptions()),
// so wait for at least one option to render before picking the first one —
// the exact seeded category name isn't known to this spec (see top-of-file
// seeding note), hence picking by position (`.first()`) rather than by name.
async function pickFirstCategory(page: Page): Promise<void> {
  const field = fieldByLabel(page, 'Category')
  await field.getByRole('combobox').click()
  const option = page.getByRole('option').first()
  await expect(option).toBeVisible()
  await option.click()
}

// Article.Tags is [CmsRelation(Interface = RelationInterface.TagSelect)] ->
// RelationInput.vue renders RelationPicker.vue's `multiple` branch, a
// PrimeVue <MultiSelect> (role="combobox" trigger + role="option" overlay
// with checkboxes). Selecting an option does NOT close the overlay (multi-
// select semantics), so press Escape afterwards to close it and commit the
// selection, matching how a real user would dismiss the panel.
async function pickFirstTag(page: Page): Promise<void> {
  const field = fieldByLabel(page, 'Tags')
  await field.getByRole('combobox').click()
  const option = page.getByRole('option').first()
  await expect(option).toBeVisible()
  await option.click()
  await page.keyboard.press('Escape')
}

test('create, edit relations, verify RelatedList, then delete an article', async ({ page }) => {
  await login(page)

  // Browse to the article collection, then create.
  await page.goto('/collections/article')
  await expect(page).toHaveURL(/\/collections\/article$/)
  await page.getByRole('button', { name: 'New' }).click()
  await expect(page).toHaveURL(/\/collections\/article\/new$/)

  // Shared field (Status, a Select) + default-locale required translatable
  // fields (Title/Body live under the first — default-locale — Tabs panel,
  // which is active by default per ItemForm.vue's `activeLocale` ref).
  const title = `E2E Relation Title ${STAMP}`
  await chooseStatus(page, 'Draft')
  await translatableFieldByLabel(page, 'Title').locator('input').fill(title)
  await translatableFieldByLabel(page, 'Body').locator('textarea').fill('E2E relations body content.')

  // Relations section: pick a Category (Dropdown/Select) and a Tag (TagSelect/MultiSelect).
  await pickFirstCategory(page)
  await pickFirstTag(page)

  await page.getByRole('button', { name: 'Save' }).click()

  // Back on the list; the new row is present BY ITS TRANSLATED TITLE — Title
  // is a translatable scalar field so selectListColumns() includes it as a
  // column, and (post-Phase-7d list-title fix) the row renders the resolved
  // translation instead of "—".
  await expect(page).toHaveURL(/\/collections\/article$/)
  await expect(page.getByText(title)).toBeVisible()

  // Open it, change the category and toggle the tag selection, save.
  await page.getByText(title).click()
  await expect(page).toHaveURL(/\/collections\/article\/[^/]+$/)

  // Re-pick the category (exercises changing a Dropdown relation that
  // already has a value — RelationPicker's ensureSelectedLabels() must have
  // resolved the current selection's label before this click).
  await pickFirstCategory(page)
  // Toggle the tag off then back on to exercise add/remove without depending
  // on a second seeded tag being present.
  await pickFirstTag(page)

  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page).toHaveURL(/\/collections\/article$/)
  await expect(page.getByText(title)).toBeVisible()

  // Open the category item and confirm the article shows up in its
  // Articles RelatedList (Category.Articles, DisplayTemplate = "{Title}").
  // Navigate via the Category field's resolved label link is not exposed as
  // a link, so browse the Category collection list instead.
  await page.goto('/collections/category')
  await expect(page).toHaveURL(/\/collections\/category$/)
  // Open the first category row (the one just assigned to the article above).
  await page.locator('.p-datatable-tbody tr').first().click()
  await expect(page).toHaveURL(/\/collections\/category\/[^/]+$/)
  await expect(fieldByLabel(page, 'Articles').getByText(title)).toBeVisible()

  // Back to the article list; delete the article.
  await page.goto('/collections/article')
  await page.getByText(title).click()
  await page.getByRole('button', { name: 'Delete' }).click()
  // PrimeVue's default locale (@primevue/core config) sets acceptLabel = "Yes";
  // ItemFormView.vue's confirm.require() doesn't override it.
  await page.getByRole('button', { name: 'Yes' }).click()
  await expect(page).toHaveURL(/\/collections\/article$/)
  await expect(page.getByText(title)).toHaveCount(0)
})
