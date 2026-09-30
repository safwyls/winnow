import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-cover-actions-app-'))
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/action-overlays-main.mjs'),
      '--data-dir',
      directory,
      '--seed-sample',
      '--no-sync',
    ],
    env: Object.fromEntries(
      Object.entries(process.env).filter(
        ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
      ),
    ) as Record<string, string>,
    chromiumSandbox: true,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible({ timeout: 45000 })
})
test.afterAll(async () => closeFixture(application, directory))
const launches = () => application.evaluate(() => (globalThis as any).overlayLaunches)
async function surface(fullscreen: boolean) {
  await application.evaluate(({ BrowserWindow }, fullscreen) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setContentSize(1280, 800)
    window.webContents.send('winnow:fullscreen:changed', fullscreen)
    window.focus()
  }, fullscreen)
  await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(fullscreen ? 'fullscreen' : 'desktop'))
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
}
test('desktop cover controls dispatch through the production bridge and reopen the real Details modal', async ({}, info) => {
  await surface(false)
  await page.getByRole('button', { name: 'Grid view', exact: true }).click()
  const tile = page.locator('.avalon-library .avalon-desktop-cover').first()
  const cover = tile.locator('.avalon-cover')
  const title = await cover.locator('.avalon-cover-fallback').textContent()
  const before = await launches()
  await cover.hover({ position: { x: 50, y: 65 } })
  await tile.getByRole('button', { name: 'Play', exact: true }).click()
  await expect.poll(launches).toBe(before + 1)
  await expect(page.locator('.avalon-details')).toHaveCount(0)
  await page.screenshot({ path: info.outputPath('desktop-actions.png') })
  for (let attempt = 0; attempt < 3; attempt++) {
    await cover.hover({ position: { x: 50, y: 65 } })
    await tile.getByRole('button', { name: 'Details', exact: true }).click()
    await expect(
      page.locator('.avalon-details').getByRole('heading', { name: title!, exact: true }),
    ).toBeVisible()
    await page.keyboard.press('Escape')
    await expect(page.locator('.avalon-details')).toHaveCount(0)
  }
  await cover.click({ position: { x: 50, y: 65 } })
  await expect(
    page.locator('.avalon-details').getByRole('heading', { name: title!, exact: true }),
  ).toBeVisible()
  await page.keyboard.press('Escape')
  await cover.click({ modifiers: ['Control'], position: { x: 50, y: 65 } })
  await expect(page.locator('.avalon-details')).toHaveCount(0)
  await expect(cover).toHaveAttribute('data-selected', 'true')
  expect(await launches()).toBe(before + 1)
  expect(errors).toEqual([])
})
test('fullscreen retains cover keyboard navigation and its Details launch through the same bridge', async () => {
  await surface(true)
  await expect(page.locator('.avalon-desktop-cover')).toHaveCount(0)
  const first = page.locator('.avalon-library .avalon-cover').first()
  await first.focus()
  await page.keyboard.press('ArrowRight')
  const selected = page.locator('.avalon-library .avalon-cover:focus')
  const title = await selected.locator('.avalon-cover-fallback').textContent()
  await page.keyboard.press('Enter')
  await expect(
    page.locator('.avalon-details').getByRole('heading', { name: title!, exact: true }),
  ).toBeVisible()
  const before = await launches()
  await page.locator('.avalon-details').getByRole('button', { name: 'Play', exact: true }).first().focus()
  await page.keyboard.press('Enter')
  await expect.poll(launches).toBe(before + 1)
  await page.keyboard.press('Escape')
  await expect(page.locator('.avalon-details')).toHaveCount(0)
  expect(errors).toEqual([])
})
