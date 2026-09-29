import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import type { ApiRequest } from '../../src/shared/bridge'
import type { GameList, LibraryResponse, ManualGame } from '../../src/renderer/api/types'

let app: ElectronApplication, page: Page, directory: string, sourceList: GameList, targetList: GameList
const failures: string[] = []
const titles = [
  'Hollow Knight',
  'Disco Elysium',
  'The Witcher 3: Wild Hunt',
  'Stardew Valley',
  'Celeste',
  "Baldur's Gate 3",
  'Slay the Spire',
  'Portal 2',
]
const games: ManualGame[] = []
async function api<T>(input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const response = await window.winnow.request(input)
    if (!response.ok) throw Error(`${input.route}: ${response.status}`)
    return response.data
  }, input) as Promise<T>
}
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-spine-'))
  const env = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('.'), '--data-dir', directory, '--seed-sample', '--no-sync'],
    env,
    chromiumSandbox: true,
    timeout: 60000,
  })
  page = await app.firstWindow()
  page.on('pageerror', (error) => failures.push(error.message))
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
  if (await page.getByRole('dialog', { name: 'Winnow setup' }).count())
    await page.getByRole('button', { name: 'Skip setup', exact: true }).click()
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
  for (const title of titles)
    games.push(await api<ManualGame>({ route: 'manual.create', body: { title, platformLabel: 'PC' } }))
  sourceList = await api<GameList>({
    route: 'list.create',
    body: { name: 'Original eight-game fixture', releaseIds: games.map((game) => game.releaseId) },
  })
  targetList = await api<GameList>({ route: 'list.create', body: { name: 'Scroll test', releaseIds: [] } })
  await page.reload()
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
  // Like the source fixture, constrain only the scroll viewport so eight titles overflow.
  await page.addStyleTag({ content: '.avalon-shell.desktop .avalon-library-scroll { max-height: 160px; }' })
})
test.afterAll(async () => {
  if (directory)
    try {
      const endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
      if (new URL(endpoint.address).hostname !== '127.0.0.1') throw Error('Unexpected fixture address')
      await fetch(new URL('/api/v1/lifecycle/shutdown', endpoint.address), {
        method: 'POST',
        headers: { Authorization: `Bearer ${endpoint.token}` },
        signal: AbortSignal.timeout(5000),
      })
    } catch {
      /* Preserve the isolated fixture for diagnostics. */
    }
  if (app) {
    const child = app.process()
    const shutdown: string[] = []
    child.stderr?.on('data', (chunk: Buffer) => {
      for (const line of chunk.toString().split(/\r?\n/))
        if (line.startsWith('SPINE_SHUTDOWN:')) shutdown.push(line)
    })
    await app.evaluate(({ app, BrowserWindow }) => {
      app.on('before-quit', () => console.error('SPINE_SHUTDOWN:before-quit'))
      app.on('will-quit', () => console.error('SPINE_SHUTDOWN:will-quit'))
      app.on('quit', (_, code) => console.error(`SPINE_SHUTDOWN:quit:${code}`))
      for (const window of BrowserWindow.getAllWindows())
        window.on('closed', () => console.error('SPINE_SHUTDOWN:window-closed'))
    })
    let timeout: ReturnType<typeof setTimeout> | undefined
    try {
      await Promise.race([
        app.close(),
        new Promise<never>((_, reject) => {
          timeout = setTimeout(() => {
            const exitCode = child.exitCode,
              signalCode = child.signalCode
            if (exitCode === null && signalCode === null) child.kill('SIGKILL')
            reject(
              Error(
                `Electron close timed out: exit=${exitCode}, signal=${signalCode}; ${shutdown.join(', ')}`,
              ),
            )
          }, 5000)
        }),
      ])
      if (child.exitCode === null && child.signalCode === null)
        await new Promise<void>((done) => child.once('exit', () => done()))
      expect(child.exitCode).toBe(0)
    } finally {
      clearTimeout(timeout)
    }
  }
})
async function prepare(view: 'grid' | 'list') {
  await app.evaluate(({ BrowserWindow }) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setContentSize(1200, 640)
    window.webContents.send('winnow:fullscreen:changed', false)
  })
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
  const listPicker = page.getByRole('combobox', { name: 'My lists', exact: true })
  if ((await listPicker.inputValue()) !== String(sourceList.id))
    await listPicker.selectOption(String(sourceList.id))
  await page.getByRole('combobox', { name: 'Sort', exact: true }).selectOption('dormant')
  await page.getByRole('button', { name: view === 'grid' ? 'Grid view' : 'List view', exact: true }).click()
  await expect(page.locator('.avalon-results-count')).toHaveText('8 games')
  await scrollTo(0)
}
async function scrollTo(top: number) {
  await page.locator('.avalon-library-scroll').evaluate(async (element, top) => {
    element.scrollTop = top
    await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
  }, top)
}
const offset = () => page.locator('.avalon-library-scroll').evaluate((element) => element.scrollTop)
async function point(row: number) {
  const box = (await page.locator('.avalon-browse-spine').boundingBox())!
  return { x: box.x + box.width / 2, y: box.y + (box.height * (row + 0.5)) / 27 }
}
async function hover(row: number) {
  const location = await point(row)
  await page.mouse.move(location.x, location.y)
}
async function drag(from: number, to: number) {
  await hover(from)
  await page.mouse.down()
  await hover(to)
  await page.mouse.up()
}
const displacement = (stop: number) =>
  page
    .locator('.avalon-browse-spine')
    .locator(':scope > *')
    .nth(stop)
    .evaluate((element) => new DOMMatrix(getComputedStyle(element).transform).m41)

