import { closeFixture } from './fixture-cleanup'
import { fillLibrarySearch, libraryField, returnToLibrary, setLibrarySort } from './library-controls'
import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import type { FeedSnapshot, LibraryResponse } from '../../src/renderer/api/types'

type Fixture = { library: LibraryResponse; feed: FeedSnapshot }
let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
function fixture(lengths = [10, 10, 2, 10, 10, 10]): Fixture {
  const games = Array.from({ length: lengths.reduce((total, count) => total + count, 0) }, (_, index) => ({
    workId: index + 1,
    title: `Layout game ${index + 1}`,
    bucket: 'never_played',
    playtimeMinutes: 0,
    entries: [
      {
        ownershipId: index + 1,
        releaseId: index + 1,
        workId: index + 1,
        title: `Layout game ${index + 1}`,
        store: 'Steam',
        installed: true,
        playtimeMinutes: 0,
      },
    ],
  }))
  let offset = 0
  return {
    library: { games, lists: [] } as unknown as LibraryResponse,
    feed: {
      candidateCount: games.length,
      confidence: 2,
      failed: false,
      shelves: lengths.map((count, index) => ({
        id: `layout-${index}`,
        title: `Layout shelf ${index + 1}`,
        blurb: 'A deterministic presentation fixture.',
        supportsFeedback: false,
        reserve: [],
        items: games.slice(offset, (offset += count)).map((game) => ({
          ownershipId: game.workId,
          releaseId: game.workId,
          title: game.title,
          reason: 'Something worth coming back to.',
        })),
      })),
    },
  }
}
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-layout-'))
  const fixturePath = join(directory, 'layout-fixture.json')
  await writeFile(fixturePath, JSON.stringify(fixture()))
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
async function replace(data: Fixture) {
  await application.evaluate(({ BrowserWindow }, value) => {
    ;(globalThis as unknown as { __winnowLayoutFixture: Fixture }).__winnowLayoutFixture = value
    BrowserWindow.getAllWindows()[0]!.webContents.send('winnow:event', {
      kind: 'library.changed',
      resource: 'feed',
    })
  }, data)
  await expect(page.getByRole('button', { name: 'Show Layout shelf 1', exact: true })).toBeAttached()
}
async function surface(mode: 'desktop' | 'fullscreen', width: number, height: number) {
  await page.emulateMedia({ reducedMotion: 'no-preference' })
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
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'For you', exact: true })
    .click()
  await page.evaluate(() => {
    document.documentElement.style.setProperty('--fullscreen-interface-scale', '1')
    document.documentElement.style.setProperty('--fullscreen-text-scale', '1')
    document.documentElement.style.setProperty('--fullscreen-safe-margin', '5%')
    document.documentElement.dataset.fitUltrawide = 'true'
  })
  await measured()
}
const activeCovers = () => page.locator('[data-row-active="true"] .avalon-cover')
async function measured() {
  await page.evaluate(
    () =>
      new Promise<void>((done) => {
        let previous = '',
          equalFrames = 0
        const sample = () => {
          const current = JSON.stringify(
            Array.from(document.querySelectorAll('.avalon-row-viewport, .avalon-cover')).map((element) => {
              const bounds = element.getBoundingClientRect()
              return [bounds.width, bounds.height]
            }),
          )
          equalFrames = current === previous ? equalFrames + 1 : 0
          previous = current
          if (equalFrames >= 3) done()
          else requestAnimationFrame(sample)
        }
        requestAnimationFrame(sample)
      }),
  )
}
async function settled() {
  await expect(page.locator('.avalon-row-viewport[data-animating]')).toHaveCount(0)
}

