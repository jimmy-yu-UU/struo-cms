// Requires: seeded bootstrap admin (same as items.spec.ts / collections.spec.ts) with write+delete
// grants on `article`, `category` and `tag`. Every category and tag a test needs is created through
// the authenticated API with a stamped name and purged in afterEach, so the database needs no seed
// rows.
//
// See frontend/e2e/README.md for the full live-gate prerequisites (API on :5221, seeded admin, etc).
// CI runs neither `pnpm e2e:sample` nor this spec: the `sample` project needs the Blog sample opted
// in, which the shipped template is not. Run it by hand after any change to RelationPicker.vue,
// MultiSelectField.vue, JunctionLinksEditor.vue, SortableList.vue, or the combobox/select vendored
// atoms.
import { test, expect } from '../fixtures'
import { type Page } from '@playwright/test'
import { e2ePassword } from '../credentials'

const EMAIL = process.env.E2E_EMAIL ?? 'admin@admin.com'
const PASSWORD = e2ePassword()
// Caller passes a unique value per run so create-then-delete is self-cleaning
// even if two runs overlap (e.g. CI + a local run against the same DB).
const STAMP = process.env.E2E_STAMP ?? 'e2e'

// See conflict.spec.ts: localhost (not 127.0.0.1) so page.request carries the app's auth cookie.
const API = process.env.E2E_API ?? 'http://localhost:5221'
// Rows this spec creates; purged in afterEach, newest first so a row that references another goes first.
let navCreated: { collection: string; id: string }[] = []

// Creates a `category` or `tag` row through the API and returns its name, which is also its option
// label in the relation pickers (DisplayTemplate = "{Name}"). Needs a logged-in page.
async function createNamed(page: Page, collection: 'category' | 'tag', name: string): Promise<string> {
  const res = await page.request.post(`${API}/api/items/${collection}`, { data: { name }, headers: { 'X-Struo-CSRF': '1' } })
  expect(res.ok()).toBe(true)
  navCreated.push({ collection, id: String((await res.json()).data.id) })
  return name
}

async function login(page: Page): Promise<void> {
  await page.goto('/')
  await expect(page).toHaveURL(/\/login$/)
  await page.fill('input[type="email"]', EMAIL)
  await page.fill('input[type="password"]', PASSWORD)
  await page.click('button[type="submit"]')
  await expect(page).toHaveURL(/\/$/)
}

// Required fields render their label with a trailing `*` and NO separating
// space (ItemForm.vue appends `<span class="text-destructive ml-0.5">*</span>` — `ml-0.5` is a
// margin, not a whitespace character, so the label's accessible text is still "Title*"). Match the
// label anchored with an optional trailing `*` — anchoring keeps "Title" from also matching the
// distinct "SEO Title" field.
function labelMatch(label: string): RegExp {
  return new RegExp(`^${label.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\*?$`)
}

// fieldByLabel()/translatableFieldByLabel() return the `.field` wrapper, not a control, because
// every call site scopes a further sub-query off it — `.locator('input')`, `.locator('.ProseMirror')`,
// `.getByRole('combobox')`, `.locator('[data-slot="combobox-trigger"]')` where the accessible name
// is the selected value rather than a fixed one (see the comment at that call site),
// `.getByText(...)` for chip/row lookups, or `.getByRole('button')` — and
// those differ per interface. `getByLabel()` resolves straight to the labelled control or trigger
// itself, which is not a uniform container shape across interfaces (nor, per FieldInput.vue's own
// comment on `:id` forwarding, consistently label-associated in the first place), so it can't stand
// in here. Scope by the `.field` container that has the matching label text instead — one locator
// strategy that works for every interface here.
function fieldByLabel(page: Page, label: string) {
  return page.locator('.field', { has: page.getByText(labelMatch(label)) })
}

