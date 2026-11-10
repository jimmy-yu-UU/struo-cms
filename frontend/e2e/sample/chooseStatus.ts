import { expect, type Page } from '@playwright/test'

// Article.Status renders SelectField.vue's ui/select: a role="combobox" trigger plus a
// role="option" overlay, so it cannot be `.fill()`ed.
//
// An option click has been seen to leave the popup open with nothing selected (not reproduced; the
// suspect is reka-ui's pointerup timing). The loop clicks the option only while the popup is open
// and repeats until it collapses; the final assertion checks the trigger shows the chosen label.
export async function chooseStatus(page: Page, optionLabel: 'Draft' | 'Published'): Promise<void> {
  const field = page.locator('.field', { has: page.getByText(/^Status\*?$/) })
  // CSS, not getByRole: an open popup marks the rest of the page aria-hidden, which hides the
  // trigger from role queries.
  const combobox = field.locator('[role="combobox"]')
  const option = page.getByRole('option', { name: optionLabel, exact: true })

  await combobox.click()
  await expect(async () => {
    if ((await combobox.getAttribute('aria-expanded', { timeout: 1_000 })) === 'true') {
      await option.click({ timeout: 2_000 })
    }
    await expect(combobox).toHaveAttribute('aria-expanded', 'false', { timeout: 2_000 })
  }).toPass({ timeout: 15_000 })
  await expect(combobox).toHaveText(optionLabel)
}