for (const mode of ['desktop', 'fullscreen'] as const)
  for (const reduced of [false, true])
    test(`${mode} grouped store marks fit the cover and preserve store names with reduced motion ${reduced}`, async ({}, info) => {
      await surface(mode, 1200, 820)
      const data = fixture([8])
      const prey = data.library.games[1]
      prey.title = 'Prey'
      prey.entries = ['steam', 'STEAM', 'epic', 'gog'].map((store, index) => ({
        ...prey.entries[0],
        store,
        ownershipId: 100 + index,
        releaseId: 100 + index,
      }))
      await application.evaluate((_, data) => {
        ;(globalThis as unknown as { __winnowLayoutFixture: Fixture }).__winnowLayoutFixture = data
      }, data)
      await page.reload()
      await surface(mode, 1200, 820)
      await page
        .getByRole('navigation', { name: 'Main navigation' })
        .getByRole('button', { name: 'Library', exact: true })
        .click()
      if (mode === 'desktop') {
        await page.getByRole('button', { name: 'Grid view', exact: true }).click()
        await page.getByRole('slider', { name: 'Density', exact: true }).fill('200')
      }
      await setLibrarySort(page, 'title')
      if (mode === 'desktop') await page.getByLabel('Sort', { exact: true }).focus()
      else await page.getByRole('button', { name: 'Filter & sort', exact: true }).focus()
      await page.emulateMedia({ reducedMotion: reduced ? 'reduce' : 'no-preference' })
      await page.mouse.move(2, 2)
      const cover = page.locator('.avalon-library .avalon-cover[data-work-id="2"]')
      const initials = cover.locator('.avalon-store-initials')
      await expect(initials.locator('span').first()).toHaveCSS('background-color', 'rgba(15, 28, 30, 0.82)')
      await expect(cover).toHaveAccessibleName('View Prey. Owned on Steam, Epic, GOG')
      await expect(initials).toHaveCSS('opacity', '1')
      await expect(initials.locator('span')).toHaveText(['S', 'E', 'G'])
      await expect(cover.locator('.avalon-store-chips > span')).toHaveText(['STEAM', 'EPIC', 'GOG'])
      await expect(initials).toHaveCSS('transition-duration', reduced ? '0s' : '0.14s')
      const bounds = await cover.boundingBox(),
        marks = await initials.boundingBox()
      expect(marks!.x).toBeGreaterThan(bounds!.x)
      expect(marks!.x + marks!.width).toBeLessThan(bounds!.x + bounds!.width)
      expect(marks!.y + marks!.height).toBeLessThan(bounds!.y + bounds!.height)
      const title = await cover.locator('.avalon-cover-fallback').boundingBox()
      expect(title!.y + title!.height).toBeLessThan(marks!.y - 3)
      if (mode === 'desktop') expect(bounds!.width).toBeLessThan(140)
      await page.screenshot({ path: info.outputPath(`${mode}-store-initials-${reduced}.png`) })
      await cover.hover()
      await expect(initials).toHaveCSS('opacity', mode === 'desktop' ? '0' : '1')
      if (mode === 'desktop') {
        await expect(cover.locator('.avalon-cover-caption')).toBeVisible()
        await expect(cover.locator('.avalon-cover-caption')).toHaveCSS('opacity', '1')
      } else await expect(cover.locator('.avalon-cover-caption')).toBeHidden()
      await cover.focus()
      await page.mouse.move(2, 2)
      await expect(initials).toHaveCSS('opacity', mode === 'desktop' ? '0' : '1')
      if (mode === 'desktop') {
        await expect(cover.locator('.avalon-cover-caption')).toBeVisible()
        await expect(cover.locator('.avalon-cover-caption')).toHaveCSS('opacity', '1')
      }
      expect(errors).toEqual([])
    })

