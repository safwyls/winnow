import { expect, type Page } from '@playwright/test'
import { libraryAction, returnToLibrary } from './library-controls'

const picker = (page: Page) => page.getByRole('combobox', { name: 'My lists', exact: true })
export async function collectionChoice(page: Page, id: number | string) {
  if (await page.locator('.avalon-shell.fullscreen').count()) {
    await returnToLibrary(page)
    await page.getByRole('button', { name: 'My lists', exact: true }).click()
    return page.locator(`[data-avalon-list="${id}"]`)
  }
  return (await picker(page).count())
    ? picker(page).locator(`option[value="${id}"]`)
    : page.locator(`[data-avalon-list="${id}"]`)
}
export async function selectCollection(page: Page, id: number | 'all') {
  if (await page.locator('.avalon-shell.fullscreen').count()) {
    await returnToLibrary(page)
    if (id === 'all') {
      if ((await page.locator('.avalon-library').getAttribute('data-list-id')) !== 'all')
        await (await libraryAction(page, 'Close list')).click()
      else
        await page
          .locator('.avalon-buckets')
          .getByRole('button', { name: /^All games/ })
          .click()
    } else {
      await page.getByRole('button', { name: 'My lists', exact: true }).click()
      await page.locator(`[data-avalon-list="${id}"]`).click()
    }
    return
  }
  if (await picker(page).count()) await picker(page).selectOption(String(id))
  else if (id === 'all')
    await page
      .locator('.avalon-buckets')
      .getByRole('button', { name: /^All games/ })
      .click()
  else await page.locator(`[data-avalon-list="${id}"]`).click()
}
export async function selectedCollection(page: Page) {
  if (await page.locator('.avalon-shell.fullscreen').count())
    return (await page.locator('.avalon-library').getAttribute('data-list-id'))!
  if (await picker(page).count()) return picker(page).inputValue()
  const selected = page.locator('[data-avalon-list][aria-pressed="true"]')
  return (await selected.count()) ? (await selected.getAttribute('data-avalon-list'))! : 'all'
}
export async function expectCollection(page: Page, id: number | 'all') {
  if (await page.locator('.avalon-shell.fullscreen').count()) {
    await expect(page.locator('.avalon-library')).toHaveAttribute('data-list-id', String(id))
    return
  }
  if (await picker(page).count()) await expect(picker(page)).toHaveValue(String(id))
  else if (id === 'all') await expect(page.locator('[data-avalon-list][aria-pressed="true"]')).toHaveCount(0)
  else await expect(page.locator(`[data-avalon-list="${id}"]`)).toHaveAttribute('aria-pressed', 'true')
}
export async function expectCollectionNames(page: Page, names: string[]) {
  if (await page.locator('.avalon-shell.fullscreen').count()) {
    await returnToLibrary(page)
    await page.getByRole('button', { name: 'My lists', exact: true }).click()
    await expect
      .poll(() =>
        page
          .locator('[data-avalon-list]')
          .allTextContents()
          .then((values) =>
            values.map((value) => value.replace(/ · \d+ games/, '').replace(' · Live list', ' · Live')),
          ),
      )
      .toEqual(names)
    await returnToLibrary(page)
    return
  }
  if (await picker(page).count())
    await expect(picker(page).locator('option')).toHaveText(['All games', ...names])
  else await expect(page.locator('[data-avalon-list] > span')).toHaveText(names)
}
