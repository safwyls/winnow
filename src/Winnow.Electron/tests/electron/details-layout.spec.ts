import { closeFixture } from './fixture-cleanup'
import { fillLibrarySearch } from './library-controls'
import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import type { LibraryResponse } from '../../src/renderer/api/types'

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
for (const [width, height] of [
  [1200, 640],
  [2560, 1440],
])
  test(`desktop Details at ${width}×${height} retains the header, bounded reading and modal origin`, async ({}, info) => {
    await surface('desktop', width, height)
    const origin = page.locator('.avalon-cover').nth(6)
    await origin.scrollIntoViewIfNeeded()
    const position = await page.locator('#main-content').evaluate((node) => node.scrollTop)
    await origin.click()
    const details = page.locator('.avalon-details.desktop')
    await expect(details).toBeVisible()
    await expect(details.getByRole('tab')).toHaveCount(5)
    await expect(details.locator('[data-controller-play],.avalon-details-close').first()).toBeFocused()
    const geometry = await details.evaluate((node) => {
      const bounds = node.getBoundingClientRect(),
        cover = node.querySelector('.avalon-detail-cover')!.getBoundingClientRect(),
        header = node.querySelector('.avalon-details-header')!.getBoundingClientRect(),
        reading = node.querySelector('.avalon-details-reading')!.getBoundingClientRect()
      return {
        width: bounds.width,
        left: bounds.left,
        top: bounds.top,
        right: bounds.right,
        bottom: bounds.bottom,
        coverWidth: cover.width,
        coverHeight: cover.height,
        headerTop: header.top,
        headerHeight: header.height,
        readingHeight: reading.height,
      }
    })
    expect(geometry.left).toBeGreaterThanOrEqual(39)
    expect(geometry.width).toBe(Math.min(width - 80, Math.max(860, Math.min(1582, width / 2))))
    expect(geometry.top).toBeGreaterThanOrEqual(39)
    expect(geometry.right).toBeLessThanOrEqual(width - 39)
    expect(geometry.bottom).toBeLessThanOrEqual(height - 39)
    expect(geometry.coverWidth).toBe(82)
    expect(geometry.coverHeight).toBe(123)
    expect(geometry.readingHeight).toBeGreaterThan(150)
    await details.getByRole('button', { name: 'Read more', exact: true }).click()
    const reading = details.locator('.avalon-details-reading')
    await reading.evaluate((node) => {
      node.scrollTop = 200
    })
    await expect.poll(() => reading.evaluate((node) => node.scrollTop)).toBe(200)
    await details.getByRole('tab', { name: 'Library', exact: true }).click()
    const header = await details.locator('.avalon-details-header').boundingBox()
    expect(header!.y).toBe(geometry.headerTop)
    expect(header!.height).toBe(geometry.headerHeight)
    await details.getByRole('tab', { name: 'Overview', exact: true }).click()
    await expect.poll(() => reading.evaluate((node) => node.scrollTop)).toBe(200)
    await details.getByRole('button', { name: 'More', exact: true }).click()
    await details.getByRole('button', { name: 'Edit metadata…', exact: true }).click()
    await expect(page.locator('.metadata-dialog').getByLabel('Name', { exact: true })).toBeFocused()
    await page.keyboard.press('Escape')
    await expect(details.getByRole('tab', { name: 'Overview', exact: true })).toHaveAttribute(
      'aria-selected',
      'true',
    )
    await expect(details.getByRole('button', { name: 'More', exact: true })).toBeFocused()
    await page.screenshot({ path: info.outputPath('desktop-details.png') })
    await page.keyboard.press('Escape')
    await expect(details).toHaveCount(0)
    await expect(origin).toBeFocused()
    await expect.poll(() => page.locator('#main-content').evaluate((node) => node.scrollTop)).toBe(position)
  })