for (const width of [1200, 1920])
  test(`desktop wall at ${width} uses whole-pixel cells and removes only complete rows without a trailing gutter`, async () => {
    const data = fixture([10])
    const original = data.library.games[0]
    const games = Array.from({ length: 1012 }, (_, index) => ({
      ...original,
      workId: index + 1,
      title: `Wall game ${String(index + 1).padStart(4, '0')}`,
      entries: [{ ...original.entries[0], workId: index + 1, ownershipId: index + 1, releaseId: index + 1 }],
    }))
    const publish = async (count: number) => {
      data.library.games = games.slice(0, count)
      await application.evaluate(({ BrowserWindow }, data) => {
        ;(globalThis as unknown as { __winnowLayoutFixture: Fixture }).__winnowLayoutFixture = data
        BrowserWindow.getAllWindows()[0].webContents.send('winnow:event', {
          kind: 'library.changed',
          resource: 'library',
        })
      }, data)
    }
    await publish(1012)
    await page.reload()
    await surface('desktop', width, 900)
    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'Library', exact: true })
      .click()
    await page.getByRole('button', { name: 'Grid view', exact: true }).click()
    await page.getByLabel('Sort', { exact: true }).selectOption('title')
    await page.mouse.move(2, 2)
    const count = page.locator('.avalon-results-count'),
      scroll = page.locator('.avalon-library-scroll')
    for (const density of [108, 148, 200]) {
      await publish(1012)
      await expect(count).toHaveText('1,012 games')
      await scroll.evaluate((element) => {
        element.scrollTop = 0
      })
      await page.getByRole('slider', { name: 'Density', exact: true }).fill(String(density))
      await measured()
      const geometry = await page
        .locator('.avalon-grid-row')
        .first()
        .evaluate((row) => {
          const style = getComputedStyle(row),
            cell = row.querySelector('.avalon-cover')!
          return {
            columns: style.gridTemplateColumns.split(' ').length,
            gap: parseFloat(style.columnGap),
            width: row.getBoundingClientRect().width,
            cellWidth: cell.getBoundingClientRect().width,
            cellHeight: cell.getBoundingClientRect().height,
          }
        })
      const { columns, gap, cellWidth, cellHeight } = geometry
      const used = columns * cellWidth + (columns - 1) * gap
      expect(used).toBeLessThanOrEqual(geometry.width)
      expect(geometry.width - used).toBeLessThan(columns + 1)
      expect(Number.isInteger(cellWidth)).toBe(true)
      expect(cellHeight).toBe(Math.floor(cellWidth * 1.5))
      const extent = (items: number) => Math.ceil(items / columns) * (cellHeight + gap) - gap
      await expect(page.locator('.avalon-wall')).toHaveCSS('height', `${extent(1012)}px`)
      await publish(1011)
      await expect(count).toHaveText('1,011 games')
      await expect(page.locator('.avalon-wall')).toHaveCSS('height', `${extent(1011)}px`)
      await scroll.evaluate((element) => {
        element.scrollTop = element.scrollHeight
      })
      await expect(page.locator('.avalon-cover[data-work-id="1011"]')).toBeVisible()
      const bottom = await page.locator('.avalon-wall').evaluate((wall) => ({
        wall: wall.getBoundingClientRect().bottom,
        last: wall.querySelector('.avalon-cover[data-work-id="1011"]')!.getBoundingClientRect().bottom,
      }))
      expect(Math.abs(bottom.wall - bottom.last)).toBeLessThan(0.1)
      await publish(1)
      await expect(count).toHaveText('1 game')
      await expect(page.locator('.avalon-wall')).toHaveCSS('height', `${cellHeight}px`)
    }
    await publish(0)
    await expect(count).toHaveText('0 games')
    await expect(page.locator('.avalon-wall')).toHaveCount(0)
    expect(errors).toEqual([])
  })

test('fullscreen Home retains each overflow page and carries the visible column across shelves', async () => {
  await surface('fullscreen', 1920, 1080)
  await replace(fixture([24, 24, 2]))
  await page.getByRole('button', { name: 'Show Layout shelf 1', exact: true }).click()
  await settled()
  const capacity = await activeCovers().count()
  expect(capacity).toBeLessThan(24)
  await activeCovers().first().focus()
  for (let index = 0; index < capacity + 1; index++) await page.keyboard.press('ArrowRight')
  await expect(page.locator('[data-row-active="true"] .avalon-home-row')).toHaveAttribute(
    'data-home-page',
    '1',
  )
  await expect(activeCovers().nth(1)).toBeFocused()
  await page.keyboard.press('ArrowDown')
  await expect(activeCovers().nth(1)).toBeFocused()
  await expect(page.locator('[data-row-active="true"] .avalon-home-row')).toHaveAttribute(
    'data-home-page',
    '0',
  )
  await page.keyboard.press('ArrowRight')
  await page.keyboard.press('ArrowRight')
  await page.keyboard.press('ArrowUp')
  await expect(activeCovers().nth(3)).toBeFocused()
  await expect(page.locator('[data-row-active="true"] .avalon-home-row')).toHaveAttribute(
    'data-home-page',
    '1',
  )
  await page.keyboard.press('ArrowDown')
  await page.keyboard.press('ArrowDown')
  await expect(activeCovers()).toHaveCount(2)
  await expect(activeCovers().last()).toBeFocused()
  await page.keyboard.press('ArrowUp')
  await expect(activeCovers().nth(1)).toBeFocused()
  await settled()
})

