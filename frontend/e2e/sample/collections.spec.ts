import { test, expect } from '../fixtures'

const EMAIL = process.env.E2E_EMAIL ?? 'admin@admin.com'
const PASSWORD = process.env.E2E_PASSWORD ?? 'admin'

test('browse a collection list', async ({ page }) => {
  await page.goto('/')
  await expect(page).toHaveURL(/\/login$/)
  await page.fill('input[type="email"]', EMAIL)
  await page.fill('input[type="password"]', PASSWORD)
  await page.click('button[type="submit"]')
  await expect(page).toHaveURL(/\/$/)

  // FE-R1: the sidebar is now an <aside> with collection links rendered directly as buttons
  // (no PanelMenu); the "Content" group is expanded by default, so no group-expand click needed.
  await page.locator('aside.sidebar').getByRole('button', { name: 'Article', exact: true }).click()
  await expect(page).toHaveURL(/\/collections\/article$/)

  // The list renders its column header (DefaultDisplayField is still "Status" on Article).
  await expect(page.getByText('Status', { exact: true })).toBeVisible()

  // FE-R1: logout moved into the UserMenu popover (no more standalone `button.logout`).
  await page.getByRole('button', { name: 'Account' }).click()
  await page.getByRole('menuitem', { name: 'Log out' }).click()
  await expect(page).toHaveURL(/\/login$/)
})
