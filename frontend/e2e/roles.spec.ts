import { test, expect } from './fixtures'
import { type Page } from '@playwright/test'
import { e2ePassword } from './credentials'

// Live gate: what the role and user forms tell the administrator when an action is not available or a
// save loses to another administrator's, and what a user form saves for an administrator who cannot
// read roles.
//
// The roles and users the conflict test and the roles test create are stamped, created through the API
// and purged in `finally`. The other tests only read: the seeded super-admin role and the bootstrap
// admin's own account.

const EMAIL = process.env.E2E_EMAIL ?? 'admin@admin.com'
const PASSWORD = e2ePassword()
const STAMP = `${process.env.E2E_STAMP ?? 'e2e'}-${Date.now()}`
// localhost (not 127.0.0.1) so page.request carries the app's auth cookie.
const API = process.env.E2E_API ?? 'http://localhost:5221'
const CSRF = { 'X-Struo-CSRF': '1' }

const SAVE_PERMISSIONS = 'Save permissions'

const EDITOR_EMAIL = `e2e-user-editor-${STAMP}@struo.test`
const TARGET_EMAIL = `e2e-role-holder-${STAMP}@struo.test`
const ACCOUNT_PASSWORD = `Roles-${STAMP}-pw`

async function login(page: Page, email: string = EMAIL, password: string = PASSWORD): Promise<void> {
  await page.goto('/')
  await expect(page).toHaveURL(/\/login$/)
  await page.fill('input[type="email"]', email)
  await page.fill('input[type="password"]', password)
  await page.click('button[type="submit"]')
  await expect(page).toHaveURL(/\/$/)
}

async function superAdminRoleId(page: Page): Promise<string> {
  const res = await page.request.get(`${API}/api/items/role?limit=100`)
  expect(res.ok(), `GET /api/items/role -> ${res.status()}`).toBeTruthy()
  const roles = (await res.json()).data as Array<{ id: string; isSuperAdmin: boolean }>
  const role = roles.find((r) => r.isSuperAdmin)
  if (!role) throw new Error('The database has no super-admin role.')
  return String(role.id)
}

test('a super-admin role shows why it cannot be deleted next to the disabled Delete button', async ({ page }) => {
  await login(page)
  await page.goto(`/collections/role/${await superAdminRoleId(page)}`)

  const reason = 'A super-admin role cannot be deleted.'
  const deleteButton = page.getByRole('button', { name: 'Delete', exact: true })
  await expect(deleteButton).toBeDisabled()
  await expect(page.getByRole('note').filter({ hasText: reason })).toBeVisible()

  // The button points at the visible note, so the reason is not only in a tooltip a disabled button never shows.
  const describedBy = await deleteButton.getAttribute('aria-describedby')
  expect(describedBy).toBeTruthy()
  await expect(page.locator(`[id="${describedBy}"]`)).toHaveText(reason)
})

test('a permissions save that loses to another administrator says so about the permissions, takes focus, and clears once saved', async ({ page }) => {
  await login(page)
  const created = await page.request.post(`${API}/api/items/role`, {
    data: { name: `e2e-matrix-${STAMP}`, isSuperAdmin: false }, headers: CSRF,
  })
  expect(created.status()).toBe(201)
  const roleId = String((await created.json()).data.id)
  try {
    await page.goto(`/collections/role/${roleId}`)
    const read = page.getByRole('checkbox', { name: 'Language — Read' })
    await expect(read).toBeVisible()

    // Another administrator saves this role's grants after the form loaded: the role's version moves on.
    const other = await page.request.put(`${API}/api/roles/${roleId}/permissions`, {
      data: [{ collection: 'user', canRead: true, canWrite: false, canDelete: false }], headers: CSRF,
    })
    expect(other.ok(), `PUT permissions -> ${other.status()}`).toBeTruthy()

    await read.click()
    await page.getByRole('button', { name: SAVE_PERMISSIONS }).click()

    const banner = page.locator('.matrix-conflict-banner')
    await expect(banner).toBeVisible()
    await expect(banner).toContainText("This role's permissions were changed by someone else.")
    await expect(banner).toBeFocused()
    await expect(page.locator('.conflict-banner')).toHaveCount(0)
    await expect(read).toBeChecked()

    // The second save carries the version the conflict brought back, so it goes through.
    await page.getByRole('button', { name: SAVE_PERMISSIONS }).click()
    await expect(page.getByText('Permissions saved', { exact: true })).toBeVisible()
    await expect(banner).toHaveCount(0)

    const stored = await page.request.get(`${API}/api/roles/${roleId}/permissions`)
    const grants = (await stored.json()).data as Array<{ collection: string; canRead: boolean }>
    expect(grants.find((g) => g.collection === 'language')?.canRead).toBe(true)
  } finally {
    await page.request.delete(`${API}/api/items/role/${roleId}?purge=true`, { headers: CSRF }).catch(() => undefined)
  }
})

