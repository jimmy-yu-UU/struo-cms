// Requires: seeded bootstrap admin (same as items.spec.ts / collections.spec.ts) with write+delete
// grants on `article`, plus AT LEAST ONE `category` row and ONE `tag` row already present in the
// database — the sample blog collection ships no seed data for these, so create one of each via the
// authenticated API before running this spec live, e.g.:
//
//   POST /api/items/category   { "name": "E2E Category" }
//   POST /api/items/tag        { "name": "E2E Tag" }
//
// See frontend/e2e/README.md for the full live-gate prerequisites (API on :5221, seeded admin, etc).
// This spec is authored + collection-validated only (`playwright test --list`); the live run against
// real PG+Redis is a separate user-driven gate, not executed here.
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

// Open a relation picker and click its first option, returning that option's label. RelationPicker
// loads its options asynchronously (onMounted loadOptions() -> API), which makes two things race:
//   1. The overlay can pop open before any option exists — worst in edit mode, where the form is
//      interactable the instant it loads (create mode hides the race behind the time spent filling
//      Status/Title/Body). Verified live: the Category <Select> / Tags <MultiSelect> render 19/13
//      options once the fetch resolves; the only failure mode is interacting before it does.
//   2. When the options DO arrive the list re-renders and the overlay repositions, so a click on a
//      pre-captured <li> hits a detached/moving element ("element is not stable" / "detached").
// So open (or re-open) AND read-label AND click as one retried unit, re-grabbing the option each
// attempt. Options are scoped to THIS picker's own overlay type — never page-wide getByRole('option')
// — because a fast preceding interaction (e.g. chooseStatus's Select) can leave another overlay
// momentarily open and a page-level query would resolve to its options (observed: Status "Draft").
async function pickFirstFromPicker(
  page: Page,
  field: ReturnType<typeof fieldByLabel>,
  multiple: boolean,
): Promise<string> {
  // MultiSelect's role="combobox" is a HIDDEN input behind the visible label/dropdown container
  // (which intercepts pointer events); click the visible `.p-multiselect` root instead. The plain
  // Select's combobox trigger is directly clickable (same idiom as chooseStatus).
  const trigger = multiple ? field.locator('.p-multiselect') : field.getByRole('combobox')
  const overlay = multiple ? '.p-multiselect-overlay' : '.p-select-overlay'
  // Dismiss any stray overlay so only the target picker's overlay contributes options (PrimeVue
  // removes a closed overlay from the DOM, so a scoped query then only ever sees the open one).
  await page.keyboard.press('Escape')
  let label = ''
  await expect(async () => {
    // Reopen only when THIS picker's overlay is absent, not when its first option is merely
    // transiently invisible. Keying the reopen on the overlay (not an option) avoids clicking the
    // trigger while the overlay is already open — which would toggle it shut mid-load and churn the
    // retry. Same open -> read -> click sequence; only the reopen predicate is tightened.
    const overlayEl = page.locator(overlay)
    if (!(await overlayEl.isVisible().catch(() => false))) await trigger.click()
    const option = page.locator(`${overlay} [role="option"]`).first()
    await expect(option).toBeVisible({ timeout: 1000 })
    label = ((await option.textContent()) ?? '').trim()
    await option.click({ timeout: 2000 })
  }).toPass({ timeout: 20000 })
  return label
}

// Article.Category is [CmsRelation(Interface = RelationInterface.Dropdown)] -> RelationPicker's plain
// (non-multiple) <Select>. The exact seeded category name isn't known to this spec (see top-of-file
// seeding note), so pick by position and capture the chosen category's label (DisplayTemplate =
// "{Name}") — the RelatedList assertion opens THIS category rather than guessing a "first row"
// (dropdown option order and category list row order need not agree in a populated DB).
async function pickFirstCategory(page: Page): Promise<string> {
  return pickFirstFromPicker(page, fieldByLabel(page, 'Category'), false)
}

// Article.Tags is [CmsRelation(Interface = RelationInterface.TagSelect)] -> RelationPicker's
// `multiple` <MultiSelect>. Selecting an option does NOT close the overlay (multi-select semantics),
// so press Escape afterwards to close it and commit the selection, as a real user would.
async function pickFirstTag(page: Page): Promise<void> {
  await pickFirstFromPicker(page, fieldByLabel(page, 'Tags'), true)
  await page.keyboard.press('Escape')
}

