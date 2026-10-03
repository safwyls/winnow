import { expect, type Page } from '@playwright/test'
import { libraryAction, returnToLibrary } from './library-controls'
import { chooseOption } from './choice-test-utils'

const picker = (page: Page) => page.locator('.avalon-list-picker button[aria-haspopup="menu"]')
async function openPicker(page: Page) {
  const control = picker(page)
  if ((await control.getAttribute('aria-expanded')) !== 'true') await control.click()
  const id = await control.getAttribute('aria-controls')
  if (!id) throw Error('List picker did not open its menu')
  return page.locator(`[id=${JSON.stringify(id)}]`)
}
export async function collectionChoice(page: Page, id: number | string) {
  if (await page.locator('.avalon-shell.fullscreen').count()) {
    await returnToLibrary(page)
    await page.getByRole('button', { name: 'My lists', exact: true }).click()
    return page.locator(`[data-avalon-list="${id}"]`)
  }
  return (await picker(page).count())
    ? (await openPicker(page)).locator(`[role="menuitemradio"][value="${id}"]`)
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
  if (await picker(page).count()) await chooseOption(picker(page), String(id))
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
  if (await picker(page).count()) return (await picker(page).getAttribute('value')) ?? 'all'
  const selected = page.locator('[data-avalon-list][aria-pressed="true"]')
  return (await selected.count()) ? (await selected.getAttribute('data-avalon-list'))! : 'all'
}
export async function expectCollection(page: Page, id: number | 'all') {
  if (await page.locator('.avalon-shell.fullscreen').count()) {
    await expect(page.locator('.avalon-library')).toHaveAttribute('data-list-id', String(id))
    return
  }
  if (await picker(page).count()) await expect(picker(page)).toHaveAttribute('value', String(id))
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
            values.map((value) => value.replace(/ · [\d,]+ games?\b/, '').replace(' · Live list', ' · Live')),
          ),
      )
      .toEqual(names)
    await returnToLibrary(page)
    return
  }
  if (await picker(page).count()) {
    const menu = await openPicker(page)
    await expect(menu.getByRole('menuitemradio')).toHaveText(['All games', ...names])
    await menu.press('Escape')
  } else await expect(page.locator('[data-avalon-list] > span')).toHaveText(names)
}
