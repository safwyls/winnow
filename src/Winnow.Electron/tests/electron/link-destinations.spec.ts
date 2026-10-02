import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-link-destinations-'))
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/link-destinations-main.mjs'),
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
    timeout: 60000,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
})
test.afterAll(async () => closeFixture(application, directory))
test.afterEach(async ({}, info) => {
  if (info.status === info.expectedStatus) return
  await info.attach('renderer-errors', { body: JSON.stringify(errors), contentType: 'application/json' })
  await page.screenshot({ path: info.outputPath('failure.png') })
})
async function surface(mode: 'desktop' | 'fullscreen') {
  await application.evaluate(({ BrowserWindow }, fullscreen) => {
    const window = BrowserWindow.getAllWindows().find((window) =>
      window.webContents.getURL().startsWith('winnow-app:'),
    )!
    window.setFullScreen(fullscreen)
    if (!fullscreen) window.setContentSize(1280, 900)
    window.focus()
  }, mode === 'fullscreen')
  await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
}
async function setDestination(value: string) {
  await page.evaluate(async (value) => {
    const result = await window.winnow.request({
      route: 'preferences.presentation.put',
      params: { preference: 'LinkDestination' },
      body: { value },
    })
    if (!result.ok) throw Error(result.message)
  }, value)
}
async function openDetails() {
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
  await page.locator('.avalon-cover').first().click()
  await expect(page.locator('.avalon-details')).toBeVisible()
  await page.locator('.avalon-details').getByRole('button', { name: 'More', exact: true }).click()
}
async function resetObservation(failReader = false) {
  await application.evaluate(({}, failReader) => {
    const state = (globalThis as any).linkDestinationsFixture
    state.external = []
    state.reading = []
    state.failReader = failReader
  }, failReader)
}
async function closeReaders() {
  await application.evaluate(({ BrowserWindow }) => {
    for (const window of BrowserWindow.getAllWindows())
      if (!window.webContents.getURL().startsWith('winnow-app:')) window.close()
  })
}
async function closeDetails(mode: 'desktop' | 'fullscreen') {
  await page.keyboard.press('Escape')
  await page
    .getByRole('button', {
      name: mode === 'desktop' ? 'Close game details' : 'B · Back to Library',
      exact: true,
    })
    .click()
}
async function assertReading(address: string) {
  await expect
    .poll(() =>
      application
        .context()
        .pages()
        .some((value) => value.url() === address),
    )
    .toBe(true)
  const content = application
    .context()
    .pages()
    .find((value) => value.url() === address)!
  await expect(content.getByRole('heading', { name: 'Reference fixture' })).toBeVisible()
  expect(await content.evaluate(() => typeof window.winnow)).toBe('undefined')
  expect(await application.evaluate(() => (globalThis as any).linkDestinationsFixture.external)).toEqual([])
  await closeReaders()
}
for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} Library Details opens all original reference targets through the saved in-app destination`, async () => {
    await surface(mode)
    await setDestination('in-app')
    await openDetails()
    for (const [name, address] of [
      ['IGDB', 'https://www.igdb.com/g/1hy'],
      ['SteamDB', 'https://steamdb.info/app/440/'],
      ['SteamGridDB', 'https://www.steamgriddb.com/steam/440'],
    ]) {
      await resetObservation()
      const button = page.getByRole('button', { name, exact: true })
      await button.click()
      await assertReading(address)
      await expect(button).toBeFocused()
    }
    expect(await application.evaluate(() => (globalThis as any).linkDestinationsFixture.launches)).toBe(0)
    await closeDetails(mode)
  })
  test(`${mode} Details applies saved browser and store preferences and reports general-page fallback`, async () => {
    await surface(mode)
    await openDetails()
    for (const [preference, name, address, fallback] of [
      ['in-app', 'Store page', 'https://store.steampowered.com/app/440/', false],
      ['browser', 'IGDB', 'https://www.igdb.com/g/1hy', false],
      ['store', 'Store page', 'steam://store/440', false],
      ['store', 'IGDB', 'https://www.igdb.com/g/1hy', true],
    ] as const) {
      await setDestination(preference)
      await resetObservation()
      const button = page.getByRole('button', { name, exact: true })
      await button.click()
      if (preference === 'in-app') await assertReading(address)
      else {
        await expect
          .poll(() => application.evaluate(() => (globalThis as any).linkDestinationsFixture.external))
          .toEqual([address])
        expect(await application.evaluate(() => (globalThis as any).linkDestinationsFixture.reading)).toEqual(
          [],
        )
      }
      if (fallback) {
        await expect(page.getByRole('status')).toContainText('Opened in your browser')
        await expect(button).toBeFocused()
        await page.getByRole('button', { name: 'Dismiss link status' }).click()
      }
    }
    expect(await application.evaluate(() => (globalThis as any).linkDestinationsFixture.launches)).toBe(0)
    await closeDetails(mode)
  })
  test(`${mode} Details reports a failed embedded reader as browser fallback without losing its origin`, async ({}, info) => {
    await surface(mode)
    await setDestination('in-app')
    await openDetails()
    await resetObservation(true)
    const button = page.getByRole('button', { name: 'IGDB', exact: true })
    await button.click()
    await expect
      .poll(() => application.evaluate(() => (globalThis as any).linkDestinationsFixture.external))
      .toEqual(['https://www.igdb.com/g/1hy'])
    await expect(page.getByRole('status')).toContainText(
      'This page cannot open in Winnow. Opened in your browser.',
    )
    await expect(button).toBeFocused()
    await resetObservation()
    await page.screenshot({ path: info.outputPath(`${mode}-fallback-notice.png`) })
    await page.getByRole('button', { name: 'Dismiss link status' }).focus()
    await page.keyboard.press('Escape')
    await expect(page.getByRole('complementary', { name: 'Link status' })).toHaveCount(0)
    await expect(button).toBeFocused()
    await expect(page.locator('.avalon-details')).toBeVisible()
    await closeDetails(mode)
  })
}
test('desktop and controller link settings share confirmed values and persist through a renderer reload', async () => {
  await surface('desktop')
  await setDestination('in-app')
  await page.getByRole('button', { name: 'Settings', exact: true }).click()
  await page
    .getByRole('navigation', { name: 'Settings section' })
    .getByRole('button', { name: 'Application', exact: true })
    .click()
  await page.getByRole('combobox', { name: 'Open links in', exact: true }).selectOption('browser')
  await surface('fullscreen')
  await page.getByRole('button', { name: 'Settings', exact: true }).click()
  await page
    .getByRole('navigation', { name: 'Settings section' })
    .getByRole('button', { name: 'Application', exact: true })
    .click()
  const row = page.getByRole('button', { name: 'Open links in', exact: true })
  await expect(row).toContainText('Default browser')
  await row.focus()
  await page.evaluate(() => {
    const pad = { pressed: false }
    Object.assign(window, { linkDestinationPad: pad })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          connected: true,
          mapping: 'standard',
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, index) => ({ pressed: index === 0 && pad.pressed })),
        },
      ],
    })
  })
  for (const pressed of [false, true, false])
    await page.evaluate(async (pressed) => {
      ;(window as any).linkDestinationPad.pressed = pressed
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
  await expect(row).toContainText('Steam client, when available')
  await expect(row).toBeFocused()
  await surface('desktop')
  await page.getByRole('button', { name: 'Settings', exact: true }).click()
  await page
    .getByRole('navigation', { name: 'Settings section' })
    .getByRole('button', { name: 'Application', exact: true })
    .click()
  await expect(page.getByRole('combobox', { name: 'Open links in', exact: true })).toHaveValue('store')
  await page.reload()
  await expect(page.getByRole('button', { name: 'Settings', exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Settings', exact: true }).click()
  await page
    .getByRole('navigation', { name: 'Settings section' })
    .getByRole('button', { name: 'Application', exact: true })
    .click()
  await expect(page.getByRole('combobox', { name: 'Open links in', exact: true })).toHaveValue('store')
})
test('link destination composition neither launches games nor reports renderer or network errors', async () => {
  expect(errors).toEqual([])
  expect(await application.evaluate(() => (globalThis as any).linkDestinationsFixture.launches)).toBe(0)
  expect(await application.evaluate(() => (globalThis as any).linkDestinationsFixture.forbidden)).toEqual([])
})