test('fullscreen theme font changes retain measured cover space', async () => {
  await surface('fullscreen', 1280, 720)
  const data = fixture([24])
  data.feed.shelves[0].supportsFeedback = true
  await replace(data)
  async function metrics() {
    return page.evaluate(() =>
      Object.fromEntries(
        [
          '.avalon-home',
          '.avalon-header',
          '.avalon-footer',
          '.avalon-home-hero',
          '.avalon-home-hero > .avalon-label',
          '.avalon-home-hero h1',
          '.avalon-home-hero > p',
          '.avalon-hero-actions',
          '.avalon-home-shelf > h2',
          '.avalon-row-viewport',
          '.avalon-cover',
        ].map((selector) => {
          const element = document.querySelector(selector)!,
            rect = element.getBoundingClientRect(),
            style = getComputedStyle(element)
          return [
            selector,
            {
              height: rect.height,
              width: rect.width,
              min: style.minHeight,
              font: style.fontSize,
              line: style.lineHeight,
            },
          ]
        }),
      ),
    )
  }
  const before = await metrics()
  await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
  await page.getByRole('combobox', { name: 'Heading font', exact: true }).fill('IBM Plex Mono')
  await page.getByRole('combobox', { name: 'Interface font', exact: true }).fill('Bricolage Grotesque')
  await page.getByRole('slider', { name: 'Theme text size', exact: true }).fill('120')
  await page.getByRole('button', { name: 'Winnow home', exact: true }).click()
  await expect(page.locator('.avalon-home-hero')).toBeVisible()
  const after = await metrics()
  expect(after['.avalon-cover'].width, JSON.stringify({ before, after }, null, 2)).toBeCloseTo(
    before['.avalon-cover'].width,
    1,
  )
  await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
  await page.getByRole('button', { name: 'Reset theme typography' }).click()
  await page.getByRole('button', { name: 'Winnow home', exact: true }).click()
})

for (const searching of [false, true])
  test(`fullscreen ${searching ? 'filtered results' : 'Library'} retain two rows, slide opaque neighbors and release rows when narrowed`, async () => {
    await surface('fullscreen', 1920, 1080)
    const data = fixture([90])
    for (const game of data.library.games) game.title = `Library game ${String(game.workId).padStart(3, '0')}`
    await replace(data)
    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'Library', exact: true })
      .click()
    await fillLibrarySearch(page, searching ? 'Library game' : '')
    const grid = page.locator('.avalon-fullscreen-grid'),
      viewport = grid.locator('.avalon-row-viewport')
    await expect(grid).toBeVisible()
    // The old Home shelf can remain visible while the invalidated library is still being read.
    await expect(page.locator('.avalon-results-count')).toHaveText(/^90 games(?: matching “Library game”)?$/)
    await expect(activeCovers().first()).toHaveAccessibleName(/^View Library game \d{3}$/)
    await measured()
    await activeCovers().first().focus()
    await page.keyboard.press('Control+Home')
    await expect(grid).toHaveAttribute('data-selected-id', '1')
    await expect(grid.locator('[data-avalon-game="1"]')).toBeFocused()
    await settled()
    const columns = await grid.locator('[data-row-active="true"]').first().locator('.avalon-cover').count()
    expect(columns).toBeGreaterThan(2)
    await page.keyboard.press('ArrowDown')
    await expect(viewport).toHaveAttribute('data-first-row', '0')
    await expect(grid).toHaveAttribute('data-selected-id', String(columns + 1))
    const overlap = await page.evaluate(() => {
      const rows = document.querySelectorAll('.avalon-fullscreen-grid [data-row-active="true"]')
      ;(window as unknown as { overlap: Element }).overlap = rows[1]
      return rows[1].getAttribute('data-row-id')!
    })
    await page.keyboard.press('ArrowDown')
    await expect(viewport).toHaveAttribute('data-first-row', '1')
    await expect(grid).toHaveAttribute('data-selected-id', String(columns * 2 + 1))
    expect(
      await page.evaluate(() => {
        const old = document.querySelector('.avalon-fullscreen-grid [data-row-active="false"]')!
        return {
          opacity: getComputedStyle(old).opacity,
          inert: old.hasAttribute('inert'),
          attached: (window as unknown as { overlap: Element }).overlap.isConnected,
        }
      }),
    ).toEqual({ opacity: '1', inert: true, attached: true })
    await expect(grid.locator(`[data-row-id="${overlap}"]`)).toHaveCount(1)
    await settled()
    await grid.dispatchEvent('wheel', { deltaY: 100 })
    await expect(viewport).toHaveAttribute('data-first-row', '2')
    const search = await libraryField(page, 'Search games')
    await search.fill('Library game 090')
    await expect(activeCovers()).toHaveCount(1)
    await expect(viewport).toHaveAttribute('data-first-row', '0')
    await expect(viewport).not.toHaveAttribute('data-animating')
    await expect(search).toBeFocused()
    await search.fill('Library game 001')
    await expect(grid).toHaveAttribute('data-selected-id', '1')
    await search.fill('Library game')
    await expect(activeCovers().first()).toHaveAttribute('data-avalon-game', '1')
    await returnToLibrary(page)
    const expandedColumns = await grid
      .locator('[data-row-active="true"]')
      .first()
      .locator('.avalon-cover')
      .count()
    await activeCovers().first().focus()
    await page.keyboard.press('ArrowDown')
    await page.keyboard.press('ArrowDown')
    await expect(grid).toHaveAttribute('data-selected-id', String(expandedColumns * 2 + 1))
    await settled()
    const selected = await grid.getAttribute('data-selected-id')
    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'For you', exact: true })
      .click()
    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'Library', exact: true })
      .click()
    await expect(grid).toHaveAttribute('data-selected-id', selected!)
    await expect(viewport).toHaveAttribute('data-first-row', '1')
    await expect(viewport).not.toHaveAttribute('data-animating')
    await fillLibrarySearch(page, '')
    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'For you', exact: true })
      .click()
    await replace(fixture())
    expect(errors).toEqual([])
  })

