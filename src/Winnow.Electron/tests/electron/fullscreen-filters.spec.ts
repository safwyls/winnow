import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import electronPath from 'electron'
import { mkdtemp, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { closeFixture } from './fixture-cleanup'
import { assertAccessibleControls, assertDirectionalReachability } from './controller-accessibility-helpers'

let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-fullscreen-filters-'))
  const games = Array.from({ length: 12 }, (_, index) => ({
    workId: index + 1,
    title: `Filter game ${index + 1}`,
    bucket: index % 2 ? 'bounced' : 'never_played',
    playtimeMinutes: index % 2 ? 300 : 0,
    firstReleaseYear: 2000 + index,
    entries: [
      {
        ownershipId: index + 1,
        releaseId: index + 1,
        workId: index + 1,
        title: `Filter game ${index + 1}`,
        store: ['steam', 'epic', 'gog'][index % 3],
        installed: index % 2 === 0,
        playtimeMinutes: index % 2 ? 300 : 0,
      },
    ],
  }))
  const facets = ['genre', 'theme', 'tag', 'feature', 'controller', 'game_mode'].flatMap((kind, index) =>
    [0, 1].map((option) => ({
      id: index * 2 + option + 1,
      kind,
      slug: `${kind}-${option}`,
      name: `${kind} ${option ? 'two' : 'one'}`,
    })),
  )
  const path = join(directory, 'filters.json')
  await writeFile(
    path,
    JSON.stringify({
      library: { games, lists: [] },
      workspace: {
        works: [],
        externalIds: [],
        epicLaunchKeys: {},
        pluginActions: {},
        facets,
        releaseFacets: games.map((game, index) => ({
          releaseId: game.workId,
          facetIds: [1, 3, 5, 7, 9].map((id) => id + (index % 2)),
          gameModes: [`game_mode-${index % 2}`],
        })),
      },
      feed: { candidateCount: 0, confidence: 0, failed: false, shelves: [] },
    }),
  )
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/fullscreen-filters-main.mjs'),
      '--data-dir',
      directory,
      '--seed-sample',
      '--no-sync',
    ],
    env: {
      ...Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ),
      WINNOW_FILTER_FIXTURE: path,
    } as Record<string, string>,
    chromiumSandbox: true,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.locator('.avalon-shell')).toBeVisible()
})
test.afterAll(async () => closeFixture(application, directory))
test.afterEach(async () => expect(errors).toEqual([]))
const panel = () => page.getByRole('dialog', { name: 'Library filters', exact: true })
async function frames() {
  await page.evaluate(
    () => new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done()))),
  )
}
async function tap(button: number) {
  for (const pressed of [[], [button], []]) {
    await page.evaluate((pressed) => {
      ;(window as unknown as { filtersPad: { pressed: number[] } }).filtersPad.pressed = pressed
    }, pressed)
    await frames()
  }
}
async function open(width = 1920, height = 1080, scale = 1) {
  await page.reload()
  await expect(page.locator('.avalon-shell')).toBeVisible()
  await application.evaluate(
    ({ BrowserWindow }, { width, height }) => {
      const window = BrowserWindow.getAllWindows()[0]
      window.setFullScreen(false)
      window.setContentSize(width, height)
      window.webContents.send('winnow:fullscreen:changed', true)
      window.focus()
    },
    { width, height },
  )
  await expect(page.locator('.avalon-shell.fullscreen')).toBeVisible()
  await page.waitForFunction(({ width, height }) => innerWidth === width && innerHeight === height, {
    width,
    height,
  })
  await expect
    .poll(() =>
      page.evaluate(async () => (await window.winnow.request({ route: 'preferences.presentation.get' })).ok),
    )
    .toBe(true)
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
  await page.getByRole('button', { name: 'Filter & sort', exact: true }).click()
  await expect(panel().getByRole('button', { name: 'GENRE · Any', exact: true })).toBeVisible()
  await page.evaluate((scale) => {
    document.documentElement.style.setProperty('--fullscreen-text-scale', String(scale))
    const state = { pressed: [] as number[] }
    Object.assign(window, { filtersPad: state })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          connected: true,
          mapping: 'standard',
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, index) => ({
            pressed: state.pressed.includes(index),
            value: state.pressed.includes(index) ? 1 : 0,
          })),
        },
      ],
    })
  }, scale)
  await frames()
}

