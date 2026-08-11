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

  // The sidebar's nav landmark is a real <nav aria-label="Main navigation"> (no <aside>);
  // collection links render directly as buttons (no PanelMenu), and the "Content" group is
  // expanded by default, so no group-expand click needed.
  await page.getByRole('navigation', { name: 'Main navigation' }).getByRole('button', { name: 'Article', exact: true }).click()
  await expect(page).toHaveURL(/\/collections\/article$/)

  // The list renders its column header (DefaultDisplayField is still "Status" on Article).
  await expect(page.getByText('Status', { exact: true })).toBeVisible()

  // Logout is in the UserMenu popover (no standalone `button.logout`).
  await page.getByRole('button', { name: 'Account' }).click()
  await page.getByRole('menuitem', { name: 'Log out' }).click()
  await expect(page).toHaveURL(/\/login$/)
})

test('the Title column header sorts (three-state cycle)', async ({ page }) => {
  await page.goto('/')
  await expect(page).toHaveURL(/\/login$/)
  await page.fill('input[type="email"]', EMAIL)
  await page.fill('input[type="password"]', PASSWORD)
  await page.click('button[type="submit"]')
  await expect(page).toHaveURL(/\/$/)

  await page.getByRole('navigation', { name: 'Main navigation' }).getByRole('button', { name: 'Article', exact: true }).click()
  await expect(page).toHaveURL(/\/collections\/article$/)

  // Title is the only field in this repo with Sortable = true, so it is the only header that
  // renders a <button> inside its <th>. aria-sort on the <th> is the state SortableHeader owns;
  // asserting it (rather than row order) keeps the test independent of seeded data.
  const titleHeader = page.getByRole('columnheader', { name: /Title/ })
  await expect(titleHeader).toHaveAttribute('aria-sort', 'none')

  await titleHeader.getByRole('button').click()
  await expect(titleHeader).toHaveAttribute('aria-sort', 'ascending')

  await titleHeader.getByRole('button').click()
  await expect(titleHeader).toHaveAttribute('aria-sort', 'descending')

  // Third click clears the sort rather than cycling back to ascending.
  await titleHeader.getByRole('button').click()
  await expect(titleHeader).toHaveAttribute('aria-sort', 'none')
})