test('desktop uses one bounded row, fits five covers at the default window and scrolls narrower shelves', async () => {
  for (const width of [1280, 1600, 900]) {
    await surface('desktop', width, 820)
    const geometry = await page
      .locator('.avalon-desktop-covers')
      .first()
      .evaluate((row) => ({
        viewport: row.clientWidth,
        extent: row.scrollWidth,
        cards: Array.from(row.querySelectorAll('.avalon-cover')).map((card) => {
          const bounds = card.getBoundingClientRect()
          return { x: bounds.x, y: bounds.y, width: bounds.width }
        }),
      }))
    expect(geometry.cards).toHaveLength(5)
    expect(new Set(geometry.cards.map((card) => card.y)).size).toBe(1)
    for (const card of geometry.cards) {
      expect(card.width).toBeGreaterThanOrEqual(180)
      expect(card.width).toBeLessThanOrEqual(240)
    }
    if (width === 900) expect(geometry.extent).toBeGreaterThan(geometry.viewport)
    else expect(geometry.extent).toBeLessThanOrEqual(geometry.viewport + 1)
  }
})
test('desktop directional input reveals the last cover and carries its column between shelves', async () => {
  await surface('desktop', 900, 700)
  await page.locator('.avalon-desktop-covers').first().locator('.avalon-cover').first().focus()
  for (let index = 0; index < 5; index++) await page.keyboard.press('ArrowRight')
  for (const key of ['ArrowDown', 'ArrowUp']) {
    await page.keyboard.press(key)
    const visible = await page.evaluate(() => {
      const cover = document.activeElement as HTMLElement,
        row = cover.closest('.avalon-desktop-covers')!
      const item = cover.getBoundingClientRect(),
        viewport = row.getBoundingClientRect()
      return {
        index: Array.from(row.querySelectorAll('.avalon-cover')).indexOf(cover),
        left: item.left - viewport.left,
        right: viewport.right - item.right,
        scroll: row.scrollLeft,
      }
    })
    expect(visible.index).toBe(4)
    expect(visible.scroll).toBeGreaterThan(0)
    expect(visible.left).toBeGreaterThanOrEqual(-1)
    expect(visible.right).toBeGreaterThanOrEqual(-1)
  }
})
test('fullscreen retains row nodes, carries and clamps columns on revisits, and reverses without fading', async () => {
  await surface('fullscreen', 1920, 1080)
  await page.getByRole('button', { name: 'Show Layout shelf 1', exact: true }).click()
  await settled()
  await measured()
  await activeCovers().nth(3).focus()
  await expect(activeCovers().nth(3)).toBeFocused()
  await page.evaluate(() => {
    const rows = Array.from(document.querySelectorAll('.avalon-retained-row'))
    ;(window as unknown as { retained: Element[] }).retained = rows
    const state = { sampled: false, translation: 0, height: 0, opacity: '', inert: false, focused: '' }
    ;(window as unknown as { reversal: typeof state }).reversal = state
    const sample = () => {
      const row = document.querySelector<HTMLElement>('[data-row-active="true"]')!
      const translation = new DOMMatrixReadOnly(getComputedStyle(row).transform).m42
      if (row.dataset.rowId === 'layout-1' && translation > 0 && translation < row.clientHeight) {
        const old = document.querySelector<HTMLElement>('[data-row-id="layout-0"]')!
        Object.assign(state, {
          sampled: true,
          translation,
          height: row.clientHeight,
          opacity: getComputedStyle(old).opacity,
          inert: old.inert,
          focused: document.activeElement?.getAttribute('data-avalon-game'),
        })
        // Reverse in the measured frame; an automation roundtrip can exceed the whole transition.
        document.activeElement?.dispatchEvent(
          new KeyboardEvent('keydown', {
            key: 'ArrowUp',
            bubbles: true,
            cancelable: true,
          }),
        )
      } else requestAnimationFrame(sample)
    }
    requestAnimationFrame(sample)
  })
  await page.keyboard.press('ArrowDown')
  await page.waitForFunction(() => (window as unknown as { reversal: { sampled: boolean } }).reversal.sampled)
  expect(await page.evaluate(() => (window as unknown as { reversal: object }).reversal)).toMatchObject({
    sampled: true,
    opacity: '1',
    inert: true,
    focused: '14',
  })
  await settled()
  await expect(activeCovers().nth(3)).toBeFocused()
  expect(
    await page.evaluate(() =>
      (window as unknown as { retained: Element[] }).retained.every((row) => row.isConnected),
    ),
  ).toBe(true)
  await activeCovers().nth(1).focus()
  await page.keyboard.press('ArrowDown')
  await settled()
  await expect(activeCovers().nth(1)).toBeFocused()
  await activeCovers().nth(3).focus()
  await page.keyboard.press('ArrowDown')
  await settled()
  await expect(activeCovers().nth(1)).toBeFocused()
  await page.keyboard.press('ArrowUp')
  await settled()
  await expect(activeCovers().nth(1)).toBeFocused()
  const nodes = await page.locator('.avalon-retained-row').evaluateAll((rows) =>
    rows.map((row) => ({
      opacity: getComputedStyle(row).opacity,
      inert: (row as HTMLElement).inert,
      active: row.getAttribute('data-row-active'),
    })),
  )
  expect(nodes.length).toBeLessThanOrEqual(3)
  expect(nodes.every((row) => row.opacity === '1' && row.inert === (row.active !== 'true'))).toBe(true)
})
test('fullscreen keeps cover geometry stable for long titles and two-line reasons at every text scale', async () => {
  await surface('fullscreen', 1280, 720)
  const data = fixture()
  data.library.games[1].title =
    'An exceptionally long game title that must stay on a single line without pushing the cover shelf below the screen '.repeat(
      4,
    )
  data.feed.shelves[0].items[0].reason = ''
  data.feed.shelves[0].items[1].reason =
    'A long recommendation with enough detail to exceed two lines and preserve the cover shelf geometry. '.repeat(
      8,
    )
  await replace(data)
  await page.getByRole('button', { name: 'Show Layout shelf 1', exact: true }).click()
  await settled()
  await expect(activeCovers().nth(1)).toHaveAccessibleName(`View ${data.library.games[1].title}`)
  for (const scale of [0.7, 1, 1.4]) {
    await page.evaluate(
      (value) => document.documentElement.style.setProperty('--fullscreen-text-scale', String(value)),
      scale,
    )
    await activeCovers().first().focus()
    const before = await activeCovers().first().boundingBox()
    await page.keyboard.press('ArrowRight')
    const after = await activeCovers().first().boundingBox()
    expect(after).toEqual(before)
    const text = await page.locator('.avalon-home-hero').evaluate((hero) => {
      const title = hero.querySelector('h1')!,
        reason = hero.querySelector('p')!
      return {
        titleHeight: title.getBoundingClientRect().height,
        lineHeight: parseFloat(getComputedStyle(title).lineHeight),
        ellipsis: getComputedStyle(title).textOverflow,
        reasonHeight: reason.getBoundingClientRect().height,
        reasonLine: parseFloat(getComputedStyle(reason).lineHeight),
      }
    })
    expect(Math.abs(text.titleHeight - text.lineHeight)).toBeLessThan(1)
    expect(text.ellipsis).toBe('ellipsis')
    expect(Math.abs(text.reasonHeight - text.reasonLine * 2)).toBeLessThan(1)
    await page.keyboard.press('ArrowRight')
    expect(await activeCovers().first().boundingBox()).toEqual(before)
    expect(await page.locator('.avalon-home-hero > p').boundingBox()).toMatchObject({
      height: text.reasonHeight,
    })
  }
  expect(errors).toEqual([])
})

