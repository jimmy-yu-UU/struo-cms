import { test, expect } from './fixtures'
import { type Page } from '@playwright/test'

// Media-library folders live gate. Covers folder browsing, filename -> Title autofill (already
// implemented server-side by Struo.Infrastructure.Files.FileService.UploadAsync), the absence of
// an "open in full editor" escape hatch on the file detail dialog, and a data-loss regression:
// MediaDetailDialog.load() must fetch
// the item with `deep=folder` so its Folder TreeSelect reflects the file's REAL folder before any
// Save -- before that fix, `folderId` came back `undefined` on every load, so any Save (even one
// that only touched Alt text) silently sent `folderId: null` and unfiled the file.
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

// Opens a file's detail dialog and waits for its `deep=folder` GET to resolve (MediaDetailDialog's
// load()) so fields -- including the Folder TreeSelect -- are populated before we assert on them.
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

// Opens the Folder TreeSelect's overlay (its one visible button is the dropdown toggle -- no clear
// icon is rendered here since the field is never empty, "Uncategorized" being a real option) and
// picks the given node by its exact label.
async function selectFolder(page: Page, label: string): Promise<void> {
  await mdField(page, 'Folder').getByRole('button').click()
  await page.getByRole('treeitem', { name: label, exact: true }).click()
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

// Clicks a folder card's Delete action and confirms via the store-backed ConfirmHost's alertdialog.
// Its accept label is common.confirm ("Confirm") -- lib/deleteAction.ts passes no explicit
// acceptLabel, so the host's default applies. Returns the DELETE request's status so callers can
// assert 204 (empty, succeeds) vs 409 (non-empty, rejected) deterministically instead of only
// polling the UI.
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

test('media folders: create, upload with title autofill, folder survives a save, move to Uncategorized, no full-editor escape hatch, and the non-empty delete guard', async ({ page }) => {
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

  // 3. Regression guard: the Folder TreeSelect must already show the REAL folder (not
  // "Uncategorized") on this very first load, before any Save has happened.
  await expect(mdField(page, 'Folder').getByRole('combobox')).toHaveAccessibleName(`Folder ${folderName}`)

  // Edit Alt text and Save -- a save that never touches the Folder field is exactly the scenario
  // that used to silently unfile the item.
  const alt = `Alt ${STAMP}`
  await mdField(page, 'Alt text').locator('input').fill(alt)
  await saveDetail(page)

  // Still browsable inside the SAME folder after the save (not silently moved to Uncategorized).
  await expect(fileTile(page, fileName)).toBeVisible()

  // Reopen for a FRESH GET (not trusting in-memory form state) and confirm the folder truly
  // persisted server-side, not just in the form.
  await openDetail(page, fileName)
  await expect(mdField(page, 'Folder').getByRole('combobox')).toHaveAccessibleName(`Folder ${folderName}`)
  await expect(mdField(page, 'Alt text').locator('input')).toHaveValue(alt)

  // 5. No "open in full editor" escape hatch anywhere in the detail dialog.
  await expect(detailDialog(page).getByRole('button', { name: /full editor/i })).toHaveCount(0)
  await expect(detailDialog(page).getByRole('link', { name: /full editor/i })).toHaveCount(0)

  // 4. Move to Uncategorized -> the file leaves the folder and becomes root-browsable.
  await selectFolder(page, 'Uncategorized')
  await saveDetail(page)
  await expect(page.getByText('No media files')).toBeVisible()

  await goToBreadcrumbRoot(page)
  await expect(fileTile(page, fileName)).toBeVisible()

  // 6. Put the file back in the folder, then attempt to delete the (now non-empty) folder ->
  // rejected with a 409 + warning toast, and the folder card remains.
  await openDetail(page, fileName)
  await selectFolder(page, folderName)
  await saveDetail(page)

  const rejectedStatus = await attemptDeleteFolder(page, folderName)
  expect(rejectedStatus).toBe(409)
  await expect(page.getByRole('alert').filter({ hasText: 'Folder is not empty' })).toBeVisible()
  await expect(folderCard(page, folderName)).toBeVisible()

  // 7. Empty the folder, then delete succeeds and the card disappears. The file is currently
  // filed under the folder (step 6 put it back), so the ROOT view (Uncategorized-only) no longer
  // shows it -- enter the folder to reach it, then hop back up to root where the folder's own
  // card renders (a folder never shows its own card while browsing inside it).
  await enterFolder(page, folderName)
  await openDetail(page, fileName)
  await selectFolder(page, 'Uncategorized')
  await saveDetail(page)
  await goToBreadcrumbRoot(page)

  const okStatus = await attemptDeleteFolder(page, folderName)
  expect(okStatus).toBe(204)
  await expect(folderCard(page, folderName)).toHaveCount(0)
})
