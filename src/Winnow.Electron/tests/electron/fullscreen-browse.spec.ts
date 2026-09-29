import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-fullscreen-browse-'))
  const path = join(directory, 'fixture.json')
  const games = Array.from({ length: 60 }, (_, index) => ({
    workId: index + 1,
    title: `Game ${String(index + 1).padStart(2, '0')}`,
    bucket: ['never_played', 'stale_but_patched', 'derelict'][index % 3],
    playtimeMinutes: index * 60,
    entries: [
      {
        ownershipId: index + 1,
        releaseId: index + 1,
        workId: index + 1,
        title: 'Edition',
        store: 'Steam',
        installed: index % 2 === 0,
        playtimeMinutes: index * 60,
      },
    ],
  }))
  await writeFile(
    path,
    JSON.stringify({
      library: { games, lists: [] },
      feed: { candidateCount: 0, confidence: 0, failed: false, shelves: [] },
    }),
  )
  const environment = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/fullscreen-browse-main.mjs'),
      '--data-dir',
      directory,
      '--no-sync',
      '--seed-sample',
    ],
    env: { ...environment, WINNOW_LAYOUT_FIXTURE: path },
    chromiumSandbox: true,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.locator('.avalon-shell')).toBeVisible()
  await expect
    .poll(() =>
      page.evaluate(async () => (await window.winnow.request({ route: 'preferences.presentation.get' })).ok),
    )
    .toBe(true)
})
test.afterAll(async () => closeFixture(application, directory))
async function surface(mode: 'desktop' | 'fullscreen', width = 1920, height = 1080) {
  await application.evaluate(
    ({ BrowserWindow }, value) => {
      const window = BrowserWindow.getAllWindows()[0]
      window.setFullScreen(false)
      window.setContentSize(value.width, value.height)
      window.webContents.send('winnow:fullscreen:changed', value.mode === 'fullscreen')
      window.focus()
    },
    { mode, width, height },
  )
  await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
  await page.waitForFunction(({ width, height }) => innerWidth === width && innerHeight === height, {
    width,
    height,
  })
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
  await expect(page.locator('.avalon-library')).toBeVisible()
}
const grid = () => page.locator('.avalon-fullscreen-grid')
const active = () => grid().locator('[data-row-active="true"] [data-avalon-game]')
const collections = () => page.getByRole('group', { name: 'Library collections' })
async function frames() {
  await page.evaluate(
    () =>
      new Promise<void>((done) =>
        requestAnimationFrame(() => requestAnimationFrame(() => requestAnimationFrame(() => done()))),
      ),
  )
}
async function controller() {
  await page.evaluate(() => {
    const state = { pressed: [] as number[] }
    ;(window as unknown as { browsePad: typeof state }).browsePad = state
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
  await frames()
}
async function tap(button: number) {
  for (const pressed of [[], [button], []]) {
    await page.evaluate((pressed) => {
      ;(window as unknown as { browsePad: { pressed: number[] } }).browsePad.pressed = pressed
    }, pressed)
    await frames()
  }
}
test('fullscreen triggers cycle exactly four collections, retain cover positions and leave desktop cuts alone', async () => {
  await surface('desktop')
  await page.getByLabel('Search games', { exact: true }).fill('Game 60')
  await page.getByLabel('Sort', { exact: true }).selectOption('time')
  await surface('fullscreen')
  await expect(collections().getByRole('button')).toHaveText([
    'All games60',
    'Installed30',
    'Never played20',
    'Patched20',
  ])
  await active().first().focus()
  await page.keyboard.press('ArrowRight')
  await page.keyboard.press('ArrowDown')
  await page.keyboard.press('ArrowDown')
  const saved = await grid().getAttribute('data-selected-id')
  await expect(grid().locator('.avalon-row-viewport')).toHaveAttribute('data-first-row', '1')
  await controller()
  for (const label of ['Installed', 'Never played', 'Patched', 'All games']) {
    await tap(7)
    await expect(collections().getByRole('button', { name: label, exact: true })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
    await expect(active().first()).toBeVisible()
    await page.evaluate(() => {
      ;(window as unknown as { browseNodes: Element[] }).browseNodes = [
        document.querySelector('.avalon-fullscreen-grid .avalon-row-viewport')!,
        ...document.querySelectorAll('.avalon-fullscreen-grid [data-row-active="true"] [data-avalon-game]'),
      ]
    })
    await application.evaluate(({ BrowserWindow }) =>
      BrowserWindow.getAllWindows()[0].webContents.send('winnow:event', {
        kind: 'library.changed',
        resource: 'library',
      }),
    )
    await frames()
    expect(
      await page.evaluate(() =>
        (window as unknown as { browseNodes: Element[] }).browseNodes.every((node) => node.isConnected),
      ),
    ).toBe(true)
  }
  await expect(grid()).toHaveAttribute('data-selected-id', saved!)
  await expect(grid().locator(`[data-avalon-game="${saved}"]`)).toBeFocused()
  await expect(grid().locator('.avalon-row-viewport')).toHaveAttribute('data-first-row', '1')
  await tap(6)
  await expect(collections().getByRole('button', { name: 'Patched', exact: true })).toHaveAttribute(
    'aria-pressed',
    'true',
  )
  await surface('desktop')
  await expect(page.getByLabel('Search games', { exact: true })).toHaveValue('Game 60')
  await expect(page.getByLabel('Sort', { exact: true })).toHaveValue('time')
  expect(errors).toEqual([])
})

test('fullscreen Library keeps the selected game and uncropped portrait geometry through resize and return navigation', async ({}, info) => {
  await page.reload()
  await surface('fullscreen', 1728, 820)
  // This source test hosts Browse directly; allow its wall the full window width.
  const fit = await page.evaluate(() =>
    window.winnow.request({
      route: 'preferences.presentation.put',
      params: { preference: 'FullscreenFitUltrawide' },
      body: { value: 'true' },
    }),
  )
  expect(fit.ok).toBe(true)
  await expect(page.locator('html')).toHaveAttribute('data-fit-ultrawide', 'true')
  await expect(active().first()).toBeVisible()
  await frames()
  await active().first().focus()
  await page.keyboard.press('ArrowRight')
  const columns = await grid()
    .locator('[data-row-active="true"]')
    .first()
    .locator('[data-avalon-game]')
    .count()
  expect(columns).toBeGreaterThan(6)
  await page.keyboard.press('ArrowDown')
  await page.keyboard.press('ArrowDown')
  const selected = await grid().getAttribute('data-selected-id')
  expect(selected).toBe(String(columns * 2 + 2))
  await page.keyboard.press('ArrowUp')
  await expect(grid()).toHaveAttribute('data-selected-id', String(columns + 2))
  await expect(grid().locator('.avalon-row-viewport')).toHaveAttribute('data-first-row', '1')
  const retained = String(columns + 2)
  for (const width of [2500, 1728]) {
    await application.evaluate(
      ({ BrowserWindow }, width) => BrowserWindow.getAllWindows()[0].setContentSize(width, 820),
      width,
    )
    await page.waitForFunction((width) => innerWidth === width, width)
    const resizedColumns = () =>
      grid().locator('[data-row-active="true"]').first().locator('[data-avalon-game]').count()
    if (width === 2500) await expect.poll(resizedColumns).toBeGreaterThan(columns)
    else await expect.poll(resizedColumns).toBe(columns)
    await expect(grid()).toHaveAttribute('data-selected-id', retained)
    await expect(grid().locator(`[data-avalon-game="${retained}"]`)).toBeFocused()
    await expect
      .poll(async () =>
        active().evaluateAll((covers) =>
          covers.every((cover) => {
            const image = cover.querySelector('img')
            return (
              image?.naturalWidth === 600 &&
              image.naturalHeight === 900 &&
              getComputedStyle(image).objectFit === 'contain'
            )
          }),
        ),
      )
      .toBe(true)
    const art = await active()
      .locator(':scope > .artwork')
      .evaluateAll((elements) =>
        elements.map((element) => {
          const bounds = element.getBoundingClientRect()
          return { width: bounds.width, height: bounds.height, bottom: bounds.bottom }
        }),
      )
    expect(art.length).toBeGreaterThan(0)
    for (const bounds of art) {
      expect(Math.abs(bounds.width - (bounds.height * 2) / 3)).toBeLessThanOrEqual(1)
      expect(bounds.bottom).toBeLessThanOrEqual(820)
    }
    await page.screenshot({ path: info.outputPath(`browse-${width}.png`) })
  }
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'For you', exact: true })
    .click()
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
  await expect(grid()).toHaveAttribute('data-selected-id', retained)
  await expect(grid().locator(`[data-avalon-game="${retained}"]`)).toBeFocused()
  expect(errors).toEqual([])
})

test('fullscreen filter drafts apply with Y from the top, cancel with B and keep Apply visible at large text', async ({}, info) => {
  await page.reload()
  await surface('fullscreen', 1280, 720)
  await page.evaluate(() => document.documentElement.style.setProperty('--fullscreen-text-scale', '1.4'))
  await controller()
  await page.getByRole('button', { name: 'Filters', exact: true }).click()
  const panel = page.getByRole('dialog', { name: 'Library filters' })
  await panel.getByRole('combobox', { name: 'Sort', exact: true }).selectOption('title-desc')
  await panel.getByRole('combobox', { name: 'Collection', exact: true }).selectOption('never_played')
  await panel.getByRole('combobox', { name: 'Installation', exact: true }).selectOption('true')
  await expect(page.locator('.avalon-toolbar select[aria-label="Sort"]')).toHaveValue('dormant')
  const apply = panel.getByRole('button', { name: 'Apply filters' })
  const geometry = await apply.evaluate((element) => {
    const bounds = element.getBoundingClientRect()
    return {
      top: bounds.top,
      bottom: bounds.bottom,
      scrolls: Boolean(element.closest('.avalon-filter-fields')),
    }
  })
  expect(geometry.top).toBeGreaterThan(0)
  expect(geometry.bottom).toBeLessThanOrEqual(720)
  expect(geometry.scrolls).toBe(false)
  const panelWidth = (await panel.boundingBox())!.width
  expect(panelWidth).toBeGreaterThan(1000)
  await page.screenshot({ path: info.outputPath('fullscreen-filter-draft-large-text.png') })
  await panel.getByRole('button', { name: 'Cancel' }).focus()
  await tap(3)
  await expect(panel).toHaveCount(0)
  await expect(page.getByLabel('Sort', { exact: true })).toHaveValue('title-desc')
  await expect(page.locator('.avalon-results-count')).toHaveText('10 games')
  await page.getByRole('button', { name: 'Filters', exact: true }).click()
  await panel.getByRole('combobox', { name: 'Sort', exact: true }).selectOption('dormant')
  await panel.getByRole('button', { name: 'Cancel' }).focus()
  await tap(1)
  await expect(panel).toHaveCount(0)
  await expect(page.getByLabel('Sort', { exact: true })).toHaveValue('title-desc')
  expect(errors).toEqual([])
})

test('ultrawide fit and large text reflow both fullscreen browse grids without losing focus or portrait fitting', async ({}, info) => {
  await page.reload()
  await surface('fullscreen', 2560, 1080)
  const preference = async (preference: string, value: string) => {
    const response = await page.evaluate(
      async ({ preference, value }) =>
        window.winnow.request({
          route: 'preferences.presentation.put',
          params: { preference },
          body: { value },
        }),
      { preference, value },
    )
    expect(response.ok).toBe(true)
  }
  await preference('FullscreenFitUltrawide', 'false')
  await expect(page.locator('html')).toHaveAttribute('data-fit-ultrawide', 'false')
  await frames()
  const columns = () =>
    grid().locator('[data-row-active="true"]').first().locator('[data-avalon-game]').count()
  const initialColumns = await columns()
  await active().first().focus()
  await page.keyboard.press('ArrowRight')
  const selected = await grid().getAttribute('data-selected-id')
  await preference('FullscreenFitUltrawide', 'true')
  await expect(page.locator('html')).toHaveAttribute('data-fit-ultrawide', 'true')
  await expect.poll(columns).toBeGreaterThan(initialColumns)
  await expect(grid().locator(`[data-avalon-game="${selected}"]`)).toBeFocused()
  await preference('FullscreenTextScale', '1.4')
  await expect
    .poll(() =>
      page.evaluate(() => document.documentElement.style.getPropertyValue('--fullscreen-text-scale')),
    )
    .toBe('1.4')
  await frames()
  await expect(grid().locator(`[data-avalon-game="${selected}"]`)).toBeFocused()
  await page.screenshot({ path: info.outputPath('browse-ultrawide-large-text.png') })
  await page.getByRole('button', { name: 'Search library', exact: true }).click()
  await page.getByRole('button', { name: 'Go to results' }).click()
  const searchSelected = await grid().getAttribute('data-selected-id')
  const wideColumns = await columns()
  await preference('FullscreenFitUltrawide', 'false')
  await expect(page.locator('html')).toHaveAttribute('data-fit-ultrawide', 'false')
  await expect.poll(columns).toBeLessThan(wideColumns)
  await expect(grid().locator(`[data-avalon-game="${searchSelected}"]`)).toBeFocused()
  await expect
    .poll(async () =>
      active().evaluateAll((covers) =>
        covers.every((cover) => {
          const image = cover.querySelector('img')
          return image?.naturalWidth === 600 && getComputedStyle(image).objectFit === 'contain'
        }),
      ),
    )
    .toBe(true)
  await page.screenshot({ path: info.outputPath('search-ultrawide-large-text.png') })
  expect(errors).toEqual([])
})