// Search filters apply on an explicit press (or Enter), never on keystroke.
async function searchByField(page: Page, fieldLabel: string, value: string): Promise<void> {
  await page.getByRole('button', { name: 'Add condition' }).click()
  await page.getByRole('combobox', { name: 'Field' }).click()
  await page.getByRole('option', { name: fieldLabel, exact: true }).click()
  await page.getByRole('textbox', { name: 'Value' }).fill(value)
  await page.getByRole('button', { name: 'Search' }).click()
}

// Populated dev DB + pagination: isolate the article row by its searchable Title before opening
// (FilterBuilder re-hydrates empty on every remount). Mirrors conflict.spec.ts / trash.spec.ts.
async function openArticleByTitle(page: Page, title: string): Promise<void> {
  await searchByField(page, 'Title', title)
  await expect(page.getByText(title, { exact: true })).toBeVisible()
  // The collection list has no row-click navigation — open via the row's explicit
  // Edit action instead. Wait for the filtered search to settle to the single matching row first
  // (trash.spec.ts idiom) — otherwise the row locator can transiently match the still-unfiltered page.
  await expect(page.locator('tbody tr')).toHaveCount(1)
  await page.getByRole('row', { has: page.getByText(title, { exact: true }) }).getByRole('button', { name: 'Edit' }).click()
  await expect(page).toHaveURL(/\/collections\/article\/[^/]+$/)
  // Wait until init()'s async GET has populated the form before any field interaction. Without this,
  // clicking a relation combobox can fire before RelationPicker mounted/loaded its options, opening
  // an empty overlay whose options never resolve. Title showing its value means the load finished.
  await expect(translatableFieldByLabel(page, 'Title').locator('input')).toHaveValue(title)
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
  // Body is [CmsField(Interface = FieldInterface.RichText)] -> RichTextInput renders a TipTap
  // editor whose editable surface is a `.ProseMirror` contenteditable, NOT a <textarea>.
  // contenteditable can't be `.fill()`ed — click to focus, then type via the keyboard
  // (same idiom as conflict.spec.ts / unsaved-guard.spec.ts).
  const body = translatableFieldByLabel(page, 'Body').locator('.ProseMirror')
  await body.click()
  await page.keyboard.type('E2E relations body content.')

  // Relations section: pick a Category (Dropdown/Select) and a Tag (TagSelect/MultiSelect).
  await pickFirstCategory(page)
  await pickFirstTag(page)

  await page.getByRole('button', { name: 'Save' }).click()

  // Back on the list; the new row is present BY ITS TRANSLATED TITLE — Title is a translatable
  // scalar field so selectListColumns() includes it as a column, and the row renders the resolved
  // translation instead of "—". Open it, change the category and
  // toggle the tag selection, save.
  await expect(page).toHaveURL(/\/collections\/article$/)
  await openArticleByTitle(page, title)

  // Re-pick the category (exercises changing a Dropdown relation that already has a value —
  // RelationPicker's ensureSelectedLabels() must have resolved the current selection's label before
  // this click). Capture the assigned category's name to drive the RelatedList assertion below.
  const categoryName = await pickFirstCategory(page)
  // Toggle the tag off then back on to exercise add/remove without depending on a second seeded tag.
  await pickFirstTag(page)

  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page).toHaveURL(/\/collections\/article$/)

  // Open the ASSIGNED category and confirm the article shows up in its Articles RelatedList
  // (Category.Articles, DisplayTemplate = "{Title}"). The Category field's resolved label is not a
  // link, so browse the Category collection list and isolate the assigned category by its Name.
  await page.goto('/collections/category')
  await expect(page).toHaveURL(/\/collections\/category$/)
  await searchByField(page, 'Name', categoryName)
  await expect(page.getByText(categoryName, { exact: true })).toBeVisible()
  await expect(page.locator('tbody tr')).toHaveCount(1)
  await page.getByRole('row', { has: page.getByText(categoryName, { exact: true }) }).getByRole('button', { name: 'Edit' }).click()
  await expect(page).toHaveURL(/\/collections\/category\/[^/]+$/)
  await expect(fieldByLabel(page, 'Articles').getByText(title)).toBeVisible()

  // Back to the article list; delete the article.
  await page.goto('/collections/article')
  await openArticleByTitle(page, title)
  await page.getByRole('button', { name: 'Delete' }).click()
  // ItemFormView.vue's onDelete() resolves through the local confirmStore/ConfirmHost, which
  // defaults its accept button to common.confirm ("Confirm"); deleteConfirm() doesn't override it.
  await page.getByRole('button', { name: 'Confirm' }).click()
  await expect(page).toHaveURL(/\/collections\/article$/)
  await searchByField(page, 'Title', title)
  // Count-settle (trash.spec.ts idiom): wait for the filtered search to settle the tbody to its
  // single "No records." empty row before asserting the deleted title is gone — otherwise the
  // assertion could pass against the still-transitioning unfiltered list.
  await expect(page.locator('tbody tr')).toHaveCount(1)
  await expect(page.getByText(title, { exact: true })).toHaveCount(0)
})

