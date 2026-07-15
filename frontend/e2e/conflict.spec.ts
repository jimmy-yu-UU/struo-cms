import { test, expect, type Page } from '@playwright/test'

// FE-4 live gate: optimistic-concurrency (version) recovery.
//
// The form carries the item's `version` (parseItemToForm -> setModel -> buildItemPayload echo).
// When the server copy is bumped out-of-band after the form loaded, saving the now-stale version
// returns 409 CONFLICT; ItemFormView must surface a conflict banner and offer two recovery paths:
//   (a) "Reload latest" -> discard local edits, show the remote copy, banner cleared;
//   (b) re-save        -> the refreshed token lets the save overwrite the server copy.
// Version monotonicity is asserted via the authenticated API at the end.
//
// COLLECTION CHOICE: this spec drives `category` (a single required text field), NOT `article`.
// Article's UI update is currently blocked by a separate, pre-existing frontend bug: an empty
// optional DateTime field ("Published At") serialises to "" and the API rejects it with
// 400 "Request body could not be parsed." (see gate-e2e-report.md). Category has no DateTime field,
// so its update round-trips cleanly, letting us exercise the generic 409-recovery path end-to-end.

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
function fieldByLabel(page: Page, label: string) {
  return page.locator('.field', { has: page.getByText(labelMatch(label)) })
}
function nameInput(page: Page) {
  return fieldByLabel(page, 'Name').locator('input')
}

// Create a category via the UI (known-good path), then open it so the edit form is loaded with a
// version token. Returns the item id (from the edit URL) and leaves the page on the form.
async function createAndOpen(page: Page, name: string): Promise<string> {
  await page.goto('/collections/category/new')
  await nameInput(page).fill(name)
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page).toHaveURL(/\/collections\/category$/)

  // Populated dev DB -> isolate the new row by its searchable Name, then open it.
  await page.getByPlaceholder('Search').fill(name)
  await expect(page.getByText(name, { exact: true })).toBeVisible()
  await page.getByText(name, { exact: true }).click()
  await expect(page).toHaveURL(/\/collections\/category\/[0-9a-fA-F-]+$/)
  // Wait until init()'s async GET has populated the form: once Name shows the value, setModel has
  // run and model.version is set. Without this, an immediate Save can race ahead of the load and
  // omit the version token (no CAS -> no 409).
  await expect(nameInput(page)).toHaveValue(name)
  const id = page.url().match(/\/collections\/category\/([0-9a-fA-F-]+)$/)![1]
  createdId = id
  return id
}

async function apiGet(page: Page, id: string): Promise<{ version: number; name: string }> {
  const res = await page.request.get(`${API}/api/items/category/${id}`)
  expect(res.ok(), `GET category ${id} -> ${res.status()}`).toBeTruthy()
  const body = await res.json()
  return { version: body.data.version as number, name: body.data.name as string }
}

// Bump the server copy out-of-band (simulates another editor saving). Renames the item and echoes
// the current version so the write is accepted, incrementing it and leaving the open form stale.
// Returns the new server version and the remote name.
async function apiBump(page: Page, id: string): Promise<{ version: number; name: string }> {
  const cur = await apiGet(page, id)
  const remoteName = `${cur.name} REMOTE`
  const res = await page.request.put(`${API}/api/items/category/${id}`, {
    headers: { 'X-Struo-CSRF': '1', 'Content-Type': 'application/json' },
    data: { name: remoteName, version: cur.version },
  })
  expect(res.ok(), `out-of-band PUT -> ${res.status()} ${await res.text()}`).toBeTruthy()
  const after = await apiGet(page, id)
  return { version: after.version, name: after.name }
}

test.afterEach(async ({ page }) => {
  if (!createdId) return
  await page.request
    .delete(`${API}/api/items/category/${createdId}?purge=true`, { headers: { 'X-Struo-CSRF': '1' } })
    .catch(() => undefined)
  createdId = undefined
})

test('save with a stale version shows the conflict banner, then Reload latest loads the remote copy', async ({ page }) => {
  await login(page)
  const name = `E2E Conflict Reload ${STAMP}`
  const id = await createAndOpen(page, name)

  // Someone else renames the category after our form loaded.
  const remote = await apiBump(page, id)

  // Make a local edit, then save our (now stale) form -> 409 -> conflict banner appears.
  await nameInput(page).fill(`${name} LOCAL`)
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByText(CONFLICT_TEXT, { exact: true })).toBeVisible()
  // Still on the edit form (no navigation on conflict), and our local edit was NOT clobbered.
  await expect(page).toHaveURL(/\/collections\/category\/[0-9a-fA-F-]+$/)
  await expect(nameInput(page)).toHaveValue(`${name} LOCAL`)

  // Reload latest -> remote copy applied (Name shows the remote value), banner cleared.
  await page.getByRole('button', { name: 'Reload latest' }).click()
  await expect(page.getByText(CONFLICT_TEXT, { exact: true })).toHaveCount(0)
  await expect(nameInput(page)).toHaveValue(remote.name)

  // API confirms no write happened during reload (version unchanged since the out-of-band bump).
  expect((await apiGet(page, id)).version).toBe(remote.version)
})

test('save with a stale version shows the conflict banner, then re-save overwrites (version increases monotonically)', async ({ page }) => {
  await login(page)
  const name = `E2E Conflict Resave ${STAMP}`
  const id = await createAndOpen(page, name)

  const created = (await apiGet(page, id)).version
  const remote = await apiBump(page, id)
  expect(remote.version).toBeGreaterThan(created)

  // First save -> stale -> 409 -> banner.
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByText(CONFLICT_TEXT, { exact: true })).toBeVisible()

  // Re-save: recovery refreshed the token, so this overwrites the server copy and navigates away.
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page).toHaveURL(/\/collections\/category$/)

  // Version strictly increased again -> the re-save committed. Monotonic: created < bumped < final.
  const final = (await apiGet(page, id)).version
  expect(final).toBeGreaterThan(remote.version)
})
