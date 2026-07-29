import { test, expect } from '../fixtures'

// Phase 9a-fe: the SPA detects a 404 by the response envelope's error code
// (`error.code === 'NOT_FOUND'`), not by matching the error message text.
// This drives ItemFormView's "Item not found." state end-to-end against the
// live 9a envelope.

const EMAIL = process.env.E2E_EMAIL ?? 'admin@struo.local'
const PASSWORD = process.env.E2E_PASSWORD ?? 'change-me-please'

test('a non-existent item id renders the not-found view (driven by error code)', async ({ page }) => {
  await page.goto('/')
  await expect(page).toHaveURL(/\/login$/)
  await page.fill('input[type="email"]', EMAIL)
  await page.fill('input[type="password"]', PASSWORD)
  await page.click('button[type="submit"]')
  await expect(page).toHaveURL(/\/$/)

  // A well-formed but non-existent article id → the API returns a 404
  // `{ success:false, error:{ code:'NOT_FOUND', … } }` envelope. The view must
  // show "Item not found." via the code branch (not the old message match).
  await page.goto('/collections/article/00000000-0000-0000-0000-000000000000')
  // en locale's itemForm.itemNotFound has no trailing period ("Item not found").
  await expect(page.getByText('Item not found', { exact: true })).toBeVisible()
})
