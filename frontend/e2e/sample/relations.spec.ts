// Requires: seeded bootstrap admin (same as items.spec.ts / collections.spec.ts) with write+delete
// grants on `article`, plus AT LEAST ONE `category` row and ONE `tag` row already present in the
// database — the sample blog collection ships no seed data for these, so create one of each via the
// authenticated API before running this spec live, e.g.:
//
//   POST /api/items/category   { "name": "E2E Category" }
//   POST /api/items/tag        { "name": "E2E Tag" }
//
// See frontend/e2e/README.md for the full live-gate prerequisites (API on :5221, seeded admin, etc).
// CI runs neither `pnpm e2e:sample` nor this spec — end-to-end coverage needs a live API and
// database alongside the frontend dev server, so running it is a local, pre-merge discipline, not
// an automated gate. Run it by hand after any change to RelationPicker.vue, MultiSelectField.vue,
// or the combobox/select vendored atoms.
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
// component root IS the native control. Name (Text) qualifies and gets a real association; Status
// (Select) and the relation fields (Category/Tags/Articles, via RelationPicker) do not — those
// components' root is a wrapper, so the same `id` lands there and pairs with nothing.
// `getByLabel()` would therefore work for some of the fields this spec touches but not others.
// Scope by the `.field` container that has the matching label text instead — one locator strategy
// that works for every interface here.
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

// Open a relation picker and click its first option, returning that option's label. RelationPicker
// loads its options asynchronously (onMounted loadOptions() -> API), which makes two things race:
//   1. The overlay can pop open before any option exists — worst in edit mode, where the form is
//      interactable the instant it loads (create mode hides the race behind the time spent filling
//      Status/Title/Body). Verified live: the Category picker (single) / Tags picker (multiple)
//      render 19/13 options once the fetch resolves; the only failure mode is interacting before it does.
//   2. When the options DO arrive the list re-renders and the overlay repositions, so a click on a
//      pre-captured option hits a detached/moving element ("element is not stable" / "detached").
// So open (or re-open) AND read-label AND click as one retried unit, re-grabbing the option each
// attempt. Options are scoped to THIS picker's own overlay type — never page-wide getByRole('option')
// — because a fast preceding interaction (e.g. chooseStatus's Select) can leave another overlay
// momentarily open and a page-level query would resolve to its options (observed: Status "Draft").
async function pickFirstFromPicker(
  page: Page,
  field: ReturnType<typeof fieldByLabel>,
): Promise<string> {
  // RelationPicker.vue's single- and multi-select branches both render the same vendored
  // ComboboxTrigger button (data-slot="combobox-trigger") — its accessible name changes with the
  // current selection (a plain label when empty, "<label>: <value>" or a "<label>, N selected"
  // string once something is picked), so the stable data-slot hook is used instead of getByRole
  // name matching.
  //
  // The options list is NOT scoped by the shared data-slot="combobox-list" attribute alone: every
  // Combobox on this form (Regions' MultiSelectField, plus this picker's OWN sibling field) renders
  // that same attribute, and reka's Presence keeps a CLOSING list mounted through its exit
  // animation (ComboboxList.vue's `data-[state=closed]:fade-out-0`), so a page-wide query can still
  // resolve into a different, still-fading list from the field picked immediately before this one.
  // reka's trigger instead renders `aria-controls` pointing at its OWN content's id (and that id
  // never changes across opens/closes — verified in reka-ui's compiled ComboboxTrigger.js /
  // ComboboxContentImpl.js), so resolving the list by that id pins it to this exact field
  // regardless of what else is open or animating elsewhere on the page. Items keep role="option"
  // (reka's underlying ListboxItem sets it unconditionally), same as the plain Select's overlay.
  const trigger = field.locator('[data-slot="combobox-trigger"]')
  // Dismiss any stray overlay, THEN wait for every combobox-list on the page (not just this
  // field's) to actually finish unmounting — not merely start its closing animation — before
  // reopening. Without this barrier, a preceding picker's still-fading list satisfies a
  // visibility check for a few hundred ms after Escape, exactly the window the next picker's first
  // retry attempt runs in.
  await page.keyboard.press('Escape')
  await expect(page.locator('[data-slot="combobox-list"]')).toHaveCount(0)
  let label = ''
  await expect(async () => {
    // Reopen only when THIS trigger reports itself closed (its own data-state, set from the same
    // open/close boolean the click handler toggles) — not by probing the list's presence, which
    // the barrier above already guarantees is gone, and which (were it probed here) would only
    // ever describe THIS field's list now that the overlay below is id-scoped rather than page-wide.
    if ((await trigger.getAttribute('data-state')) !== 'open') await trigger.click()
    // Read aria-controls AFTER the open-check/click, every attempt, never before: reka's
    // ComboboxContent.js only generates the id the first time its content actually mounts
    // (`rootContext.contentId ||= useId(...)`), so on a field whose overlay has never opened yet
    // the trigger's aria-controls is still "" — reading it once outside this retried block would
    // permanently pin an empty selector for the rest of the function's life.
    const listId = await trigger.getAttribute('aria-controls')
    const overlay = page.locator(`#${listId}`)
    const option = overlay.locator('[role="option"]').first()
    await expect(option).toBeVisible({ timeout: 1000 })
    label = ((await option.textContent()) ?? '').trim()
    await option.click({ timeout: 2000 })
  }).toPass({ timeout: 20000 })
  return label
}