// See conflict.spec.ts: localhost (not 127.0.0.1) so page.request carries the app's auth cookie.
const API = process.env.E2E_API ?? 'http://localhost:5221'
// Rows this test creates; purged in afterEach. The seeded category is NOT purged (it pre-exists).
let navCreated: { collection: string; id: string }[] = []
test.afterEach(async ({ page }) => {
  for (const c of navCreated) {
    await page.request
      .delete(`${API}/api/items/${c.collection}/${c.id}?purge=true`, { headers: { 'X-Struo-CSRF': '1' } })
      .catch(() => undefined)
  }
  navCreated = []
})

// Live gate: a RelatedList row click is a same-route-record, params-only navigation
// (`collections/category/<id>` -> `collections/article/<id>`, both the `collection-item` route).
// Vue Router treats this as an update of the existing component, not a leave, so
// onBeforeRouteLeave never fires — ItemFormView's own onBeforeRouteUpdate guard is what prompts
// for the dirty form here; without it, the edits would be silently discarded. Separately,
// AppShell's <router-view> is keyed on route.path, which is what makes accepting the prompt
// actually remount ItemFormView (init() re-runs, loading the target record) instead of Vue
// reusing the existing instance in place. Category.Articles is the sample's RelatedList (see the
// assertion above).
test('dirty form + RelatedList row click prompts unsaved guard, then remounts to the target record', async ({ page }) => {
  await login(page)

  // Create an article assigned to a category so that category's Articles RelatedList has a row.
  const title = `E2E Nav Article ${STAMP}`
  await page.goto('/collections/article/new')
  await chooseStatus(page, 'Draft')
  await translatableFieldByLabel(page, 'Title').locator('input').fill(title)
  const body = translatableFieldByLabel(page, 'Body').locator('.ProseMirror')
  await body.click()
  await page.keyboard.type('E2E nav body content.')
  const categoryName = await pickFirstCategory(page)
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page).toHaveURL(/\/collections\/article$/)

  // Open the assigned category; wait for its form + Articles RelatedList row to load.
  await page.goto('/collections/category')
  await searchByField(page, 'Name', categoryName)
  await expect(page.getByText(categoryName, { exact: true })).toBeVisible()
  await expect(page.locator('tbody tr')).toHaveCount(1)
  await page.getByRole('row', { has: page.getByText(categoryName, { exact: true }) }).getByRole('button', { name: 'Edit' }).click()
  await expect(page).toHaveURL(/\/collections\/category\/[^/]+$/)
  await expect(fieldByLabel(page, 'Articles').getByText(title)).toBeVisible()

  // Dirty the category form (edit Name) so the guard has unsaved changes to protect.
  const nameInput = fieldByLabel(page, 'Name').locator('input')
  const originalName = await nameInput.inputValue()
  await nameInput.fill(`${originalName} edited`)

  // Click the article row in the RelatedList -> same-record params-only nav -> dirty guard prompts.
  // The related-list table has no row-click event, so the label itself renders as the button that
  // carries navigation; target that button rather than the row's text.
  await fieldByLabel(page, 'Articles').getByRole('button', { name: title }).click()
  const guard = page.getByRole('alertdialog').filter({ hasText: 'Unsaved changes' })
  await expect(guard).toBeVisible()

  // Accept -> navigation proceeds, the keyed view remounts, and init() reloads the ARTICLE (Title
  // populated) — proving the form no longer reuses stale category data. This is ItemFormView's
  // guardLeave() confirm via ConfirmHost, whose accept button defaults to common.confirm ("Confirm").
  await page.getByRole('button', { name: 'Confirm' }).click()
  await expect(page).toHaveURL(/\/collections\/article\/[^/]+$/)
  await expect(translatableFieldByLabel(page, 'Title').locator('input')).toHaveValue(title)

  navCreated.push({ collection: 'article', id: page.url().match(/\/collections\/article\/([^/]+)$/)![1] })
})