for (const [textScale, reducedMotion] of [
  [1, false],
  [1.4, false],
  [1.4, true],
] as const)
  test(`fullscreen repeated refresh retains every frame of geometry at text ${textScale}, reduced motion ${reducedMotion}`, async () => {
    await surface('fullscreen', 1920, 1080)
    await replace(fixture([10]))
    await expect(page.getByRole('button', { name: 'Show Layout shelf 2', exact: true })).toHaveCount(0)
    await page.evaluate(
      ({ textScale, reducedMotion }) => {
        document.documentElement.style.setProperty('--fullscreen-text-scale', String(textScale))
        document.documentElement.classList.toggle('reduced-motion', reducedMotion)
      },
      { textScale, reducedMotion },
    )
    await page.emulateMedia({ reducedMotion: reducedMotion ? 'reduce' : 'no-preference' })
    await activeCovers().nth(3).focus()
    const record = await page.evaluate(() => {
      const row = document.querySelector('.avalon-row-viewport')!,
        cover = document.activeElement!
      const base = row.getBoundingClientRect().toJSON()
      const samples: string[] = [],
        state = { running: true, row, cover, base, samples }
      ;(window as unknown as { geometry: typeof state }).geometry = state
      const sample = () => {
        if (!state.running) return
        samples.push(JSON.stringify(row.getBoundingClientRect().toJSON()))
        requestAnimationFrame(sample)
      }
      requestAnimationFrame(sample)
      return base
    })
    for (let index = 0; index < 3; index++) {
      await replace(fixture([10]))
      await page.evaluate(
        () => new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done()))),
      )
    }
    await replace(fixture([10, 10, 10, 10]))
    await expect(page.getByRole('button', { name: 'Show Layout shelf 4', exact: true })).toBeAttached()
    const result = await page.evaluate(() => {
      const state = (
        window as unknown as {
          geometry: { running: boolean; row: Element; cover: Element; samples: string[] }
        }
      ).geometry
      state.running = false
      return {
        samples: state.samples,
        retained: state.row === document.querySelector('.avalon-row-viewport'),
        focused: state.cover === document.activeElement,
      }
    })
    expect(result.retained).toBe(true)
    expect(result.focused).toBe(true)
    expect(result.samples.length).toBeGreaterThan(2)
    expect(new Set(result.samples)).toEqual(new Set([JSON.stringify(record)]))
    await page.keyboard.press('ArrowDown')
    await expect(activeCovers().nth(3)).toBeFocused()
    if (reducedMotion) expect(await page.locator('.avalon-row-viewport[data-animating]').count()).toBe(0)
    await settled()
    await page.emulateMedia({ reducedMotion: 'no-preference' })
    await page.evaluate(() => document.documentElement.classList.remove('reduced-motion'))
  })

