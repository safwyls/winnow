import { closeFixture } from './fixture-cleanup'
import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Page,
  type Locator,
} from '@playwright/test'
import { mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'

let app: ElectronApplication, page: Page, directory: string
const errors: string[] = []
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-artwork-browser-'))
  const env = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/artwork-browser-main.mjs'),
      '--data-dir',
      directory,
      '--seed-sample',
      '--no-sync',
    ],
    env,
    chromiumSandbox: true,
    timeout: 60000,
  })
  page = await app.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
})
test.afterAll(async () => closeFixture(app, directory))
const dialog = () => page.locator('.artwork-browser-dialog')
const detail = () => page.locator('.avalon-details')
const candidate = (slot = 'Hero', index = 0) =>
  dialog().locator(`[data-artwork-candidate="steam:${slot}_${index}"]`).last()
const writes = () =>
  app.evaluate(
    () => (globalThis as unknown as { __artworkBrowser: { writes: unknown[] } }).__artworkBrowser.writes,
  )
async function open(mode: 'desktop' | 'fullscreen', width = 1280, height = 820, scale = 1) {
  await app.evaluate(() =>
    (globalThis as unknown as { __artworkBrowser: { reset(): void } }).__artworkBrowser.reset(),
  )
  await page.reload()
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
  await app.evaluate(
    ({ BrowserWindow }, value) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setFullScreen(false)
      window.setContentSize(value.width, value.height)
      window.webContents.send('winnow:fullscreen:changed', value.mode === 'fullscreen')
    },
    { mode, width, height },
  )
  await expect.poll(() => page.evaluate(() => [innerWidth, innerHeight])).toEqual([width, height])
  await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
  await page.evaluate(async (scale) => {
    const value = await window.winnow.request({
      route: 'preferences.presentation.put',
      params: { preference: 'FullscreenTextScale' },
      body: { value: String(scale) },
    })
    if (!value.ok) throw Error(value.message)
  }, scale)
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
  await page.locator('.avalon-cover').first().click()
  await detail().getByRole('button', { name: 'More', exact: true }).click()
  const actions =
    mode === 'fullscreen' ? page.getByRole('dialog', { name: 'More game actions', exact: true }) : detail()
  await actions.getByRole('button', { name: 'Artwork…', exact: true }).click()
  await expect(dialog()).toBeVisible()
  await expect(dialog().getByRole('button', { name: 'Hero artwork', exact: true })).toBeFocused()
  await expect(candidate()).toBeVisible()
}
async function inside(target: Locator, container: Locator) {
  const box = await target.boundingBox(),
    outer = await container.boundingBox()
  expect(box).not.toBeNull()
  expect(outer).not.toBeNull()
  expect(box!.width).toBeGreaterThan(0)
  expect(box!.height).toBeGreaterThan(0)
  expect(box!.x).toBeGreaterThanOrEqual(outer!.x - 1)
  expect(box!.y).toBeGreaterThanOrEqual(outer!.y - 1)
  expect(box!.x + box!.width).toBeLessThanOrEqual(outer!.x + outer!.width + 1)
  expect(box!.y + box!.height).toBeLessThanOrEqual(outer!.y + outer!.height + 1)
}
for (const [width, height] of [
  [1280, 820],
  [1200, 640],
  [1920, 1080],
])
  test(`desktop artwork at ${width}×${height} keeps preview and actions fixed while only the gallery scrolls`, async ({}, info) => {
    await open('desktop', width, height)
    expect((await dialog().boundingBox())!.width).toBe(Math.min(width - 48, 1440))
    expect(await detail().getAttribute('aria-hidden')).toBe('true')
    const preview = dialog().getByRole('region', { name: 'Artwork preview' }),
      gallery = dialog().locator('.artwork-browser-gallery'),
      apply = dialog().getByRole('button', { name: 'Use artwork', exact: true })
    for (const slot of ['Hero', 'Cover', 'Icon']) {
      await dialog()
        .getByRole('button', { name: `${slot} artwork`, exact: true })
        .click()
      const count = (await writes()).length
      await candidate(slot, 1).click()
      await expect(apply).toBeEnabled()
      expect((await writes()).length).toBe(count)
      await apply.click()
      await expect(apply).toBeDisabled()
      await expect(dialog().getByRole('button', { name: 'Use automatic', exact: true })).toBeEnabled()
      expect((await writes()).slice(count).map((value) => (value as { slot: string }).slot)).toEqual([slot])
      for (const target of [
        preview,
        apply,
        dialog().getByRole('button', { name: 'Choose file', exact: true }),
        dialog().getByRole('button', { name: 'Import URL', exact: true }),
      ])
        await inside(target, dialog())
      for (const image of await preview.locator('img').all()) {
        await expect.poll(() => image.evaluate((node) => (node as HTMLImageElement).naturalWidth)).toBe(800)
        await inside(image, preview)
      }
      if (slot === 'Hero') {
        await dialog().getByRole('button', { name: 'Fullscreen crop · 16:9', exact: true }).click()
        await inside(preview.locator('img'), preview)
        const crop = await preview.locator('.artwork-preview-frame').boundingBox()
        expect(crop!.width / crop!.height).toBeCloseTo(16 / 9, 2)
      } else if (slot === 'Cover') {
        const crop = await preview.locator('.artwork-preview-frame').boundingBox()
        expect(crop!.width / crop!.height).toBeCloseTo(2 / 3, 2)
      }
      const before = [await preview.boundingBox(), await apply.boundingBox()]
      await gallery.evaluate((node) => {
        node.scrollTop = 500
      })
      await expect.poll(() => gallery.evaluate((node) => node.scrollTop)).toBeGreaterThan(0)
      expect([await preview.boundingBox(), await apply.boundingBox()]).toEqual(before)
      await page.screenshot({ path: info.outputPath(`${width}-${slot}.png`) })
    }
    await page.keyboard.press('Escape')
    await expect(dialog()).toHaveCount(0)
    await expect(detail().getByRole('button', { name: 'More', exact: true })).toBeFocused()
    expect(errors).toEqual([])
  })
