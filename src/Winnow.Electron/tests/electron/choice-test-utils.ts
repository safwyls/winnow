import { expect, type Locator } from '@playwright/test'

/** Exercise the themed popup instead of mutating the selected value directly. */
export async function chooseOption(control: Locator, value: string) {
  if ((await control.getAttribute('aria-expanded')) !== 'true') await control.click()
  const id = await control.getAttribute('aria-controls')
  if (!id) throw new Error('Choice control did not open its menu')
  const menu = control.page().locator(`[id=${JSON.stringify(id)}]`)
  const options = menu.getByRole('menuitemradio')
  const index = await options.evaluateAll(
    (items, selected) => items.findIndex((item) => (item as HTMLButtonElement).value === selected),
    value,
  )
  if (index < 0) throw new Error(`No choice ${JSON.stringify(value)}`)
  await expect(options.nth(index)).toBeEnabled()
  await options.nth(index).click()
}