for (const [margin, text] of [
  [5, 1],
  [10, 1.4],
])
  test(`fullscreen interface scaling preserves shelf alignment at margin ${margin}, text ${text}`, async () => {
    await surface('fullscreen', 1920, 1080)
    await replace(fixture())
    await page.getByRole('button', { name: 'Show Layout shelf 1', exact: true }).click()
    await settled()
    await page.evaluate(
      ({ margin, text }) => {
        document.documentElement.style.setProperty('--fullscreen-safe-margin', `${margin}%`)
        document.documentElement.style.setProperty('--fullscreen-text-scale', String(text))
      },
      { margin, text },
    )
    async function geometry() {
      return page.evaluate(() => {
        const page = document.querySelector('.avalon-content')!.getBoundingClientRect(),
          row = document.querySelector('.avalon-row-viewport')!.getBoundingClientRect(),
          heading = document.querySelector('.avalon-home-shelf > h2')!.getBoundingClientRect(),
          rail = document.querySelector('.avalon-shelf-navigation')!.getBoundingClientRect(),
          cover = document.querySelector('[data-row-active="true"] .avalon-cover')!.getBoundingClientRect()
        return {
          height: cover.height,
          bottomGap: page.bottom - row.bottom,
          headingGap: row.top - heading.bottom,
          centerGap: (rail.top + rail.bottom - row.top - row.bottom) / 2,
        }
      })
    }
    const initial = await geometry()
    const safeArea = await page.locator('.avalon-shell').evaluate((shell) => ({
      supported: CSS.supports('padding-block', 'calc(5% * (100vh / 100vw))'),
      padding: parseFloat(getComputedStyle(shell).paddingTop),
    }))
    expect(safeArea.supported).toBe(true)
    expect(safeArea.padding).toBeCloseTo((1080 * margin) / 100, 0)
    expect(initial.height).toBeGreaterThan(100)
    for (const scale of [0.8, 1, 1.2]) {
      await page.evaluate(
        (value) => document.documentElement.style.setProperty('--fullscreen-interface-scale', String(value)),
        scale,
      )
      await page.evaluate(
        () => new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done()))),
      )
      const next = await geometry()
      expect(Math.abs(next.bottomGap)).toBeLessThan(3)
      expect(Math.abs(next.centerGap)).toBeLessThan(1)
      expect(next.headingGap / scale).toBeGreaterThanOrEqual(11)
      expect(next.headingGap / scale).toBeLessThanOrEqual(13)
      if (scale <= 1) {
        expect(next.height / initial.height).toBeGreaterThanOrEqual(scale - 0.02)
        expect(next.height / initial.height).toBeLessThanOrEqual(scale + 0.02)
      }
    }
  })