// Translatable fields (Title/Body) live inside ItemForm.vue's <Tabs>. Two independent gates keep
// an inactive locale's fields out of the DOM: reka's TabsContent unmounts its own slot content
// when not the active panel (TabsRoot's `unmountOnHide` defaults to true), and ItemForm.vue's own
// `v-if="loc.code === activeLocale"` wraps the Field markup inside that slot as a second,
// belt-and-braces gate — not redundant with it: reka's gate settles one microtask later than the
// `v-if` does (ItemForm.test.ts documents needing a flush for it), while the `v-if` flips in the
// same render tick, so it closes that transient window. The TabsContent element itself is still
// force-mounted for every locale (with `hidden` set), but it renders no content for the inactive
// one — confirmed by ItemForm.test.ts, which asserts exactly one translatable `.field-input` (the
// active locale's) both before and after switching tabs. Plain fieldByLabel() above would therefore
// already resolve to a single match; scoping to `:visible` here only becomes load-bearing if both
// gates were removed at once.
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
  const title = `E2E Relation Title ${STAMP}-${Date.now()}`
  // Category and tag rows for this run; the pickers need no pre-existing rows.
  const firstCategory = await createNamed(page, 'category', `E2E Category A ${STAMP}-${Date.now()}`)
  const secondCategory = await createNamed(page, 'category', `E2E Category B ${STAMP}-${Date.now()}`)
  const tagName = await createNamed(page, 'tag', `E2E Tag ${STAMP}-${Date.now()}`)
  // The pickers load their options when the form mounts, so open the form after the rows exist.
  await page.reload()
  await chooseStatus(page, 'Draft')
  await translatableFieldByLabel(page, 'Title').locator('input').fill(title)
  // Body is [CmsField(Interface = FieldInterface.RichText)] -> RichTextInput renders a TipTap
  // editor whose editable surface is a `.ProseMirror` contenteditable, NOT a <textarea>.
  // contenteditable can't be `.fill()`ed — click to focus, then type via the keyboard
  // (same idiom as conflict.spec.ts / unsaved-guard.spec.ts).
  const body = translatableFieldByLabel(page, 'Body').locator('.ProseMirror')
  await body.click()
  await page.keyboard.type('E2E relations body content.')

  // Relations section: pick a Category (single-select Combobox) and a Tag (multi-select Combobox).
  await pickOptionByLabel(page, fieldByLabel(page, 'Category'), firstCategory)
  await pickOptionByLabel(page, fieldByLabel(page, 'Tags'), tagName)
  // RelationPicker.vue's multi-select branch renders each selection as a Badge chip inside the field
  // box, beside the trigger, so this is visible regardless of the overlay's open/closed state and proves
  // the click actually landed on Tags' own list.
  await expect(fieldByLabel(page, 'Tags').getByText(tagName, { exact: true })).toBeVisible()

  const created = page.waitForResponse((r) =>
    r.request().method() === 'POST' && new URL(r.url()).pathname === '/api/items/article')
  await page.getByRole('button', { name: 'Save' }).click()
  navCreated.push({ collection: 'article', id: String((await (await created).json()).data.id) })

  // Back on the list; the new row is present BY ITS TRANSLATED TITLE — Title is a translatable
  // scalar field so selectListColumns() includes it as a column, and the row renders the resolved
  // translation instead of "—". Open it, change the category and
  // toggle the tag selection, save.
  await expect(page).toHaveURL(/\/collections\/article$/)
  await openArticleByTitle(page, title)
  // Reloaded fresh from the server (openArticleByTitle waits on the GET response), so the chip
  // still being visible here — not just after the pre-Save assertion above — proves the tag
  // actually landed on the saved record, not only in the form's local reactive state.
  await expect(fieldByLabel(page, 'Tags').getByText(tagName, { exact: true })).toBeVisible()

  // Switch to the second category (exercises changing a Dropdown relation that already has a value —
  // RelationPicker's ensureSelectedLabels() must have resolved the current selection's label before
  // this click). It is the category the RelatedList assertion below opens.
  const categoryName = secondCategory
  await pickOptionByLabel(page, fieldByLabel(page, 'Category'), categoryName)
  // Picking the linked tag again toggles it OFF (add/remove use the same click path in
  // RelationPicker.vue's toggleValue()) — assert the chip is gone to prove this removal, not just the
  // earlier addition, actually reached the Tags picker.
  await pickOptionByLabel(page, fieldByLabel(page, 'Tags'), tagName)
  await expect(fieldByLabel(page, 'Tags').getByText(tagName, { exact: true })).toHaveCount(0)

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