for (const view of ['grid', 'list'] as const) {
  test(`${view} spine preserves ordering and selection with source pointer, scrub, halo and keyboard behavior`, async ({}, info) => {
    await prepare(view)
    const rail = page.locator('.avalon-browse-spine')
    await expect(rail.locator('.avalon-sort-notch')).toHaveCount(27)
    expect(await rail.evaluate((element) => getComputedStyle(element).backgroundColor)).toBe(
      'rgba(0, 0, 0, 0)',
    )
    await drag(2, 13)
    const proportion = await page
      .locator('.avalon-library-scroll')
      .evaluate((element) => element.scrollTop / (element.scrollHeight - element.clientHeight))
    expect(proportion).toBeGreaterThanOrEqual(0.48)
    expect(proportion).toBeLessThanOrEqual(0.52)
    await expect(rail.locator(':scope > *').nth(13)).toHaveAttribute('data-halo', '4')
    expect(await displacement(13)).toBeCloseTo(-13, 1)
    await page.mouse.move(400, 200)
    await page.getByRole('combobox', { name: 'Sort', exact: true }).selectOption('title')
    await scrollTo(0)
    await expect(rail.getByRole('button')).toHaveCount(27)
    await expect(rail.getByRole('button', { name: 'Jump to #' })).toBeDisabled()
    await expect(rail.getByRole('button', { name: 'Jump to A' })).toBeDisabled()
    await expect(rail.getByRole('button', { name: 'Jump to T' })).toBeEnabled()
    await expect(rail.getByRole('button', { name: 'Jump to B' })).toHaveAttribute('data-halo', '4')
    const geometry = await rail.evaluate((element) => {
      const scroll = document.querySelector<HTMLElement>('.avalon-library-scroll')!
      const box = element.getBoundingClientRect(),
        scrollbarLeft = scroll.getBoundingClientRect().left + scroll.clientWidth
      const stops = [...element.querySelectorAll('button')]
      const cards = [...scroll.querySelectorAll('[data-avalon-game]')]
      return {
        width: box.width,
        rowHeight: box.height / 27,
        gap: scrollbarLeft - box.right,
        font: getComputedStyle(stops[0]).fontSize,
        cursor: getComputedStyle(element).cursor,
        centers: stops.map((stop) => {
          const glyph = stop.firstElementChild!.getBoundingClientRect()
          return glyph.x + glyph.width / 2
        }),
        covered: cards.some((card) => card.getBoundingClientRect().right > box.left - 13),
        halo: getComputedStyle(stops[2].firstElementChild!).backgroundColor,
      }
    })
    expect(geometry.width).toBe(24)
    expect(geometry.rowHeight).toBeGreaterThanOrEqual(11)
    expect(geometry.gap).toBeGreaterThanOrEqual(0)
    expect(geometry.gap).toBeLessThanOrEqual(2)
    expect(geometry.font).toBe('11px')
    expect(geometry.cursor).toBe('default')
    expect(Math.max(...geometry.centers) - Math.min(...geometry.centers)).toBeLessThan(0.01)
    expect(geometry.covered).toBe(false)
    expect(geometry.halo).not.toBe('rgba(0, 0, 0, 0)')
    const firstCard = page.locator('.avalon-library [data-avalon-game]').first()
    await firstCard.evaluate((element: HTMLElement) => element.focus({ preventScroll: true }))
    const selected = await firstCard.getAttribute('data-work-id')
    await hover(20)
    expect(await offset()).toBe(0)
    expect(await displacement(20)).toBeCloseTo(-13, 1)
    await hover(20.35)
    expect(await displacement(20)).toBeGreaterThan(-12.9)
    expect(await displacement(20)).toBeLessThan(-12.5)
    await page.emulateMedia({ reducedMotion: 'reduce' })
    await hover(20.35)
    expect(
      await rail
        .locator('button')
        .nth(20)
        .evaluate((element) => getComputedStyle(element).transitionDuration),
    ).toMatch(/^0(?:s|\.0*1ms)$/)
    await hover(20)
    await page.mouse.down()
    await page.mouse.up()
    await expect(page.getByRole('combobox', { name: 'Sort', exact: true })).toHaveValue('title')
    await expect.poll(offset).toBeGreaterThan(0)
    await scrollTo(0)
    await expect(page.locator(`.avalon-library [data-work-id="${selected}"]`)).toHaveAttribute(
      view === 'grid' ? 'data-selected' : 'aria-pressed',
      'true',
    )
    await scrollTo(0)
    await drag(2, 13)
    await expect(rail.locator('button').nth(13)).toHaveAttribute('data-halo', '4')
    expect(await displacement(13)).toBeCloseTo(-13, 1)
    const scrubbed = await offset()
    await scrollTo(0)
    const jumpP = rail.getByRole('button', { name: 'Jump to P' })
    await jumpP.focus()
    await jumpP.press('Enter')
    await expect.poll(offset).toBe(scrubbed)
    await page.getByRole('combobox', { name: 'Sort', exact: true }).selectOption('title-desc')
    await scrollTo(0)
    await expect(rail.getByRole('button').first()).toHaveAccessibleName('Jump to Z')
    const jumpC = rail.getByRole('button', { name: 'Jump to C' })
    await jumpC.focus()
    await jumpC.press('Space')
    await expect.poll(offset).toBeGreaterThan(0)
    await expect(page.getByRole('combobox', { name: 'Sort', exact: true })).toHaveValue('title-desc')
    await hover(8)
    await page.screenshot({ path: info.outputPath(`${view}-spine-wave.png`) })
    await page.mouse.move(400, 200)
    expect(
      await rail
        .getByRole('button')
        .evaluateAll((buttons) =>
          buttons.every((button) => new DOMMatrix(getComputedStyle(button).transform).m41 === 0),
        ),
    ).toBe(true)
    await scrollTo(0)
    await expect(rail.getByRole('button', { name: 'Jump to T' })).toHaveAttribute('data-halo', '4')
    await page.getByRole('combobox', { name: 'Sort', exact: true }).selectOption('time')
    await expect(rail.locator('.avalon-sort-notch')).toHaveCount(27)
    await expect(rail.getByRole('button')).toHaveCount(0)
    await page.emulateMedia({ reducedMotion: 'no-preference' })
  })

  test(`${view} retains scroll after adding to a list and restores it after Details changes the underlying offset`, async () => {
    await prepare(view)
    await page.getByRole('combobox', { name: 'Sort', exact: true }).selectOption('title')
    await scrollTo(200)
    const before = await offset()
    expect(before).toBeGreaterThan(0)
    const chosen = games.find((game) => game.workId === games[2].workId)!
    const card = page.locator(`.avalon-library [data-work-id="${chosen.workId}"]`)
    await card.evaluate((element: HTMLElement) => element.focus({ preventScroll: true }))
    await page.getByRole('button', { name: 'Add to list…', exact: true }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Scroll test', exact: true }).click()
    await expect(page.getByRole('dialog')).toHaveCount(0)
    expect(await offset()).toBe(before)
    expect(
      (await api<LibraryResponse>({ route: 'library.get' })).lists.find((list) => list.id === targetList.id)!
        .releaseIds,
    ).toContain(chosen.releaseId)
    await card.evaluate((element: HTMLElement) => element.click())
    await expect(page.locator('.avalon-details')).toBeVisible()
    await scrollTo(0)
    expect(await offset()).toBe(0)
    await page.getByRole('button', { name: 'Close game details', exact: true }).click()
    await expect(page.locator('.avalon-details')).toHaveCount(0)
    await expect.poll(offset).toBe(before)
  })
}
test('fullscreen keeps its paged browse surface without a desktop spine', async () => {
  await prepare('grid')
  await app.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0]!.webContents.send('winnow:fullscreen:changed', true),
  )
  await expect(page.locator('.avalon-shell')).toHaveClass(/fullscreen/)
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
  await expect(page.locator('.avalon-browse-spine')).toHaveCount(0)
  await expect(page.locator('.avalon-fullscreen-grid')).toBeVisible()
  expect(failures).toEqual([])
})
