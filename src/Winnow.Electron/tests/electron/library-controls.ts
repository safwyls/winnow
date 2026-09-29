import { expect, type Page } from '@playwright/test'

export async function returnToLibrary(page: Page) {
  const back = page.getByRole('button', { name: 'Back to library', exact: true })
  if (await back.isVisible()) await back.click()
}
export async function libraryAction(page: Page, name: string | RegExp) {
  const action = page.getByRole('button', { name, exact: typeof name === 'string' })
  if (await action.isVisible()) return action
  await returnToLibrary(page)
  if (await action.isVisible()) return action
  await page.getByRole('button', { name: 'More', exact: true }).click()
  return action
}
export async function libraryField(page: Page, name: string) {
  const field = page.getByLabel(name, { exact: true })
  if (await field.isVisible()) return field
  if (!(await page.getByRole('dialog', { name: 'Library options', exact: true }).isVisible()))
    await page.getByRole('button', { name: 'More', exact: true }).click()
  const disclosure = page.getByText('Current search and sort', { exact: true })
  await disclosure.click()
  await expect(field).toBeVisible()
  return field
}
export async function fillLibrarySearch(page: Page, text: string) {
  await (await libraryField(page, 'Search games')).fill(text)
  await returnToLibrary(page)
}
export async function expectLibrarySearch(page: Page, text: string) {
  await expect(await libraryField(page, 'Search games')).toHaveValue(text)
  await returnToLibrary(page)
}
export async function setLibrarySort(page: Page, sort: string) {
  if (await page.locator('.avalon-shell.fullscreen').count()) {
    await returnToLibrary(page)
    await page.getByRole('button', { name: 'Filter & sort', exact: true }).click()
    const panel = page.getByRole('dialog', { name: 'Library filters', exact: true })
    await panel.getByRole('combobox', { name: 'Sort', exact: true }).selectOption(sort)
    await panel.getByRole('button', { name: 'Apply filters' }).click()
  } else await page.getByLabel('Sort', { exact: true }).selectOption(sort)
}
export async function expectLibrarySort(page: Page, sort: string) {
  await expect(await libraryField(page, 'Sort')).toHaveValue(sort)
  await returnToLibrary(page)
}