// Article.Category is [CmsRelation(Interface = RelationInterface.Dropdown)] -> RelationPicker's
// single-select Combobox branch. The exact seeded category name isn't known to this spec (see
// top-of-file seeding note), so pick by position and capture the chosen category's label
// (DisplayTemplate = "{Name}") — the RelatedList assertion opens THIS category rather than
// guessing a "first row" (dropdown option order and category list row order need not agree in a
// populated DB).
async function pickFirstCategory(page: Page): Promise<string> {
  return pickFirstFromPicker(page, fieldByLabel(page, 'Category'))
}

// Article.Tags is [CmsRelation(Interface = RelationInterface.TagSelect)] -> RelationPicker's
// `multiple` Combobox branch. Selecting an option does NOT close the overlay (multi-select
// semantics), so press Escape afterwards to close it and commit the selection, as a real user
// would. Returns the toggled tag's label so callers can assert the chip that RelationPicker.vue
// renders outside the trigger (unaffected by the overlay closing) — without that assertion,
// a helper that silently read a different field's list (the bug this id-scoping fixes) would
// still report a label and leave the suite green while never touching the Tags picker at all.
async function pickFirstTag(page: Page): Promise<string> {
  const label = await pickFirstFromPicker(page, fieldByLabel(page, 'Tags'))
  await page.keyboard.press('Escape')
  return label
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

  // Relations section: pick a Category (single-select Combobox) and a Tag (multi-select Combobox).
  await pickFirstCategory(page)
  const tagName = await pickFirstTag(page)
  // RelationPicker.vue's multi-select branch renders each selection as a Badge chip OUTSIDE the
  // trigger/overlay, so this is visible regardless of the overlay's open/closed state and proves
  // the click actually landed on Tags' own list (see pickFirstTag's doc comment).
  await expect(fieldByLabel(page, 'Tags').getByText(tagName, { exact: true })).toBeVisible()

  await page.getByRole('button', { name: 'Save' }).click()

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

  // Re-pick the category (exercises changing a Dropdown relation that already has a value —
  // RelationPicker's ensureSelectedLabels() must have resolved the current selection's label before
  // this click). Capture the assigned category's name to drive the RelatedList assertion below.
  const categoryName = await pickFirstCategory(page)
  // Only one tag is seeded, so re-picking it here toggles it OFF (add/remove use the same
  // click path in RelationPicker.vue's toggleValue()) — assert the chip is gone to prove this
  // removal, not just the earlier addition, actually reached the Tags picker.
  await pickFirstTag(page)
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