test('desktop artwork traps both keyboard directions and Back restores More without writing', async () => {
  await app.evaluate(() => {
    ;(globalThis as unknown as { __artworkBrowser: { count: number } }).__artworkBrowser.count = 1
  })
  await open('desktop')
  await candidate().click()
  const count = (await writes()).length
  for (const key of ['Tab', 'Shift+Tab']) {
    await dialog().getByRole('button', { name: 'Back', exact: true }).focus()
    const visited = new Set<string>()
    let returned = false
    for (let index = 0; index < 60; index++) {
      await page.keyboard.press(key)
      const focused = await page.evaluate(() => ({
        inside: Boolean(document.activeElement?.closest('.artwork-browser-dialog')),
        name: document.activeElement?.getAttribute('aria-label') ?? document.activeElement?.textContent,
      }))
      expect(focused.inside).toBe(true)
      visited.add(focused.name ?? '')
      if (focused.name === 'Back') {
        returned = true
        break
      }
    }
    expect(returned).toBe(true)
    expect(visited.size).toBeGreaterThanOrEqual(8)
  }
  await dialog().getByRole('button', { name: 'Back', exact: true }).press('Enter')
  await expect(dialog()).toHaveCount(0)
  await expect(detail().getByRole('button', { name: 'More', exact: true })).toBeFocused()
  expect((await writes()).length).toBe(count)
})
for (const [reducedMotion, scale] of [
  [false, 1],
  [true, 1.4],
] as const)
  test(`fullscreen artwork preserves controller selection focus during paging with reduced motion ${reducedMotion} and text ${scale}`, async ({}, info) => {
    await app.evaluate(() => {
      ;(globalThis as unknown as { __artworkBrowser: { count: number } }).__artworkBrowser.count = 1
    })
    await open('fullscreen', 1920, 1080, scale)
    await page.emulateMedia({ reducedMotion: reducedMotion ? 'reduce' : 'no-preference' })
    await page.evaluate(() => {
      const state = { pressed: [] as number[] }
      ;(window as unknown as { artworkPad: typeof state }).artworkPad = state
      Object.defineProperty(navigator, 'getGamepads', {
        configurable: true,
        value: () => [
          {
            index: 0,
            connected: true,
            mapping: 'standard',
            axes: [0, 0, 0, 0],
            buttons: Array.from({ length: 17 }, (_, index) => ({ pressed: state.pressed.includes(index) })),
          },
        ],
      })
    })
    const buttons = (pressed: number[]) =>
      page.evaluate(async (pressed) => {
        ;(window as unknown as { artworkPad: { pressed: number[] } }).artworkPad.pressed = pressed
        await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
      }, pressed)
    const tap = async (index: number) => {
      await buttons([])
      await buttons([index])
      await buttons([])
    }
    for (const [button, order] of [
      [7, ['Cover', 'Icon', 'Hero']],
      [6, ['Icon', 'Cover', 'Hero']],
    ] as const)
      for (const slot of order) {
        await tap(button)
        await expect(dialog().getByRole('button', { name: `${slot} artwork`, exact: true })).toBeFocused()
      }
    await candidate().focus()
    await tap(0)
    await expect(candidate()).toHaveAttribute('aria-pressed', 'true')
    // Load the next provider page without stealing the selected candidate's focus.
    await dialog()
      .getByRole('button', { name: 'Load more Steam', exact: true })
      .evaluate((node) => (node as HTMLButtonElement).click())
    await expect(candidate('Hero', 1)).toHaveCount(1)
    await expect(candidate()).toBeFocused()
    const apply = dialog().getByRole('button', { name: 'Use artwork', exact: true })
    await inside(apply, dialog())
    await apply.focus()
    await tap(0)
    await expect(dialog().getByText('Artwork saved.', { exact: true })).toBeVisible()
    await expect(dialog().getByRole('button', { name: 'Use automatic', exact: true })).toBeEnabled()
    await page.screenshot({ path: info.outputPath(`fullscreen-${scale}.png`) })
    await tap(1)
    await expect(dialog()).toHaveCount(0)
    await expect(detail().getByRole('button', { name: 'More', exact: true })).toBeFocused()
    await page.evaluate(() =>
      Object.defineProperty(navigator, 'getGamepads', { configurable: true, value: () => [] }),
    )
    expect(errors).toEqual([])
  })
