import { expect, type Page } from '@playwright/test'

const picker = (page: Page) => page.getByRole('combobox', { name: 'My lists', exact: true })
export async function collectionChoice(page: Page, id: number | string) {
  return (await picker(page).count())
    ? picker(page).locator(`option[value="${id}"]`)
    : page.locator(`[data-avalon-list="${id}"]`)
}
export async function selectCollection(page: Page, id: number | 'all') {
  if (await picker(page).count()) await picker(page).selectOption(String(id))
  else if (id === 'all')
    await page
      .locator('.avalon-buckets')
      .getByRole('button', { name: /^All games/ })
      .click()
  else await page.locator(`[data-avalon-list="${id}"]`).click()
}
export async function selectedCollection(page: Page) {
  if (await picker(page).count()) return picker(page).inputValue()
  const selected = page.locator('[data-avalon-list][aria-pressed="true"]')
  return (await selected.count()) ? (await selected.getAttribute('data-avalon-list'))! : 'all'
}
export async function expectCollection(page: Page, id: number | 'all') {
  if (await picker(page).count()) await expect(picker(page)).toHaveValue(String(id))
  else if (id === 'all') await expect(page.locator('[data-avalon-list][aria-pressed="true"]')).toHaveCount(0)
  else await expect(page.locator(`[data-avalon-list="${id}"]`)).toHaveAttribute('aria-pressed', 'true')
}
export async function expectCollectionNames(page: Page, names: string[]) {
  if (await picker(page).count())
    await expect(picker(page).locator('option')).toHaveText(['All games', ...names])
  else await expect(page.locator('[data-avalon-list] > span')).toHaveText(names)
}