test('ultrawide fullscreen reaches every recommendation and its backdrop fills the canvas', async () => {
  await surface('fullscreen', 3440, 1440)
  await replace(fixture())
  await page.getByRole('button', { name: 'Show Layout shelf 1', exact: true }).click()
  await settled()
  await page.evaluate(() => {
    document.documentElement.style.setProperty('--fullscreen-interface-scale', '.8')
    document.documentElement.style.setProperty('--fullscreen-safe-margin', '10%')
  })
  await expect(activeCovers().first()).toBeVisible()
  await activeCovers().first().focus()
  for (let index = 0; index < 9; index++) await page.keyboard.press('ArrowRight')
  await expect(page.locator('[data-row-active="true"] [data-avalon-game="10"]')).toBeFocused()
  const bounds = await page.locator('.avalon-home-backdrop').boundingBox()
  expect(bounds).toEqual({ x: 0, y: 0, width: 3440, height: 1440 })
  expect(
    await activeCovers()
      .last()
      .evaluate((cover) => {
        const item = cover.getBoundingClientRect(),
          row = cover.closest('.avalon-home-row')!.getBoundingClientRect()
        return item.left >= row.left - 1 && item.right <= row.right + 1
      }),
  ).toBe(true)
  expect(errors).toEqual([])
})

for (const [width, height] of [
  [1280, 720],
  [3840, 2160],
])
  test(`fullscreen long hero settles after a palette change and Settings return at ${width}x${height}`, async () => {
    await surface('fullscreen', width, height)
    const data = fixture([1])
    data.library.games[0].title = 'The Forgotten Kingdom: Adventures Beyond the Distant Northern Mountains'
    data.feed.shelves[0].items[0].reason = 'Explore new regions and finish your adventure. '.repeat(30)
    await replace(data)
    await page.getByRole('button', { name: 'Show Layout shelf 1', exact: true }).click()
    await settled()
    await expect(activeCovers()).toHaveCount(1)
    await page.evaluate(() => {
      document.documentElement.style.setProperty('--fullscreen-text-scale', '1.4')
      document.documentElement.style.setProperty('--fullscreen-safe-margin', '2%')
    })
    await page.getByRole('button', { name: 'Settings', exact: true }).click()
    await page.getByRole('banner').getByRole('button', { name: 'Theme Studio', exact: true }).click()
    await page.getByRole('combobox', { name: /^Avalon palette/ }).selectOption('bottle-green')
    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'For you', exact: true })
      .click()
    await expect(activeCovers().first()).toBeFocused()
    const frames = await page.evaluate(
      () =>
        new Promise<string[]>((done) => {
          const samples: string[] = []
          const sample = () => {
            samples.push(
              JSON.stringify(
                document.querySelector('.avalon-row-viewport')!.getBoundingClientRect().toJSON(),
              ),
            )
            if (samples.length === 60) done(samples)
            else requestAnimationFrame(sample)
          }
          requestAnimationFrame(sample)
        }),
    )
    expect(new Set(frames.slice(-10)).size).toBe(1)
    expect(new Set(frames).size).toBeLessThan(10)
    expect(errors).toEqual([])
  })