test.afterEach(async ({ page }) => {
  for (const c of [...navCreated].reverse()) {
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
  const categoryName = await createNamed(page, 'category', `E2E Nav Category ${STAMP}-${Date.now()}`)
  await page.goto('/collections/article/new')
  await chooseStatus(page, 'Draft')
  await translatableFieldByLabel(page, 'Title').locator('input').fill(title)
  const body = translatableFieldByLabel(page, 'Body').locator('.ProseMirror')
  await body.click()
  await page.keyboard.type('E2E nav body content.')
  await pickOptionByLabel(page, fieldByLabel(page, 'Category'), categoryName)
  const created = page.waitForResponse((r) =>
    r.request().method() === 'POST' && new URL(r.url()).pathname === '/api/items/article')
  await page.getByRole('button', { name: 'Save' }).click()
  // Recorded right after the save so a later failure still purges the article, and with it the category.
  navCreated.push({ collection: 'article', id: String((await (await created).json()).data.id) })
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
  // populated) — proving the form does not reuse stale category data. This is ItemFormView's
  // guardLeave() confirm via ConfirmHost, whose accept button defaults to common.confirm ("Confirm").
  await page.getByRole('button', { name: 'Confirm' }).click()
  await expect(page).toHaveURL(/\/collections\/article\/[^/]+$/)
  await expect(translatableFieldByLabel(page, 'Title').locator('input')).toHaveValue(title)
})

// Opens a relation picker (single or multiple Combobox) and clicks the option with THIS label.
// RelationPicker loads its options asynchronously and the list re-renders when they arrive, so open,
// find and click run as one retried unit. The options list is resolved through the trigger's own
// `aria-controls` id, never a page-wide `combobox-list` query: every Combobox on the form renders
// that data-slot and reka keeps a closing list mounted through its exit animation, so a page-wide
// query can resolve into another field's still-fading list. aria-controls is read after the
// open-check on every attempt because reka only generates the id once its content first mounts.
async function pickOptionByLabel(page: Page, field: ReturnType<typeof fieldByLabel>, label: string): Promise<void> {
  const trigger = field.locator('[data-slot="combobox-trigger"]')
  await page.keyboard.press('Escape')
  await expect(page.locator('[data-slot="combobox-list"]')).toHaveCount(0)
  await expect(async () => {
    if ((await trigger.getAttribute('data-state')) !== 'open') await trigger.click()
    const listId = await trigger.getAttribute('aria-controls')
    const option = page.locator(`#${listId}`).getByRole('option', { name: label, exact: true })
    await expect(option).toBeVisible({ timeout: 1000 })
    await option.click({ timeout: 2000 })
  }).toPass({ timeout: 20000 })
  await page.keyboard.press('Escape')
}

// Article.Tags' junction (ArticleTag, chapter 8, `docs/guide/en/08-relations.md`) is a
// [CmsCollection] with a Note payload field and the relation's SortField, so ItemFormView renders
// JunctionLinksEditor for it: one row per linked tag (label badge + inline Note input + move
// arrows) instead of RelationPicker's chips. Live gate for the payload round-trip: Note typed in
// a row and a reorder both survive Save and a fresh reload.
test('junction links editor: tag Note and order survive save + reload', async ({ page }) => {
  await login(page)
  const tagA = await createNamed(page, 'tag', `E2E Link A ${STAMP}-${Date.now()}`)
  const tagB = await createNamed(page, 'tag', `E2E Link B ${STAMP}-${Date.now()}`)

  const title = `E2E Junction Title ${STAMP}`
  await page.goto('/collections/article/new')
  await chooseStatus(page, 'Draft')
  await translatableFieldByLabel(page, 'Title').locator('input').fill(title)
  const body = translatableFieldByLabel(page, 'Body').locator('.ProseMirror')
  await body.click()
  await page.keyboard.type('E2E junction body content.')

  const tags = fieldByLabel(page, 'Tags')
  await pickOptionByLabel(page, tags, tagA)
  await pickOptionByLabel(page, tags, tagB)
  const rowLabels = tags.locator('.junction-link__label')
  await expect(rowLabels).toHaveText([tagA, tagB])
  const rowA = tags.locator('.junction-link', { has: page.getByText(tagA, { exact: true }) })
  await rowA.getByLabel('Note').fill(`note ${STAMP}`)
  // Move A below B: `.junction-link` is the <li> SortableList renders for each row, and the move
  // arrows sit inside it.
  await rowA.getByTestId('move-down').click()
  await expect(rowLabels).toHaveText([tagB, tagA])

  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page).toHaveURL(/\/collections\/article$/)
  await openArticleByTitle(page, title)
  navCreated.push({ collection: 'article', id: page.url().split('/').pop()! })

  const reloaded = fieldByLabel(page, 'Tags')
  await expect(reloaded.locator('.junction-link__label')).toHaveText([tagB, tagA])
  await expect(reloaded.locator('.junction-link', { has: page.getByText(tagA, { exact: true }) }).getByLabel('Note')).toHaveValue(`note ${STAMP}`)
  await expect(reloaded.locator('.junction-link', { has: page.getByText(tagB, { exact: true }) }).getByLabel('Note')).toHaveValue('')
})