for (const [width, height, scale, uiScale] of [
  [1280, 720, 1, 1],
  [1280, 720, 1.4, 1],
  [1280, 720, 1.4, 1.2],
  [2560, 1440, 1, 1],
])
  test(`fullscreen Details at ${width}×${height} and text ${scale}, interface ${uiScale} keeps cinematic identity and reading reachable`, async ({}, info) => {
    await surface('fullscreen', width, height)
    await page.evaluate(
      async ([scale, uiScale]) => {
        for (const [preference, value] of [
          ['FullscreenTextScale', scale],
          ['FullscreenInterfaceScale', uiScale],
        ]) {
          const response = await window.winnow.request({
            route: 'preferences.presentation.put',
            params: { preference },
            body: { value: String(value) },
          })
          if (!response.ok) throw Error(response.message)
        }
      },
      [scale, uiScale],
    )
    await expect(page.locator('html')).toHaveCSS('--fullscreen-text-scale', String(scale))
    const origin = page.locator('.avalon-cover').first()
    await origin.click()
    const details = page.locator('.avalon-details.fullscreen')
    await expect(details).toBeVisible()
    await expect(details.getByRole('tab')).toHaveCount(4)
    await expect(page.getByRole('button', { name: 'B · Back to Library', exact: true })).toBeVisible()
    const metrics = await details.evaluate((node) => {
      const title = node.querySelector('h1')!,
        style = getComputedStyle(title),
        reading = node.querySelector('.avalon-details-reading')!,
        hero = node.querySelector('.avalon-details-header')!,
        overview = node.querySelector('.avalon-details-overview')!,
        left = node.querySelector('.avalon-personal-history')!
      return {
        titleHeight: title.getBoundingClientRect().height,
        lineHeight: parseFloat(style.lineHeight),
        family: style.fontFamily,
        bodyFamily: getComputedStyle(document.documentElement).getPropertyValue('--font-body').trim(),
        readingHeight: reading.getBoundingClientRect().height,
        headerHeight: hero.getBoundingClientRect().height,
        actionsHeight: node.querySelector('.avalon-details-actions')!.getBoundingClientRect().height,
        rootHeight: node.getBoundingClientRect().height,
        zoom: getComputedStyle(document.body).zoom,
        htmlScale: getComputedStyle(document.documentElement).getPropertyValue(
          '--fullscreen-interface-scale',
        ),
        heroBottom: hero.getBoundingClientRect().bottom,
        height: innerHeight,
        width: innerWidth,
        personalRatio:
          left.getBoundingClientRect().width /
          (overview.getBoundingClientRect().width -
            parseFloat(getComputedStyle(overview).gap) * parseFloat(getComputedStyle(document.body).zoom)),
      }
    })
    expect(metrics.titleHeight).toBeLessThanOrEqual(metrics.lineHeight * 2 * uiScale + 1)
    expect(metrics.family.replace(/["']/g, '')).toBe(metrics.bodyFamily.replace(/["']/g, ''))
    expect(metrics.readingHeight, JSON.stringify(metrics)).toBeGreaterThan(100)
    expect(metrics.heroBottom).toBeLessThan(metrics.height - 100)
    expect(metrics.personalRatio).toBeCloseTo(0.28, 2)
    await expect(details.getByRole('button', { name: /^Open screenshot / })).toHaveCount(2)
    await expect(details.getByRole('button', { name: 'View gallery →', exact: true })).toBeVisible()
    const more = details.getByRole('button', { name: 'Read more →', exact: true })
    await more.click()
    await expect(details.getByRole('region', { name: 'About', exact: true })).toBeVisible()
    await expect(details.getByRole('tab')).toHaveCount(0)
    await page.keyboard.press('Escape')
    await expect(more).toBeFocused()
    await details.getByRole('tab', { name: 'Library', exact: true }).click()
    await expect(details.getByRole('heading', { name: 'Owned copies', exact: true })).toBeVisible()
    await page.screenshot({ path: info.outputPath('fullscreen-details.png') })
    await page.getByRole('button', { name: 'B · Back to Library', exact: true }).click()
    await expect(origin).toBeFocused()
  })
for (const mode of ['desktop', 'fullscreen'] as const)
  test(`${mode} Overview renders test-owned hero and complete screenshot previews through production artwork IPC`, async ({}, info) => {
    await application.evaluate(() => {
      ;(globalThis as unknown as { __detailsHero: boolean }).__detailsHero = true
    })
    await page.reload()
    await expect(page.locator('.avalon-cover').first()).toBeVisible()
    await surface(mode, 1920, 1080)
    const title = await page.evaluate(async () => {
      const library = await window.winnow.request<LibraryResponse>({ route: 'library.get' })
      if (!library.ok) throw Error(library.message)
      return library.data!.games.find((game) => game.playtimeMinutes > 0 && game.lastPlayedAt)?.title
    })
    expect(title).toBeTruthy()
    await fillLibrarySearch(page, title!)
    await page.locator('.avalon-cover').first().click()
    const details = page.locator('.avalon-details')
    await expect(details.locator('.avalon-detail-backdrop[data-state="ready"] img')).toBeVisible()
    await expect(details.getByRole('tab', { name: 'Overview', exact: true })).toHaveAttribute(
      'aria-selected',
      'true',
    )
    const images = details.locator('.screenshot-strip img')
    await expect(images).toHaveCount(mode === 'fullscreen' ? 2 : 10)
    for (const image of await images.all())
      await expect.poll(() => image.evaluate((node) => (node as HTMLImageElement).naturalWidth)).toBe(1280)
    await page.screenshot({ path: info.outputPath(`${mode}-overview-art.png`) })
    if (mode === 'fullscreen')
      for (const image of await images.all()) {
        const size = await image.boundingBox()
        expect(size!.width / size!.height).toBeCloseTo(16 / 9, 2)
        await expect(image).toHaveCSS('object-fit', 'contain')
      }
    const hero = await details.locator('.avalon-detail-backdrop').boundingBox()
    expect(hero!.width).toBeGreaterThan(700)
    await page
      .getByRole('button', {
        name: mode === 'desktop' ? 'Close game details' : 'B · Back to Library',
        exact: true,
      })
      .click()
  })
test('Details emits no uncaught renderer errors', () => expect(errors).toEqual([]))
