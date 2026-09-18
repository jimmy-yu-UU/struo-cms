import { test, expect } from './fixtures'
import { type Page } from '@playwright/test'

// Media-library folders live gate. Covers folder browsing, filename -> Title autofill (already
// implemented server-side by Struo.Infrastructure.Files.FileService.UploadAsync), that a detail-
// dialog save which never touches folder assignment does not unfile the item (MediaDetailDialog
// dropped its own Folder field entirely in favour of the media library's drag-and-drop / right-
// click "Move to…" / batch-move controls -- this spec drives folder moves through the grid tile's
// context menu and the MediaMoveDialog it opens instead), the absence of an "open in full editor"
// escape hatch on the file detail dialog, and the non-empty-folder delete guard.
//
// See media.spec.ts for the shared upload/login/mdField conventions this spec reuses verbatim.

const EMAIL = process.env.E2E_EMAIL ?? 'admin@admin.com'
const PASSWORD = process.env.E2E_PASSWORD ?? 'admin'
const STAMP = process.env.E2E_STAMP ?? 'e2e'
const API = process.env.E2E_API ?? 'http://localhost:5221'

// Smallest valid 1x1 transparent PNG -- see media.spec.ts for why this is sufficient for
// ImageDimensionReader.TryRead's PNG() header check.
const PNG_1X1 = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAAAAAA6fptVAAAACklEQVR4nGNgAAIAAAUAAen63NgAAAAASUVORK5CYII=',
  'base64',
)

// MediaMoveDialog.vue: each option's accessible name is "<label> — <moveSubmit>".
const MOVE_SUBMIT = 'Move'

let uploadedFileId: string | undefined
let createdFolderId: string | undefined

async function login(page: Page): Promise<void> {
  await page.goto('/')
  await expect(page).toHaveURL(/\/login$/)
  await page.fill('input[type="email"]', EMAIL)
  await page.fill('input[type="password"]', PASSWORD)
  await page.click('button[type="submit"]')
  await expect(page).toHaveURL(/\/$/)
}