for (const [width, height, scale] of [
  [1920, 1080, 1],
  [1280, 720, 1.4],
] as const)
  test(`fullscreen Filters preserves Browse and Refine, readable type, controller hints and fixed actions at ${width}x${height} text ${scale}`, async ({}, info) => {
    await open(width, height, scale)
    await expect(panel().getByRole('heading', { name: 'Filter & sort' })).toHaveCSS(
      'font-size',
      `${64 * scale}px`,
    )
    await expect(panel().getByRole('heading', { name: 'Browse' })).toHaveCSS('font-size', `${32 * scale}px`)
    await expect(panel().getByRole('status')).toHaveText('12 games')
    await expect(panel().locator('.fullscreen-filter-refine > button')).toHaveCount(8)
    await expect(panel().locator('[data-filter-glyph]')).toHaveCount(3)
    for (const glyph of ['A', 'B', 'Y'])
      await expect(panel().locator(`[data-filter-glyph="${glyph}"] svg`)).toBeVisible()
    const layout = await panel().evaluate((element) => {
      const bounds = (selector: string) => {
        const rect = element.querySelector(selector)!.getBoundingClientRect()
        return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom }
      }
      return {
        browse: bounds('.fullscreen-filter-browse'),
        refine: bounds('.fullscreen-filter-refine'),
        footer: bounds('footer'),
        header: bounds('header'),
        outerScroll: element.scrollHeight - element.clientHeight,
      }
    })
    expect(layout.browse.right).toBeLessThanOrEqual(layout.refine.left + 1)
    expect(layout.footer.top).toBeGreaterThan(layout.header.bottom)
    expect(layout.footer.bottom).toBeLessThanOrEqual(height)
    expect(layout.refine.bottom).toBeLessThanOrEqual(layout.footer.top)
    expect(layout.outerScroll).toBeLessThanOrEqual(1)
    await assertAccessibleControls(page, panel())
    await assertDirectionalReachability(page, panel(), tap)
    await panel().getByRole('button', { name: 'Collection · All games' }).focus()
    await panel()
      .locator('.fullscreen-filter-browse, .fullscreen-filter-refine')
      .evaluateAll((columns) => columns.forEach((column) => column.scrollTo({ top: 0 })))
    await frames()
    await page.screenshot({ path: info.outputPath(`fullscreen-filters-${width}-${scale}.png`) })
  })

test('controller facet pages retain staged counts, restore focus with B, apply with Y and discard root edits', async ({}, info) => {
  await open()
  const genre = panel().getByRole('button', { name: 'GENRE · Any', exact: true })
  await genre.focus()
  await tap(0)
  await expect(panel().getByRole('heading', { name: 'GENRE', exact: true })).toHaveCSS('font-size', '48px')
  const option = panel().getByRole('button', { name: 'genre one, 6 matching titles', exact: true })
  await expect(option).toBeFocused()
  await tap(0)
  await expect(option).toHaveAttribute('aria-pressed', 'true')
  await expect(panel().getByRole('status')).toHaveText('6 games')
  await expect(page.locator('.avalon-results-count')).toHaveText('12 games')
  await page.screenshot({ path: info.outputPath('fullscreen-filters-genre.png') })
  await tap(1)
  await expect(panel().getByRole('button', { name: 'GENRE · 1 selected' })).toBeFocused()
  await tap(3)
  await expect(panel()).toHaveCount(0)
  await expect(page.locator('.avalon-results-count')).toHaveText('6 games')
  await expect(page.locator('.avalon-fullscreen-grid [data-selected="true"]')).toBeFocused()
  await page.getByRole('button', { name: 'Filter & sort', exact: true }).click()
  await panel().getByRole('button', { name: 'Clear filters', exact: true }).click()
  await expect(panel().getByRole('status')).toHaveText('12 games')
  await tap(1)
  await expect(panel()).toHaveCount(0)
  await expect(page.locator('.avalon-results-count')).toHaveText('6 games')
})

test('year buttons open the controller keyboard, return focus, reject invalid years and apply the accepted range', async ({}, info) => {
  await open()
  const from = panel().getByRole('button', { name: /^Release year from ·/ })
  await from.focus()
  await tap(0)
  const keyboard = page.getByRole('dialog', { name: 'Enter text', exact: true })
  await expect(keyboard).toBeVisible()
  await expect(keyboard.locator('[data-keyboard-glyph]')).toHaveCount(5)
  for (const digit of '999') await keyboard.getByRole('button', { name: digit, exact: true }).click()
  await tap(1)
  await expect(keyboard).toHaveCount(0)
  await expect(from).toBeFocused()
  await expect(panel().getByRole('button', { name: 'Apply filters' })).toBeDisabled()
  await tap(3)
  await expect(panel()).toBeVisible()
  await expect(panel().getByRole('alert')).toContainText('1000 to 9999')
  await panel().getByRole('button', { name: 'Clear filters' }).click()
  await from.focus()
  await tap(0)
  for (const digit of '2006') await keyboard.getByRole('button', { name: digit, exact: true }).click()
  await page.screenshot({ path: info.outputPath('fullscreen-filter-year-keyboard.png') })
  await tap(7)
  await expect(keyboard).toHaveCount(0)
  await expect(from).toBeFocused()
  await expect(from).toHaveText('Release year from · 2006')
  await expect(panel().getByRole('status')).toHaveText('6 games')
  await tap(3)
  await expect(panel()).toHaveCount(0)
  await expect(page.locator('.avalon-results-count')).toHaveText('6 games')
})
