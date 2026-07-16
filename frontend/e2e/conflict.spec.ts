import { test, expect, type Page } from '@playwright/test'

// FE-4 live gate: optimistic-concurrency (version) recovery.
//
// The form carries the item's `version` (parseItemToForm -> setModel -> buildItemPayload echo).
// When the server copy is bumped out-of-band after the form loaded, saving the now-stale version
// returns 409 CONFLICT; ItemFormView must surface a conflict banner and offer two recovery paths:
//   (a) "Reload latest" -> discard local edits, show the remote copy, banner cleared;
//   (b) re-save        -> the 409 refreshed the version token (ItemFormView sets model.version to
//                         the server's latest), so saving again overwrites the server copy.
// Version monotonicity is asserted via the authenticated API at the end.
//
// COLLECTION CHOICE: this spec drives `article` — the collection the audit finding FE-4 names.
// The article edit form carries a required translatable Title plus an optional DateTime
// "Published At" that is LEFT BLANK on purpose: this doubles as live proof of the batch-3b Task-1
// fix. Before that fix an empty DateTime serialised to "" and the API rejected the whole save with
// 400 "Request body could not be parsed." — the request never reached the version CAS. With the fix
// (empty date/time/dateTime serialises to null) the stale save now reaches the optimistic-lock check
// and returns 409 as designed. If Task 1 regresses, createAndOpen()'s Save would 400 and never land
// on the list, failing this spec before the conflict logic is even exercised.

const EMAIL = process.env.E2E_EMAIL ?? 'admin@struo.local'
const PASSWORD = process.env.E2E_PASSWORD ?? 'change-me-please'
const STAMP = process.env.E2E_STAMP ?? 'e2e'
// Backend origin for out-of-band writes. MUST share the auth cookie's host with the app: the SPA
// (baseURL localhost:5173) receives a host-only cookie for `localhost`, which page.request replays
// to localhost:5080 (cookies ignore port). Using 127.0.0.1 here would NOT carry the cookie.
const API = process.env.E2E_API ?? 'http://localhost:5080'

const CONFLICT_TEXT =
  'This item was changed by someone else. Review your edits and save again to overwrite, or reload the latest version.'

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
// Translatable fields (Title/Body) live inside ItemForm.vue's non-lazy <Tabs>: every locale's
// TabPanel stays mounted (only `display:none` toggled), so an unscoped `.field` match resolves to
// one wrapper per locale (strict-mode violation). Scope to `:visible` so only the active
// (default-locale) tab's field matches. See items.spec.ts for the full rationale.
function translatableFieldByLabel(page: Page, label: string) {
  return page.locator('.field:visible', { has: page.getByText(labelMatch(label)) })
}
function fieldByLabel(page: Page, label: string) {
  return page.locator('.field', { has: page.getByText(labelMatch(label)) })
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

// Create an article via the UI (known-good path), then open it so the edit form is loaded with a
// version token. Published At is deliberately left blank (see file header — Task-1 proof). Returns
// the item id (from the edit URL) and leaves the page on the form.
async function createAndOpen(page: Page, title: string): Promise<string> {
  await page.goto('/collections/article/new')
  await chooseStatus(page, 'Draft')
  await titleInput(page).fill(title)
  // Body is a RichText (TipTap) contenteditable — click and type (fill() does not work on it).
  const body = translatableFieldByLabel(page, 'Body').locator('.ProseMirror')
  await body.click()
  await page.keyboard.type('E2E conflict body content.')
  // Published At left blank on purpose (Task-1 proof): this Save must reach the list (200), not 400.
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page).toHaveURL(/\/collections\/article$/)

  // Populated dev DB -> isolate the new row by its searchable Title, then open it.
  await page.getByPlaceholder('Search').fill(title)
  await expect(page.getByText(title, { exact: true })).toBeVisible()
  await page.getByText(title, { exact: true }).click()
  await expect(page).toHaveURL(/\/collections\/article\/[0-9a-fA-F-]+$/)
  // Wait until init()'s async GET has populated the form: once Title shows the value, setModel has
  // run and model.version is set. Without this, an immediate Save can race ahead of the load and
  // omit the version token (no CAS -> no 409).
  await expect(titleInput(page)).toHaveValue(title)
  const id = page.url().match(/\/collections\/article\/([0-9a-fA-F-]+)$/)![1]
  createdId = id
  return id
}

