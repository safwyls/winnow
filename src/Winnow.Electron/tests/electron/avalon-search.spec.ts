import { closeFixture } from './fixture-cleanup'
import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'

let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-search-'))
  const fixturePath = join(directory, 'search-fixture.json')
  const games = Array.from({ length: 90 }, (_, index) => ({
    workId: index + 1,
    title: `Search game ${String(index + 1).padStart(3, '0')}`,
    bucket: 'never_played',
    playtimeMinutes: 0,
    entries: [
      {
        ownershipId: index + 1,
        releaseId: index + 1,
        workId: index + 1,
        title: `Search game ${index + 1}`,
        store: 'Steam',
        installed: true,
        playtimeMinutes: 0,
      },
    ],
  }))
  await writeFile(
    fixturePath,
    JSON.stringify({
      library: { games, lists: [] },
      feed: {
        candidateCount: games.length,
        confidence: 2,
        failed: false,
        shelves: [
          {
            id: 'search-fixture',
            title: 'Ready to play',
            blurb: '',
            supportsFeedback: false,
            reserve: [],
            items: games.slice(0, 10).map((game) => ({
              ownershipId: game.workId,
              releaseId: game.workId,
              title: game.title,
              reason: 'Search presentation fixture.',
            })),
          },
        ],
      },
    }),
  )
  const environment = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('tests/electron/layout-main.mjs'), '--data-dir', directory, '--seed-sample', '--no-sync'],
    env: { ...environment, WINNOW_LAYOUT_FIXTURE: fixturePath },
    chromiumSandbox: true,
    timeout: 60_000,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
})
test.afterAll(async () => closeFixture(application, directory))
async function surface(mode: 'desktop' | 'fullscreen', width = 1920, height = 1080) {
  await application.evaluate(
    ({ BrowserWindow }, value) => {
      const window = BrowserWindow.getAllWindows()[0]!
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
  await page.getByRole('button', { name: 'Winnow home', exact: true }).click()
}
const input = () => page.getByRole('searchbox', { name: 'Search games' })
const active = () => page.locator('.avalon-search-page [data-row-active="true"] .avalon-cover')
async function search() {
  await page.keyboard.press('Control+k')
  await expect(page.getByRole('heading', { name: 'Search', exact: true })).toBeVisible()
  await input().fill('')
  await expect(page.locator('.avalon-search-count')).toHaveText('90 games')
  await page.getByRole('button', { name: 'Go to results', exact: true }).click()
  await page.keyboard.press('Control+Home')
  await expect(active().first()).toBeFocused()
}
test('fullscreen Search returns to the same column, pages from its header, and releases narrowed rows', async () => {
  await surface('fullscreen')
  await search()
  const grid = page.locator('.avalon-search-page .avalon-fullscreen-grid')
  const columns = await page
    .locator('.avalon-search-page [data-row-active="true"]')
    .first()
    .locator('.avalon-cover')
    .count()
  await active().nth(2).focus()
  await page.keyboard.press('ArrowUp')
  await expect(page.getByRole('button', { name: 'Go to results', exact: true })).toBeFocused()
  await page.keyboard.press('ArrowDown')
  await expect(active().nth(2)).toBeFocused()
  await page.getByRole('button', { name: 'Enter search', exact: true }).focus()
  await expect
    .poll(() =>
      page.getByRole('button', { name: 'Enter search', exact: true }).evaluate((button) => {
        const style = getComputedStyle(button)
        return {
          background: style.backgroundColor,
          line: style.borderBottomWidth,
          focusedColor: style.borderBottomColor === style.color,
          outline: style.outlineStyle,
        }
      }),
    )
    .toEqual({ background: 'rgba(0, 0, 0, 0)', line: '3px', focusedColor: true, outline: 'none' })
  await page.evaluate(() => {
    const state = { observed: false, opaque: false, inactive: false }
    ;(window as unknown as { searchMotion: typeof state }).searchMotion = state
    const sample = () => {
      const moving = document.querySelector('.avalon-search-page [data-animating]')
      if (moving) {
        const rows = [...moving.querySelectorAll<HTMLElement>('.avalon-retained-row')]
        Object.assign(state, {
          observed: true,
          opaque: rows.every((row) =>
            [row, ...row.querySelectorAll('.avalon-cover, .artwork')].every(
              (element) => getComputedStyle(element).opacity === '1',
            ),
          ),
          inactive: rows.filter((row) => row.dataset.rowActive === 'false').every((row) => row.inert),
        })
      } else requestAnimationFrame(sample)
    }
    requestAnimationFrame(sample)
  })
  await page.keyboard.press('PageDown')
  await expect(grid).toHaveAttribute('data-selected-id', String(columns * 2 + 3))
  await expect(page.locator('.avalon-search-hints')).toContainText('Rows 2–3')
  await expect
    .poll(() => page.evaluate(() => (window as unknown as { searchMotion: object }).searchMotion))
    .toEqual({ observed: true, opaque: true, inactive: true })
  await grid.dispatchEvent('wheel', { deltaY: 120 })
  await grid.dispatchEvent('wheel', { deltaY: 120 })
  await expect(page.locator('.avalon-search-page .avalon-row-viewport')).toHaveAttribute(
    'data-first-row',
    '3',
  )
  const old = await page.locator('.avalon-search-page [data-row-active="false"]').first().elementHandle()
  await input().fill('Search game 090')
  await expect(active()).toHaveCount(1)
  await expect(input()).toBeFocused()
  expect(await old?.evaluate((element) => element.isConnected)).toBe(false)
  await expect(page.locator('.avalon-search-page [data-animating]')).toHaveCount(0)
  await input().fill('No matching title')
  await expect(page.getByRole('button', { name: 'Go to results', exact: true })).toBeDisabled()
  await expect(page.getByText('No games match. Try a different title.')).toBeVisible()
})
for (const [width, height, scale] of [
  [1280, 720, 1.4],
  [1920, 1080, 1],
] as const)
  test(`fullscreen Search fits two rows at ${width}x${height} and retains its query through Details`, async ({}, info) => {
    await surface('fullscreen', width, height)
    await search()
    await page.evaluate(
      (value) => document.documentElement.style.setProperty('--fullscreen-text-scale', String(value)),
      scale,
    )
    await expect.poll(async () => active().first().boundingBox()).not.toBeNull()
    const geometry = await active().evaluateAll((cards) =>
      cards.map((card) => {
        const box = card.getBoundingClientRect()
        return { top: box.top, bottom: box.bottom, left: box.left, right: box.right }
      }),
    )
    expect(new Set(geometry.map((box) => box.top)).size).toBe(2)
    expect(
      geometry.every((box) => box.top >= 0 && box.bottom <= height && box.left >= 0 && box.right <= width),
    ).toBe(true)
    await page.screenshot({ path: info.outputPath(`search-${width}.png`) })
    await input().fill('Search game 001')
    const card = page.getByRole('button', { name: 'View Search game 001', exact: true })
    await card.click({ modifiers: [] })
    await expect(page.locator('.avalon-details')).toBeVisible()
    await page.getByRole('button', { name: 'B · Back to Search', exact: true }).click()
    await expect(input()).toHaveValue('Search game 001')
    await expect(card).toBeFocused()
    await expect(page.locator('.avalon-search-page [data-animating]')).toHaveCount(0)
    await page.keyboard.press('Escape')
    await expect(page.locator('.avalon-home')).toBeVisible()
    await expect(page.locator('.avalon-shell.fullscreen')).toBeVisible()
    await page.evaluate(() => document.documentElement.style.setProperty('--fullscreen-text-scale', '1'))
  })
test('desktop keeps inline Library search while View, Y and LT/RT operate the fullscreen Search page', async () => {
  await surface('desktop', 1280, 720)
  await page.keyboard.press('Control+k')
  const desktop = page.locator('[data-library-search]')
  await expect(desktop).toBeFocused()
  await desktop.fill('desktop query')
  await expect(page.locator('.avalon-search-page')).toHaveCount(0)
  await surface('fullscreen', 1920, 1080)
  await page.evaluate(() => {
    const state = { pressed: [] as number[] }
    ;(window as unknown as { searchPad: typeof state }).searchPad = state
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
      ;(window as unknown as { searchPad: { pressed: number[] } }).searchPad.pressed = pressed
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
  const tap = async (button: number) => {
    await buttons([])
    await buttons([button])
    await buttons([])
  }
  await tap(8)
  await expect(input()).toBeVisible()
  await input().fill('')
  await page.getByRole('button', { name: 'Go to results', exact: true }).click()
  await page.keyboard.press('Control+Home')
  const columns = await page
    .locator('.avalon-search-page [data-row-active="true"]')
    .first()
    .locator('.avalon-cover')
    .count()
  await page.getByRole('button', { name: 'Enter search', exact: true }).focus()
  await tap(7)
  await expect(page.locator('.avalon-search-page .avalon-fullscreen-grid')).toHaveAttribute(
    'data-selected-id',
    String(columns * 2 + 1),
  )
  await tap(3)
  const keyboard = page.getByRole('dialog', { name: 'Enter text' })
  await expect(keyboard).toBeVisible()
  await tap(1)
  await expect(keyboard).toHaveCount(0)
  await expect(input()).toBeFocused()
  await page.evaluate(() =>
    Object.defineProperty(navigator, 'getGamepads', { configurable: true, value: () => [] }),
  )
  await application.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0]!.webContents.send('winnow:fullscreen:changed', false),
  )
  await expect(desktop).toHaveValue('desktop query')
  expect(errors).toEqual([])
})
