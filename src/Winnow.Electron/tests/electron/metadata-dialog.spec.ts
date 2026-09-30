import { closeFixture } from './fixture-cleanup'
import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'

let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-details-'))
  const env = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('tests/electron/details-main.mjs'), '--data-dir', directory, '--seed-sample', '--no-sync'],
    env,
    chromiumSandbox: true,
    timeout: 60000,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
})
test.afterAll(async () => closeFixture(application, directory))
async function surface(mode: 'desktop' | 'fullscreen', width: number, height: number) {
  await page.reload()
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
  await application.evaluate(
    ({ BrowserWindow }, value) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setFullScreen(false)
      window.setContentSize(value.width, value.height)
      window.webContents.send('winnow:fullscreen:changed', value.mode === 'fullscreen')
    },
    { mode, width, height },
  )
  await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
  await expect.poll(() => page.evaluate(() => innerWidth)).toBe(width)
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
}
async function editor(mode: 'desktop' | 'fullscreen', width: number, height: number, scale = 1) {
  await surface(mode, width, height)
  await page.evaluate(
    (scale) => document.documentElement.style.setProperty('--theme-text-scale', String(scale)),
    scale,
  )
  await page.locator('.avalon-cover').first().click()
  await page.locator('.avalon-details').getByRole('button', { name: 'More', exact: true }).click()
  await page.getByRole('button', { name: 'Edit details', exact: true }).click()
  await expect(page.locator('.metadata-dialog')).toBeVisible()
  return page.locator('.metadata-dialog')
}
for (const [width, height, scale] of [
  [1200, 640, 1],
  [1280, 820, 1],
  [1920, 1080, 1],
  [800, 700, 1],
  [940, 820, 1.2],
]) {
  test(`metadata overlay at ${width}×${height}, text ${scale} bounds fields and keeps navigation reachable`, async ({}, info) => {
    const dialog = await editor('desktop', width, height, scale)
    const name = dialog.getByRole('textbox', { name: 'Name', exact: true })
    await expect(name).toBeFocused()
    const bounds = (await dialog.boundingBox())!
    expect(bounds.width).toBe(Math.min(width - 48, 1440))
    expect(bounds.x).toBeGreaterThanOrEqual(24)
    expect(bounds.y).toBeGreaterThanOrEqual(24)
    expect(bounds.x + bounds.width).toBeLessThanOrEqual(width - 24)
    expect(bounds.y + bounds.height).toBeLessThanOrEqual(height - 24)
    for (const key of ['Tab', 'Shift+Tab'])
      for (let index = 0; index < 30; index++) {
        await page.keyboard.press(key)
        expect(await dialog.evaluate((node) => node.contains(document.activeElement))).toBe(true)
      }
    const back = dialog.getByRole('button', { name: 'Back', exact: true })
    const initialBack = await back.boundingBox()
    await name.fill('Unfinished title')
    await dialog
      .getByLabel('About', { exact: true })
      .fill('A long description should wrap at the edge of the editor. '.repeat(50))
    for (const label of ['Name', 'Release year', 'Publisher', 'About', 'Cover art', 'Background art']) {
      const input = dialog.getByRole('textbox', { name: label, exact: true })
      await input.scrollIntoViewIfNeeded()
      const field = (await input.boundingBox())!,
        body = (await dialog.locator('.metadata-dialog-body').boundingBox())!
      expect(field.x).toBeGreaterThanOrEqual(body.x)
      expect(field.x + field.width).toBeLessThanOrEqual(body.x + body.width + 1)
      expect(field.y).toBeGreaterThanOrEqual(body.y - 1)
      expect(field.y + field.height).toBeLessThanOrEqual(body.y + body.height + 1)
      expect(field.height).toBeGreaterThanOrEqual(38)
      expect(await back.boundingBox()).toEqual(initialBack)
    }
    const year = dialog.getByLabel('Release year', { exact: true })
    await year.fill('2201')
    await dialog.getByRole('button', { name: 'Save release year', exact: true }).click()
    await expect(dialog.getByRole('alert')).toHaveText('Enter a release year between 1900 and 2200.')
    expect((await dialog.getByRole('alert').boundingBox())!.y).toBeGreaterThan((await year.boundingBox())!.y)
    const browse = dialog.getByRole('button', { name: 'Browse cover artwork', exact: true })
    await browse.click()
    await expect(page.locator('.artwork-browser-dialog')).toBeVisible()
    await page.keyboard.press('Escape')
    await expect(browse).toBeFocused()
    await expect(name).toHaveValue('Unfinished title')
    await dialog.locator('.metadata-dialog-body').evaluate((node) => {
      node.scrollTop = 0
    })
    await page.screenshot({ path: info.outputPath(`metadata-${width}.png`) })
    await back.click()
    await expect(dialog).toHaveCount(0)
    await expect(
      page.locator('.avalon-details').getByRole('button', { name: 'More', exact: true }),
    ).toBeFocused()
    await page.keyboard.press('Escape')
    expect(errors).toEqual([])
  })
}
test('fullscreen metadata keeps field order, keyboard editing, validation and Back restoration', async ({}, info) => {
  const dialog = await editor('fullscreen', 1280, 720)
  await page.evaluate(() => {
    const state = { pressed: [] as number[] }
    ;(window as unknown as { metadataPad: typeof state }).metadataPad = state
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, index) => ({ pressed: state.pressed.includes(index) })),
        },
      ],
    })
  })
  const buttons = (pressed: number[]) =>
    page.evaluate(async (pressed) => {
      ;(window as unknown as { metadataPad: { pressed: number[] } }).metadataPad.pressed = pressed
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
  const tap = async (index: number) => {
    await buttons([])
    await buttons([index])
    await buttons([])
  }
  const menu = dialog.locator('.metadata-field-menu > button')
  await expect(menu).toHaveCount(6)
  expect((await menu.allTextContents()).map((text) => text.split(' · ')[0])).toEqual([
    'Name',
    'Release year',
    'About',
    'Cover art',
    'Publisher',
    'Background art',
  ])
  const yearRow = menu.filter({ hasText: /^Release year · / })
  await yearRow.focus()
  await tap(0)
  const year = dialog.getByLabel('Release year', { exact: true }),
    original = await year.inputValue()
  await expect(dialog.getByRole('button', { name: 'Edit value', exact: true })).toBeFocused()
  await tap(3)
  await expect(page.getByRole('dialog', { name: 'Enter text', exact: true })).toBeVisible()
  await tap(1)
  await expect(year).toBeFocused()
  await year.fill('2201')
  await dialog.getByRole('button', { name: 'Save release year', exact: true }).click()
  await expect(dialog.getByRole('alert')).toHaveText('Enter a release year between 1900 and 2200.')
  await page.screenshot({ path: info.outputPath('fullscreen-metadata-validation.png') })
  await tap(1)
  await expect(yearRow).toBeFocused()
  await yearRow.click()
  await expect(year).toHaveValue(original)
  await expect(dialog.getByRole('alert')).toHaveCount(0)
  await page.keyboard.press('Escape')
  const cover = menu.filter({ hasText: /^Cover art · / })
  await cover.click()
  await expect(
    page.locator('.artwork-browser-dialog').getByRole('button', { name: 'Cover artwork', exact: true }),
  ).toBeFocused()
  await page.keyboard.press('Escape')
  await expect(cover).toBeFocused()
  await page.keyboard.press('Escape')
  await expect(dialog).toHaveCount(0)
  await expect(
    page.locator('.avalon-details').getByRole('button', { name: 'More', exact: true }),
  ).toBeFocused()
  await page.evaluate(() =>
    Object.defineProperty(navigator, 'getGamepads', { configurable: true, value: () => [] }),
  )
  expect(errors).toEqual([])
})
