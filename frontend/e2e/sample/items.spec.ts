import { test, expect } from '../fixtures'
import { type Page } from '@playwright/test'

const EMAIL = process.env.E2E_EMAIL ?? 'admin@admin.com'
const PASSWORD = process.env.E2E_PASSWORD ?? 'admin'
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

// ItemForm.vue's FieldLabel does carry an explicit `for`/`id` pair — but per FieldInput.vue's own
// comment on `:id` forwarding, that pairing only really associates when the field's rendered
// component root IS the native control. Title (Text) qualifies and gets a real association;
// Status (Select) does not — SelectField's root is a wrapper, so the same `id` lands there and
// pairs with nothing. `getByLabel()` would therefore work for some of the fields this spec touches
// but not others. Scope by the `.field` container that has the matching label text instead — one
// locator strategy that works for every interface here.
function fieldByLabel(page: Page, label: string) {
  return page.locator('.field', { has: page.getByText(labelMatch(label)) })
}

// Translatable fields (Title/Body) live inside ItemForm.vue's <Tabs>. Each locale's Field markup
// is individually gated by `v-if="loc.code === activeLocale"` inside its TabsContent, so only the
// active locale's Title/Body fields are ever mounted -- the inactive locale's panel renders
// nothing (confirmed by ItemForm.test.ts, which asserts exactly one translatable `.field-input` --
// the active locale's -- both before and after switching tabs). Plain fieldByLabel() above would
// therefore already resolve to a single match; scoping to `:visible` here is a defensive safeguard
// against that inner v-if changing, not what disambiguates today's DOM.
function translatableFieldByLabel(page: Page, label: string) {
  return page.locator('.field:visible', { has: page.getByText(labelMatch(label)) })
}

// Article.Status is [CmsField(Interface = FieldInterface.Select)] with
// CmsOptions("draft:Draft", "published:Published") -> FieldInput renders SelectField.vue's
// ui/select (a role="combobox" trigger + role="listbox"/"option" overlay), NOT a text input, so
// it cannot be `.fill()`ed. Click the trigger, then click the option by its visible label.
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

// The dev DB is populated and the article list is paginated, so a freshly-created row is not
// guaranteed to be on page 1. Isolate it by its searchable Title first — FilterBuilder re-hydrates
// empty on every remount, so re-search each time we return to the list. Mirrors the
// create-then-open idiom in conflict.spec.ts / unsaved-guard.spec.ts / trash.spec.ts.
async function openByTitle(page: Page, title: string): Promise<void> {
  await searchByField(page, 'Title', title)
  await expect(page.getByText(title, { exact: true })).toBeVisible()
  // Row-click navigation was removed from the collection list — open via the row's explicit
  // Edit action instead. Wait for the filtered search to settle to the single matching row first
  // (trash.spec.ts idiom) — otherwise the row locator can transiently match the still-unfiltered page.
  await expect(page.locator('tbody tr')).toHaveCount(1)
  await page.getByRole('row', { has: page.getByText(title, { exact: true }) }).getByRole('button', { name: 'Edit' }).click()
  await expect(page).toHaveURL(/\/collections\/article\/[^/]+$/)
  // Wait until init()'s async GET has populated the form before any field interaction — the URL
  // asserting only proves the route changed, not that the item finished loading. Title showing its
  // value means the load resolved (mirrors relations.spec.ts openArticleByTitle).
  await expect(translatableFieldByLabel(page, 'Title').locator('input')).toHaveValue(title)
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
  await translatableFieldByLabel(page, 'Title').locator('input').fill(title)
  // Body is [CmsField(Interface = FieldInterface.RichText)] -> RichTextInput renders a TipTap
  // editor whose editable surface is a `.ProseMirror` contenteditable, NOT a <textarea>.
  // contenteditable can't be `.fill()`ed — click to focus, then type via the keyboard
  // (same idiom as conflict.spec.ts / unsaved-guard.spec.ts).
  const body = translatableFieldByLabel(page, 'Body').locator('.ProseMirror')
  await body.click()
  await page.keyboard.type('E2E body content.')
  await page.getByRole('button', { name: 'Save' }).click()

  // Back on the list; the new row is present (Title is a scalar, non-system field so
  // selectListColumns() includes it as a column). Open it, edit the shared field, save.
  await expect(page).toHaveURL(/\/collections\/article$/)
  await openByTitle(page, title)
  await chooseStatus(page, 'Published')
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page).toHaveURL(/\/collections\/article$/)

  // Delete it.
  await openByTitle(page, title)
  await page.getByRole('button', { name: 'Delete' }).click()
  // ItemFormView.vue's onDelete() resolves through the local confirmStore/ConfirmHost, which
  // defaults its accept button to common.confirm ("Confirm"); deleteConfirm() doesn't override it.
  await page.getByRole('button', { name: 'Confirm' }).click()
  await expect(page).toHaveURL(/\/collections\/article$/)
  await searchByField(page, 'Title', title)
  // Count-settle (trash.spec.ts idiom): the filtered search transitions the tbody from the
  // unfiltered page to the filtered result. The deleted, uniquely-stamped title matches nothing,
  // so wait for the table to settle to its single "No records." empty row before asserting the title
  // is gone — otherwise the assertion could pass against the still-transitioning unfiltered list.
  await expect(page.locator('tbody tr')).toHaveCount(1)
  await expect(page.getByText(title, { exact: true })).toHaveCount(0)
})
