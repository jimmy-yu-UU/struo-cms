import { expect, type Page } from '@playwright/test'

// Article.Status is [CmsField(Interface = FieldInterface.Select)] with
// CmsOptions("draft:Draft", "published:Published"), so FieldInput renders SelectField.vue's ui/select
// (a role="combobox" trigger plus a role="option" overlay), not a text input that `.fill()` could set.
//
// reka-ui's SelectContent registers a one-shot capture-phase `pointerup` listener that calls
// preventDefault() while the trigger's pointer-down position is still recorded, and SelectItem
// ignores a `pointerup` whose default is prevented. When the overlay mounts after the trigger's own
// pointerup, that listener consumes the option's pointerup instead: the click lands, nothing is
// selected, and the popup stays open. The one-shot listener is spent afterwards, so a second option
// click selects.
//
// The loop therefore confirms the outcome (popup collapsed) and re-clicks the option while it is
// still open, and the final assertion confirms the trigger shows the chosen label.
export async function chooseStatus(page: Page, optionLabel: 'Draft' | 'Published'): Promise<void> {
  const field = page.locator('.field', { has: page.getByText(/^Status\*?$/) })
  const combobox = field.getByRole('combobox')
  const option = page.getByRole('option', { name: optionLabel })

  await combobox.click()
  await expect(async () => {
    await option.click({ timeout: 2_000 })
    await expect(combobox).toHaveAttribute('aria-expanded', 'false', { timeout: 2_000 })
  }).toPass({ timeout: 15_000 })
  await expect(combobox).toContainText(optionLabel)
}
