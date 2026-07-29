import { test, expect } from './fixtures'
import { type Page } from '@playwright/test'

// Audit 2026-07-21 Batch 4 (TEST-6) live gate: Media Library upload + per-locale Title/Alt save
// (FE-R6). Upload goes through a real <input type="file"> via Playwright's setInputFiles() with an
// in-memory buffer (no fixture file on disk needed, and no drag-and-drop simulation — a hidden file
// input accepts setInputFiles() regardless of its `display:none` dropzone styling, so this is not the
// kind of flaky UI gesture the task brief warned about). ImageDimensionReader
// (Struo.Infrastructure.Files) reads width/height from magic bytes only and swallows any parse
// failure (returns null), so a minimal-but-signature-valid PNG is accepted unconditionally by
// FilesController.Upload — there is no server-side image validation that could reject it.

const EMAIL = process.env.E2E_EMAIL ?? 'admin@admin.com'
const PASSWORD = process.env.E2E_PASSWORD ?? 'admin'
const STAMP = process.env.E2E_STAMP ?? 'e2e'
// See conflict.spec.ts: localhost (not 127.0.0.1) so page.request carries the app's auth cookie.
const API = process.env.E2E_API ?? 'http://localhost:5221'

// Smallest valid 1x1 transparent PNG (signature + IHDR + minimal IDAT/IEND) — enough for
// ImageDimensionReader.TryRead's PNG() header check (8-byte signature + IHDR at offset 16/20).
const PNG_1X1 = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAAAAAA6fptVAAAACklEQVR4nGNgAAIAAAUAAen63NgAAAAASUVORK5CYII=',
  'base64',
)

let uploadedFileId: string | undefined

async function login(page: Page): Promise<void> {
  await page.goto('/')
  await expect(page).toHaveURL(/\/login$/)
  await page.fill('input[type="email"]', EMAIL)
  await page.fill('input[type="password"]', PASSWORD)
  await page.click('button[type="submit"]')
  await expect(page).toHaveURL(/\/$/)
}

// MediaDetailDialog.vue's Title/Alt/File-URL fields use a plain (unlinked) `<label class="md-field">`
// — same convention as ItemForm.vue's `.field` — scope by the wrapper with the matching label text.
function mdField(page: Page, label: string) {
  return page.locator('.md-field', { has: page.getByText(label, { exact: true }) })
}
function detailDialog(page: Page) {
  return page.getByRole('dialog', { name: 'File details' })
}

// Uploads one in-memory PNG via the Upload dialog's hidden file input, waits for the real
// POST /api/files to resolve (deterministic — no reliance on the dropzone's transient "uploading"
// row disappearing), then closes the dialog. Returns the uploaded file's server id, read back off
// the id embedded in that same response (so no separate network call is needed later).
async function uploadOne(page: Page, fileName: string): Promise<string> {
  await page.getByRole('button', { name: 'Upload' }).click()
  await expect(page.getByRole('dialog', { name: 'Upload files' })).toBeVisible()

  const uploadResponse = page.waitForResponse(
    (res) => res.request().method() === 'POST' && res.url().includes('/api/files') && res.status() === 201,
  )
  await page.locator('input[type="file"]').setInputFiles({ name: fileName, mimeType: 'image/png', buffer: PNG_1X1 })
  const res = await uploadResponse
  const body = await res.json()
  const id = body.data.id as string
  // Register for cleanup NOW — before the dialog-close steps below — so a failure between here and
  // the caller's assignment can't orphan the uploaded file in the shared dev store. (Review nit.)
  uploadedFileId = id

  // Close the dialog (dismissableMask + closeOnEscape are both PrimeVue Dialog defaults here).
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog', { name: 'Upload files' })).toHaveCount(0)
  return id
}

// Isolates the uploaded row by its unique stamped filename (server-side debounced search, same
// idiom as items.spec.ts / conflict.spec.ts) and opens its detail dialog.
async function openDetailByName(page: Page, fileName: string): Promise<void> {
  await page.getByPlaceholder('Search files…').fill(fileName)
  // Each grid tile is a <button class="media-tile">; the filename lives only in its
  // .media-tile__name caption now (FileThumbnail's non-image chip shows icon + content type,
  // not the filename, to avoid duplicating it). Scope to that span rather than an unscoped
  // getByText(fileName) and click the enclosing tile button (MediaGrid emits `open` -> detail dialog).
  const tile = page.locator('.media-tile', { has: page.locator('.media-tile__name', { hasText: fileName }) })
  await expect(tile).toBeVisible()
  // The dialog becomes visible (props.file !== null) BEFORE MediaDetailDialog.load() finishes its
  // async GET — and load() sets activeLocale (initially '') and resets model. Wait for that GET so
  // fields go into the right translation bucket (a fill before load() lands in translations[''] and
  // is then clobbered) and the reopened values are populated before we assert.
  const loaded = page.waitForResponse(
    (r) => /\/api\/items\/file\//.test(r.url()) && r.request().method() === 'GET',
  )
  await tile.click()
  await expect(detailDialog(page)).toBeVisible()
  await loaded
}

test.afterEach(async ({ page }) => {
  if (!uploadedFileId) return
  await page.request
    .delete(`${API}/api/files/${uploadedFileId}`, { headers: { 'X-Struo-CSRF': '1' } })
    .catch(() => undefined)
  uploadedFileId = undefined
})

test('upload a file, then save its per-locale Title/Alt and see them persist', async ({ page }) => {
  await login(page)
  await page.goto('/media')
  await expect(page).toHaveURL(/\/media$/)

  // Date.now() keeps the filename unique per run: with the default STAMP='e2e', a leftover
  // e2e-media-e2e.png from a prior failed run would make getByText(exact) match two tiles
  // (strict-mode violation). (Review nit.)
  const fileName = `e2e-media-${STAMP}-${Date.now()}.png`
  uploadedFileId = await uploadOne(page, fileName)

  await openDetailByName(page, fileName)
  const title = `E2E Media Title ${STAMP}`
  const alt = `E2E Media Alt ${STAMP}`
  await mdField(page, 'Title').locator('input').fill(title)
  await mdField(page, 'Alt text').locator('input').fill(alt)
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(detailDialog(page)).toHaveCount(0)

  // Reopen (fresh GET, not the in-memory form state) to prove the save actually persisted.
  await openDetailByName(page, fileName)
  await expect(mdField(page, 'Title').locator('input')).toHaveValue(title)
  await expect(mdField(page, 'Alt text').locator('input')).toHaveValue(alt)
})
