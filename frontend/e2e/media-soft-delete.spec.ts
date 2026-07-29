import { test, expect } from './fixtures'
import { type Page } from '@playwright/test'

// File soft-delete live gate. Covers the critical path this feature adds to the media
// library: trashing a file from its DETAIL DIALOG (the active-view path — this is the one where a
// duplicate-ConfirmDialog bug was previously fixed by scoping MediaDetailDialog's ConfirmDialog to
// its own unnamed group, separate from MediaLibraryView's `media-folder`/`media-file` groups), then
// restore, re-trash, and permanent purge from the Trash view. Also asserts the server-side effect
// (GET /api/files/{id}/content 404 while trashed, 200 once restored) so this isn't just a UI-state
// check — it proves FileService's soft-delete query filter actually excludes the row.
//
// See media.spec.ts for the shared upload/login/PNG-fixture conventions this spec reuses verbatim.

const EMAIL = process.env.E2E_EMAIL ?? 'admin@admin.com'
const PASSWORD = process.env.E2E_PASSWORD ?? 'admin'
const STAMP = process.env.E2E_STAMP ?? 'e2e'
// See conflict.spec.ts: localhost (not 127.0.0.1) so page.request carries the app's auth cookie.
const API = process.env.E2E_API ?? 'http://localhost:5221'

// Smallest valid 1x1 transparent PNG — see media.spec.ts for why this is sufficient for
// ImageDimensionReader.TryRead's PNG() header check.
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

function detailDialog(page: Page) {
  return page.getByRole('dialog', { name: 'File details' })
}
// MediaGrid tiles: same convention as media.spec.ts's openDetailByName.
function fileTile(page: Page, fileName: string) {
  return page.locator('.media-tile', { has: page.locator('.media-tile__name', { hasText: fileName }) })
}
// The Trash view renders a plain `<table class="media-trash-list">` (MediaLibraryView.vue) with the
// filename as its own <td> text — scope a row by that text. `.locator(sel, { has })` (not
// `getByRole('row', { has })` — that option isn't part of getByRole's filter set and silently
// matches every row, including the header) mirrors fileTile()/folderCard()'s proven convention.
function trashRow(page: Page, fileName: string) {
  return page.locator('.media-trash-list tbody tr', { has: page.getByText(fileName, { exact: true }) })
}

// Uploads one in-memory PNG via the Upload dialog's hidden file input (see media.spec.ts for the
// full rationale), waits for the real POST /api/files 201, and registers the id for cleanup.
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
  uploadedFileId = id

  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog', { name: 'Upload files' })).toHaveCount(0)
  return id
}

// Isolates the uploaded row by its unique stamped filename (server-side debounced search) and opens
// its detail dialog, waiting for MediaDetailDialog's `deep=folder` GET so fields are populated.
async function openDetailByName(page: Page, fileName: string): Promise<void> {
  await page.getByPlaceholder('Search files…').fill(fileName)
  const tile = fileTile(page, fileName)
  await expect(tile).toBeVisible()
  const loaded = page.waitForResponse(
    (r) => /\/api\/items\/file\//.test(r.url()) && r.request().method() === 'GET',
  )
  await tile.click()
  await expect(detailDialog(page)).toBeVisible()
  await loaded
}

// Deletes (trashes) the currently-open file from its detail dialog, asserting that exactly ONE
// "move to trash" confirm dialog appears (the regression this spec guards) before accepting it.
async function trashFromDetailDialog(page: Page): Promise<void> {
  await detailDialog(page).getByRole('button', { name: 'Delete file', exact: true }).click()
  const trashConfirm = page.getByRole('alertdialog', { name: 'Move to trash' })
  await expect(trashConfirm).toHaveCount(1)
  await trashConfirm.getByRole('button', { name: 'Yes' }).click()
  await expect(detailDialog(page)).toHaveCount(0)
}

// Active <-> Trash toggle (SelectButton, gated on canDelete) — waits for the resulting file-list GET
// before returning so the caller's next assertion isn't racing the reload.
async function switchMode(page: Page, mode: 'Active' | 'Trash'): Promise<void> {
  const loaded = page.waitForResponse(
    (r) => r.request().method() === 'GET' && /\/api\/items\/file(\?|$)/.test(r.url()),
  )
  await page.getByText(mode, { exact: true }).click()
  await loaded
}

async function contentStatus(page: Page, id: string): Promise<number> {
  const res = await page.request.get(`${API}/api/files/${id}/content`)
  return res.status()
}

test.afterEach(async ({ page }) => {
  if (!uploadedFileId) return
  await page.request
    .delete(`${API}/api/files/${uploadedFileId}?purge=true`, { headers: { 'X-Struo-CSRF': '1' } })
    .catch(() => undefined)
  uploadedFileId = undefined
})

test('media file: trash from detail dialog, restore, re-trash, purge — with server-side content gating', async ({ page }) => {
  await login(page)
  await page.goto('/media')
  await expect(page).toHaveURL(/\/media$/)

  const fileName = `e2e-soft-delete-${STAMP}-${Date.now()}.png`
  uploadedFileId = await uploadOne(page, fileName)
  await expect(fileTile(page, fileName)).toBeVisible()
  expect(await contentStatus(page, uploadedFileId)).toBe(200)

  // Trash it from the detail dialog (active-view path) — the single-ConfirmDialog assertion lives
  // inside trashFromDetailDialog.
  await openDetailByName(page, fileName)
  await trashFromDetailDialog(page)
  await expect(fileTile(page, fileName)).toHaveCount(0)

  // Server-side: the file is excluded from reads (FileService.GetAsync respects the soft-delete
  // filter), so content now 404s even though the blob is untouched.
  expect(await contentStatus(page, uploadedFileId)).toBe(404)

  // Trash view lists it.
  await switchMode(page, 'Trash')
  await expect(trashRow(page, fileName)).toBeVisible()

  // Restore -> leaves Trash, reappears in Active, content is served again.
  await trashRow(page, fileName).getByRole('button', { name: 'Restore', exact: true }).click()
  await expect(trashRow(page, fileName)).toHaveCount(0)
  await switchMode(page, 'Active')
  await expect(fileTile(page, fileName)).toBeVisible()
  expect(await contentStatus(page, uploadedFileId)).toBe(200)

  // Trash it again (detail-dialog path again), then permanently purge it from Trash.
  await openDetailByName(page, fileName)
  await trashFromDetailDialog(page)
  await expect(fileTile(page, fileName)).toHaveCount(0)

  await switchMode(page, 'Trash')
  await expect(trashRow(page, fileName)).toBeVisible()
  await trashRow(page, fileName).getByRole('button', { name: 'Delete permanently', exact: true }).click()
  await expect(page.getByRole('alertdialog', { name: 'Delete permanently' })).toHaveCount(1)
  await page.getByRole('alertdialog', { name: 'Delete permanently' }).getByRole('button', { name: 'Yes' }).click()
  await expect(trashRow(page, fileName)).toHaveCount(0)

  // Purged: gone from Trash and content stays 404 (not merely "excluded from reads" but truly gone).
  expect(await contentStatus(page, uploadedFileId)).toBe(404)
  uploadedFileId = undefined
})