// Same `.md-field` wrapper convention as media.spec.ts.
function mdField(page: Page, label: string) {
  return page.locator('.md-field', { has: page.getByText(label, { exact: true }) })
}
function detailDialog(page: Page) {
  return page.getByRole('dialog', { name: 'File details' })
}
function moveDialog(page: Page) {
  return page.getByRole('dialog', { name: 'Move to…' })
}
// MediaFolderCards.vue: `.folder-card` div (role="button") wrapping a `.folder-card__name` span.
function folderCard(page: Page, name: string) {
  return page.locator('.folder-card', { has: page.locator('.folder-card__name', { hasText: name }) })
}
// MediaGrid tiles: same convention as media.spec.ts's openDetailByName.
function fileTile(page: Page, fileName: string) {
  return page.locator('.media-tile', { has: page.locator('.media-tile__name', { hasText: fileName }) })
}
function folderDeleteConfirmDialog(page: Page) {
  return page.getByRole('alertdialog', { name: 'Delete folder' })
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

// Creates a folder via the "New folder" dialog, waits for the real POST /api/items/mediafolder
// 201, and registers the id for cleanup.
async function createFolder(page: Page, name: string): Promise<string> {
  await page.getByRole('button', { name: 'New folder' }).click()
  const dialog = page.getByRole('dialog', { name: 'New folder' })
  await expect(dialog).toBeVisible()

  const created = page.waitForResponse(
    (res) => res.request().method() === 'POST' && res.url().includes('/api/items/mediafolder') && res.status() === 201,
  )
  await dialog.getByRole('textbox', { name: 'Folder name' }).fill(name)
  await dialog.getByRole('button', { name: 'OK' }).click()
  const res = await created
  const body = await res.json()
  const id = body.data.id as string
  createdFolderId = id

  await expect(folderCard(page, name)).toBeVisible()
  return id
}

// Clicks a folder card (not its Rename/Delete actions) and waits for the resulting file-list GET
// (mediaFolderFilter scopes it to that folder's contents) before asserting the breadcrumb.
async function enterFolder(page: Page, name: string): Promise<void> {
  const loaded = page.waitForResponse(
    (r) => r.request().method() === 'GET' && /\/api\/items\/file(\?|$)/.test(r.url()),
  )
  await folderCard(page, name).click()
  await loaded
  await expect(page.locator('.media-crumb__current')).toHaveText(name)
}

// Root breadcrumb link is always present once any folder/breadcrumb UI has rendered.
async function goToBreadcrumbRoot(page: Page): Promise<void> {
  const loaded = page.waitForResponse(
    (r) => r.request().method() === 'GET' && /\/api\/items\/file(\?|$)/.test(r.url()),
  )
  await page.locator('.media-crumb__link').first().click()
  await loaded
}

// Opens a file's detail dialog and waits for its item GET to resolve (MediaDetailDialog's load())
// so fields are populated before we assert on them.
async function openDetail(page: Page, fileName: string): Promise<void> {
  const tile = fileTile(page, fileName)
  await expect(tile).toBeVisible()
  const loaded = page.waitForResponse(
    (r) => r.request().method() === 'GET' && /\/api\/items\/file\//.test(r.url()),
  )
  await tile.click()
  await expect(detailDialog(page)).toBeVisible()
  await loaded
}

// Server-truth check for the folder-untouched-save regression guard: the UI-level "the tile is
// still visible in the current listing" assertion cannot actually prove the file wasn't unfiled,
// because the grid's post-save reload (MediaDetailDialog's `@saved="load"` on MediaLibraryView) is
// never awaited by saveDetail() -- it can pass on stale, pre-reload DOM either way. A fresh
// `deep=folder` GET straight to the API is server truth and cannot race the UI.
async function assertServerFolder(page: Page, fileId: string, expectedFolderId: string | null): Promise<void> {
  const res = await page.request.get(`${API}/api/items/file/${fileId}?deep=folder`)
  expect(res.ok(), `GET file ${fileId} -> ${res.status()}`).toBeTruthy()
  const body = await res.json()
  const folder = body.data.folder as { id?: string } | null | undefined
  expect(folder?.id ?? null).toBe(expectedFolderId)
}

// Saves the detail dialog, waiting for the real PUT /api/items/file/{id} to resolve and the dialog
// to close (MediaDetailDialog emits 'saved' + 'close' only after the request succeeds).
async function saveDetail(page: Page): Promise<void> {
  const saved = page.waitForResponse(
    (r) => r.request().method() === 'PUT' && /\/api\/items\/file\//.test(r.url()),
  )
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await saved
  await expect(detailDialog(page)).toHaveCount(0)
}

// Moves a file to `targetLabel` ("Root" for Uncategorized, or a folder's own name) through the
// grid tile's right-click context menu and the MediaMoveDialog it opens -- the mechanism the
// detail dialog's own Folder field was removed in favour of. Waits for the real
// PUT /api/items/file/{id} that performMove issues (itemsApi.update('file', id, { folderId })).
async function moveFileTo(page: Page, fileName: string, targetLabel: string): Promise<void> {
  await fileTile(page, fileName).click({ button: 'right' })
  await page.getByRole('menuitem', { name: 'Move to…' }).click()
  await expect(moveDialog(page)).toBeVisible()
  const moved = page.waitForResponse(
    (r) => r.request().method() === 'PUT' && /\/api\/items\/file\//.test(r.url()),
  )
  await moveDialog(page).getByRole('button', { name: `${targetLabel} — ${MOVE_SUBMIT}`, exact: true }).click()
  await moved
  await expect(moveDialog(page)).toHaveCount(0)
}

// Clicks a folder card's Delete action and confirms via the store-backed ConfirmHost's alertdialog.
// onRemoveFolder builds this request inline and passes no acceptLabel, so ConfirmHost's default
// applies: common.confirm ("Confirm"). Returns the DELETE request's status so callers can assert
// 204 (empty, succeeds) vs 409 (non-empty, rejected) deterministically instead of only polling the
// UI.
async function attemptDeleteFolder(page: Page, name: string): Promise<number> {
  const del = page.waitForResponse(
    (r) => r.request().method() === 'DELETE' && /\/api\/items\/mediafolder\//.test(r.url()),
  )
  await folderCard(page, name).getByRole('button', { name: 'Delete folder' }).click()
  await expect(folderDeleteConfirmDialog(page)).toBeVisible()
  await folderDeleteConfirmDialog(page).getByRole('button', { name: 'Confirm' }).click()
  const res = await del
  return res.status()
}

test.afterEach(async ({ page }) => {
  // File first, then folder -- a folder still holding the file would 409 on delete (Restrict).
  if (uploadedFileId) {
    await page.request
      .delete(`${API}/api/files/${uploadedFileId}`, { headers: { 'X-Struo-CSRF': '1' } })
      .catch(() => undefined)
    uploadedFileId = undefined
  }
  if (createdFolderId) {
    await page.request
      .delete(`${API}/api/items/mediafolder/${createdFolderId}`, { headers: { 'X-Struo-CSRF': '1' } })
      .catch(() => undefined)
    createdFolderId = undefined
  }
})

test('media folders: create, upload with title autofill, a folder-untouched save does not unfile, move via context menu, no full-editor escape hatch, and the non-empty delete guard', async ({ page }) => {
  await login(page)
  await page.goto('/media')
  await expect(page).toHaveURL(/\/media$/)

  // 1. Create folder.
  const folderName = `C-${STAMP}`
  createdFolderId = await createFolder(page, folderName)

  // 2. Enter it, upload a file, and confirm filename -> Title autofill on the real backend.
  await enterFolder(page, folderName)
  const fileBase = `photo-${STAMP}`
  const fileName = `${fileBase}.png`
  uploadedFileId = await uploadOne(page, fileName)
  await expect(fileTile(page, fileName)).toBeVisible()

  await openDetail(page, fileName)
  await expect(mdField(page, 'Title').locator('input')).toHaveValue(fileBase)

  // 3. Edit Alt text and Save -- a save that never touches folder assignment (the dialog has no
  // way to touch it any more) is exactly the scenario that silently unfiles the item if this
  // guard regresses. The tile-still-visible check is a cheap UI-level sanity check only; the real
  // proof is the server GET below, which cannot race the grid's own post-save reload the way a
  // DOM assertion can.
  const alt = `Alt ${STAMP}`
  await mdField(page, 'Alt text').locator('input').fill(alt)
  await saveDetail(page)
  await expect(fileTile(page, fileName)).toBeVisible()
  await assertServerFolder(page, uploadedFileId!, createdFolderId!)

  // Reopen for a FRESH GET (not trusting in-memory form state) and confirm Alt truly persisted
  // server-side, then confirm there is no "open in full editor" escape hatch anywhere in the
  // detail dialog, then close it so the grid underneath becomes interactive again.
  await openDetail(page, fileName)
  await expect(mdField(page, 'Alt text').locator('input')).toHaveValue(alt)
  await expect(detailDialog(page).getByRole('button', { name: /full editor/i })).toHaveCount(0)
  await expect(detailDialog(page).getByRole('link', { name: /full editor/i })).toHaveCount(0)
  await page.keyboard.press('Escape')
  await expect(detailDialog(page)).toHaveCount(0)

  // 4. Move to root ("Uncategorized") via the grid tile's "Move to…" context menu -> the file
  // leaves the folder and becomes root-browsable.
  await moveFileTo(page, fileName, 'Root')
  await expect(page.getByText('No media files')).toBeVisible()

  await goToBreadcrumbRoot(page)
  await expect(fileTile(page, fileName)).toBeVisible()

  // 5. Put the file back in the folder, then attempt to delete the (now non-empty) folder ->
  // rejected with a 409 + warning toast, and the folder card remains.
  await moveFileTo(page, fileName, folderName)

  const rejectedStatus = await attemptDeleteFolder(page, folderName)
  expect(rejectedStatus).toBe(409)
  // useToast() (see composables/useToast.ts) bridges to vue-sonner: the visible toast is a plain
  // styled <li> with no role, and sonner's accessibility announcement is a separate off-screen
  // `<section aria-live="polite">` -- neither carries role="alert", so this must match on visible
  // text rather than the alert role.
  await expect(page.getByText('Folder is not empty', { exact: false })).toBeVisible()
  await expect(folderCard(page, folderName)).toBeVisible()

  // 6. Empty the folder, then delete succeeds and the card disappears. The file is currently
  // filed under the folder (step 5 put it back), so the ROOT view (Uncategorized-only) does not
  // show it -- enter the folder to reach it, move it back to root, then hop back up to root where
  // the folder's own card renders (a folder never shows its own card while browsing inside it).
  await enterFolder(page, folderName)
  await moveFileTo(page, fileName, 'Root')
  await goToBreadcrumbRoot(page)

  const okStatus = await attemptDeleteFolder(page, folderName)
  expect(okStatus).toBe(204)
  await expect(folderCard(page, folderName)).toHaveCount(0)
})
