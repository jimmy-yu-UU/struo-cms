import { test, expect } from '@playwright/test'

const EMAIL = process.env.E2E_EMAIL ?? 'admin@struo.local'
const PASSWORD = process.env.E2E_PASSWORD ?? 'change-me-please'

test('browse a collection list', async ({ page }) => {
  await page.goto('/')
  await expect(page).toHaveURL(/\/login$/)
  await page.fill('input[type="email"]', EMAIL)
  await page.fill('input[type="password"]', PASSWORD)
  await page.click('button[type="submit"]')
  await expect(page).toHaveURL(/\/$/)

  // Open the Article collection from the nav (PanelMenu group must be expanded to reveal the leaf).
  await page.getByText('Content', { exact: true }).click()
  await page.getByText('Article', { exact: true }).click()
  await expect(page).toHaveURL(/\/collections\/article$/)

  // The list renders its column header (DefaultDisplayField "Status").
  await expect(page.getByText('Status', { exact: true })).toBeVisible()

  await page.click('button.logout')
  await expect(page).toHaveURL(/\/login$/)
})
