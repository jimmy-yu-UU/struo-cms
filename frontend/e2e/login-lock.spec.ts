import { test, expect } from './fixtures'
import { type APIRequestContext, type Page } from '@playwright/test'
import { e2ePassword } from './credentials'

// Live gate: a super-admin gives a role read and write on the user collection; an account is locked by
// wrong passwords; a user holding that role clears the lock from the user list's Login lock column;
// the locked account then signs in again.
//
// Every record is stamped and created through the API, and removed in `finally`. The accounts used
// here are new ones, so the throttle counters of the bootstrap admin that other specs sign in with
// are untouched.

const ADMIN_EMAIL = process.env.E2E_EMAIL ?? 'admin@admin.com'
const ADMIN_PASSWORD = e2ePassword()
const STAMP = `${process.env.E2E_STAMP ?? 'e2e'}-${Date.now()}`
// localhost (not 127.0.0.1) so page.request carries the app's auth cookie.
const API = process.env.E2E_API ?? 'http://localhost:5221'
const CSRF = { 'X-Struo-CSRF': '1' }

const UNLOCKER_EMAIL = `e2e-unlocker-${STAMP}@struo.test`
const VICTIM_EMAIL = `e2e-victim-${STAMP}@struo.test`
const ACCOUNT_PASSWORD = `Unlock-${STAMP}-pw`
// Default RateLimiting:LoginAccount:PermitLimit is 10, so the 11th attempt is the first refused;
// the loop stops at the first 429 and the margin covers a fork that raised the limit a little.
const MAX_ATTEMPTS = 40

async function signIn(page: Page, email: string, password: string): Promise<void> {
  await page.goto('/login')
  await page.fill('input[type="email"]', email)
  await page.fill('input[type="password"]', password)
  await page.click('button[type="submit"]')
  await expect(page).toHaveURL(/\/$/)
}

async function lockByWrongPasswords(anonymous: APIRequestContext, email: string): Promise<void> {
  for (let attempt = 0; attempt < MAX_ATTEMPTS; attempt += 1) {
    const res = await anonymous.post(`${API}/api/auth/login`, {
      data: { email, password: 'definitely-not-the-password' },
      headers: CSRF,
    })
    if (res.status() === 429) return
  }
  throw new Error(`The account was still not locked after ${MAX_ATTEMPTS} wrong passwords.`)
}

test('a role with write on the user collection lets its user clear an account lock from the user list', async ({ page, browser, playwright }) => {
  const created: Array<{ path: string }> = []
  const anonymous = await playwright.request.newContext()
  const unlockerContext = await browser.newContext()
  try {
    await signIn(page, ADMIN_EMAIL, ADMIN_PASSWORD)

    const roleRes = await page.request.post(`${API}/api/items/role`, {
      data: { name: `e2e-unlock-${STAMP}`, isSuperAdmin: false }, headers: CSRF,
    })
    expect(roleRes.status()).toBe(201)
    const roleId = String((await roleRes.json()).data.id)
    created.push({ path: `/api/items/role/${roleId}?purge=true` })

    const makeUser = async (email: string): Promise<string> => {
      const res = await page.request.post(`${API}/api/users`, {
        data: { email, password: ACCOUNT_PASSWORD }, headers: CSRF,
      })
      expect(res.status()).toBe(201)
      const id = String((await res.json()).data.id)
      created.push({ path: `/api/items/user/${id}?purge=true` })
      return id
    }
    const unlockerId = await makeUser(UNLOCKER_EMAIL)
    await makeUser(VICTIM_EMAIL)
    const assign = await page.request.put(`${API}/api/items/user/${unlockerId}`, {
      data: { roles: [roleId] }, headers: CSRF,
    })
    expect(assign.ok()).toBe(true)

    // Read and write on the user collection are the grants that show and clear a lock.
    const grants = await page.request.put(`${API}/api/roles/${roleId}/permissions`, {
      data: [{ collection: 'user', canRead: true, canWrite: true, canDelete: false }], headers: CSRF,
    })
    expect(grants.ok()).toBe(true)

    await lockByWrongPasswords(anonymous, VICTIM_EMAIL)

    // The holder of the role finds the victim's row in the user list and clears the lock there.
    const unlocker = await unlockerContext.newPage()
    await unlocker.addInitScript(() => {
      try { localStorage.setItem('struo.uiLocale', 'en') } catch { /* ignore */ }
    })
    await signIn(unlocker, UNLOCKER_EMAIL, ACCOUNT_PASSWORD)
    await unlocker.goto('/collections/user')
    await unlocker.getByRole('button', { name: `Unlock ${VICTIM_EMAIL}` }).click()
    await unlocker.getByRole('alertdialog', { name: 'Clear login lock' }).getByRole('button', { name: 'Confirm' }).click()
    await expect(unlocker.getByText('Lock cleared')).toBeVisible()
    await expect(unlocker.getByRole('button', { name: `Unlock ${VICTIM_EMAIL}` })).toHaveCount(0)

    const after = await anonymous.post(`${API}/api/auth/login`, {
      data: { email: VICTIM_EMAIL, password: ACCOUNT_PASSWORD }, headers: CSRF,
    })
    expect(after.status()).toBe(200)
  } finally {
    // Users first: they reference the role. A failed delete must not hide the test's own result.
    for (const { path } of [...created].reverse()) {
      await page.request.delete(`${API}${path}`, { headers: CSRF }).catch(() => undefined)
    }
    await unlockerContext.close()
    await anonymous.dispose()
  }
})