test('an address whose id cannot be an id explains itself and offers the way back', async ({ page }) => {
  await login(page)
  await page.goto('/collections/role/not-an-id')

  await expect(page.getByText('This item could not be loaded. The address may contain an id that is not valid.')).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Edit Role' })).toHaveCount(0)

  await page.getByRole('button', { name: 'Back to list' }).click()
  await expect(page).toHaveURL(/\/collections\/role$/)
})

test('the user form names the token section as the password section does', async ({ page }) => {
  await login(page)
  const me = await page.request.get(`${API}/api/auth/me`)
  expect(me.ok(), `GET /api/auth/me -> ${me.status()}`).toBeTruthy()
  const userId = String((await me.json()).data.id)

  await page.goto(`/collections/user/${userId}`)

  await expect(page.getByRole('heading', { name: 'API access token', exact: true })).toBeVisible()
})

test('a user form saved by an administrator who cannot read roles leaves the roles of the user as they are', async ({ page, browser }) => {
  const created: string[] = []
  const editorContext = await browser.newContext()
  try {
    await login(page)

    const makeRole = async (name: string): Promise<string> => {
      const res = await page.request.post(`${API}/api/items/role`, { data: { name }, headers: CSRF })
      expect(res.status()).toBe(201)
      const id = String((await res.json()).data.id)
      created.push(`/api/items/role/${id}?purge=true`)
      return id
    }
    const makeUser = async (email: string, roleId: string): Promise<string> => {
      const res = await page.request.post(`${API}/api/users`, {
        data: { email, password: ACCOUNT_PASSWORD }, headers: CSRF,
      })
      expect(res.status()).toBe(201)
      const id = String((await res.json()).data.id)
      created.push(`/api/items/user/${id}?purge=true`)
      const assign = await page.request.put(`${API}/api/items/user/${id}`, {
        data: { roles: [roleId] }, headers: CSRF,
      })
      expect(assign.ok(), `PUT roles -> ${assign.status()}`).toBe(true)
      return id
    }

    const editorRole = await makeRole(`e2e-user-editor-${STAMP}`)
    const keptRole = await makeRole(`e2e-kept-${STAMP}`)
    await makeUser(EDITOR_EMAIL, editorRole)
    const targetId = await makeUser(TARGET_EMAIL, keptRole)
    // Read and write on users, nothing on roles.
    const grants = await page.request.put(`${API}/api/roles/${editorRole}/permissions`, {
      data: [{ collection: 'user', canRead: true, canWrite: true, canDelete: false }], headers: CSRF,
    })
    expect(grants.ok(), `PUT permissions -> ${grants.status()}`).toBe(true)

    const editor = await editorContext.newPage()
    await editor.addInitScript(() => {
      try { localStorage.setItem('struo.uiLocale', 'en') } catch { /* ignore */ }
    })
    await login(editor, EDITOR_EMAIL, ACCOUNT_PASSWORD)
    await editor.goto(`/collections/user/${targetId}`)

    // The Roles field is there, disabled, and says why.
    const roles = editor.locator('[data-field-key="roles"]')
    await expect(roles.getByRole('textbox')).toBeDisabled()
    await expect(roles).toContainText('so this field is not shown and stays as it is.')

    const newName = `Renamed ${STAMP}`
    await editor.locator('[data-field-key="name"] input').fill(newName)
    const [saved] = await Promise.all([
      editor.waitForResponse((r) => r.request().method() === 'PUT' && r.url().includes(`/api/items/user/${targetId}`)),
      editor.getByRole('button', { name: 'Save', exact: true }).click(),
    ])
    expect(saved.status()).toBe(200)
    expect(Object.keys(saved.request().postDataJSON() as Record<string, unknown>)).not.toContain('roles')

    const after = await page.request.get(`${API}/api/items/user/${targetId}?deep=roles`)
    expect(after.ok(), `GET user -> ${after.status()}`).toBe(true)
    const stored = (await after.json()).data as { name: string; roles: Array<{ id: string }> }
    expect(stored.name).toBe(newName)
    expect(stored.roles.map((r) => String(r.id))).toEqual([keptRole])
  } finally {
    // Users first: they reference the roles. A failed delete must not hide the test's own result.
    for (const path of [...created].reverse()) {
      await page.request.delete(`${API}${path}`, { headers: CSRF }).catch(() => undefined)
    }
    await editorContext.close()
  }
})
