import { test, expect } from '@playwright/test'

const EMAIL = process.env.E2E_EMAIL ?? 'admin@struo.local'
const PASSWORD = process.env.E2E_PASSWORD ?? 'change-me-please'

test('login → dashboard → logout', async ({ page }) => {
  // Visiting a protected route while unauthenticated redirects to login.
  await page.goto('/')
  await expect(page).toHaveURL(/\/login$/)

  await page.fill('input[type="email"]', EMAIL)
  await page.fill('input[type="password"]', PASSWORD)
  await page.click('button[type="submit"]')

  await expect(page).toHaveURL(/\/$/)
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()

  await page.click('button.logout')
  await expect(page).toHaveURL(/\/login$/)
})
