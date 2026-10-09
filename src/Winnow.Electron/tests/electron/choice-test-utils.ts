import { expect, type Locator, type Page } from '@playwright/test'

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

/** Palette swatches stay visible in Atmosphere; IDs remain stable when an authored name changes. */
export function paletteSwatch(page: Page, id: string) {
  return page
    .getByRole('region', { name: 'Atmosphere', exact: true })
    .getByRole('group', { name: 'Palette', exact: true })
    .locator('button[value=' + JSON.stringify(id) + ']')
}
export async function choosePalette(page: Page, id: string) {
  const swatch = paletteSwatch(page, id)
  await swatch.click()
  await expect(swatch).toHaveAttribute('aria-pressed', 'true')
}