async function apiGet(page: Page, id: string): Promise<{ version: number; title: string }> {
  const res = await page.request.get(`${API}/api/items/article/${id}`)
  expect(res.ok(), `GET article ${id} -> ${res.status()}`).toBeTruthy()
  const body = await res.json()
  return { version: body.data.version as number, title: body.data.translations.en.title as string }
}

// Bump the server copy out-of-band (simulates another editor saving). Renames the default-locale
// Title and echoes the current version so the write is accepted, incrementing it and leaving the
// open form stale. Returns the new server version and the remote title.
async function apiBump(page: Page, id: string): Promise<{ version: number; title: string }> {
  const cur = await apiGet(page, id)
  const remoteTitle = `${cur.title} REMOTE`
  const res = await page.request.put(`${API}/api/items/article/${id}`, {
    headers: { 'X-Struo-CSRF': '1', 'Content-Type': 'application/json' },
    data: { translations: { en: { title: remoteTitle } }, version: cur.version },
  })
  expect(res.ok(), `out-of-band PUT -> ${res.status()} ${await res.text()}`).toBeTruthy()
  const after = await apiGet(page, id)
  return { version: after.version, title: after.title }
}

test.afterEach(async ({ page }) => {
  if (!createdId) return
  await page.request
    .delete(`${API}/api/items/article/${createdId}?purge=true`, { headers: { 'X-Struo-CSRF': '1' } })
    .catch(() => undefined)
  createdId = undefined
})

test('save with a stale version shows the conflict banner, then Reload latest loads the remote copy', async ({ page }) => {
  await login(page)
  const title = `E2E Conflict Reload ${STAMP}`
  const id = await createAndOpen(page, title)

  // Someone else renames the article after our form loaded.
  const remote = await apiBump(page, id)

  // Make a local edit, then save our (now stale) form -> 409 -> conflict banner appears.
  await titleInput(page).fill(`${title} LOCAL`)
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByText(CONFLICT_TEXT, { exact: true })).toBeVisible()
  // Still on the edit form (no navigation on conflict), and our local edit was NOT clobbered.
  await expect(page).toHaveURL(/\/collections\/article\/[0-9a-fA-F-]+$/)
  await expect(titleInput(page)).toHaveValue(`${title} LOCAL`)

  // Reload latest -> remote copy applied (Title shows the remote value), banner cleared.
  await page.getByRole('button', { name: 'Reload latest' }).click()
  await expect(page.getByText(CONFLICT_TEXT, { exact: true })).toHaveCount(0)
  await expect(titleInput(page)).toHaveValue(remote.title)

  // API confirms no write happened during reload (version unchanged since the out-of-band bump).
  expect((await apiGet(page, id)).version).toBe(remote.version)
})

test('save with a stale version shows the conflict banner, then re-save overwrites (version increases monotonically)', async ({ page }) => {
  await login(page)
  const title = `E2E Conflict Resave ${STAMP}`
  const id = await createAndOpen(page, title)

  const created = (await apiGet(page, id)).version
  const remote = await apiBump(page, id)
  expect(remote.version).toBeGreaterThan(created)

  // First save -> stale -> 409 -> banner.
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByText(CONFLICT_TEXT, { exact: true })).toBeVisible()

  // Re-save: the 409 refreshed the token, so this overwrites the server copy and navigates away.
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page).toHaveURL(/\/collections\/article$/)

  // Version strictly increased again -> the re-save committed. Monotonic: created < bumped < final.
  const final = (await apiGet(page, id)).version
  expect(final).toBeGreaterThan(remote.version)
})
