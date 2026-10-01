import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Page,
  type Locator,
} from '@playwright/test'
import { execFile } from 'node:child_process'
import { promisify } from 'node:util'
import { mkdir, mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import { libraryAction } from './library-controls'
import { assertAccessibleControls, assertDirectionalReachability } from './controller-accessibility-helpers'
import type { ApiRequest } from '../../src/shared/bridge'
import type { Mode, Workspace } from '../../src/renderer/api/types'

const fixture = resolve(
  '../..',
  '.tmp/task38113-fixture-artifacts/bin/Winnow.Electron.Fixtures/debug/Winnow.Electron.Fixtures.dll',
)
let app: ElectronApplication, page: Page, directory: string, mode: Mode, workId: number
let endpoint: { address: string; token: string }
const errors: string[] = []
const details = () => page.locator('.avalon-details')
const tiles = () => page.locator('.avalon-library [data-avalon-game]')
const tile = (id = workId) => page.locator(`.avalon-library [data-avalon-game="${id}"]`)
const match = () => details().getByRole('region', { name: 'Game match', exact: true })
type FixtureState = { rows: Record<string, Record<string, any>[]>; pending: number; searches: string[] }
async function api<T>(input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const response = await window.winnow.request(input)
    if (!response.ok) throw Error(`${input.route}: ${response.status} ${response.message}`)
    return response.data
  }, input) as Promise<T>
}
async function control<T>(path: string, body?: unknown): Promise<T> {
  const response = await fetch(new URL(`/__fixture/matching/${path}`, endpoint.address), {
    method: body === undefined ? 'GET' : 'POST',
    headers: { Authorization: `Bearer ${endpoint.token}`, 'Content-Type': 'application/json' },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    signal: AbortSignal.timeout(45000),
  })
  if (!response.ok) throw Error(`IGDB fixture ${path}: HTTP ${response.status}`)
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}
const state = () => control<FixtureState>('state')
async function tap(button: number) {
  // Standard Gamepad API frames are simulated; no physical-controller claim is made.
  for (const pressed of [false, true, false])
    await page.evaluate(
      async ({ button, pressed }) => {
        Object.defineProperty(navigator, 'getGamepads', {
          configurable: true,
          value: () => [
            {
              index: 0,
              connected: true,
              mapping: 'standard',
              axes: [0, 0, 0, 0],
              buttons: Array.from({ length: 17 }, (_, index) => ({
                pressed: pressed && index === button,
                touched: false,
                value: pressed && index === button ? 1 : 0,
              })),
            },
          ],
        })
        await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
      },
      { button, pressed },
    )
}
async function activate(control: Locator) {
  await expect(control).toBeEnabled()
  await control.scrollIntoViewIfNeeded()
  await control.focus()
  await expect(control).toBeFocused()
  if (mode === 'fullscreen') await tap(0)
  else await page.keyboard.press('Enter')
}
async function surface() {
  await app.evaluate(({ BrowserWindow }, mode) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setContentSize(mode === 'desktop' ? 1100 : 1920, mode === 'desktop' ? 900 : 1080)
    window.isFullScreen = () => mode === 'fullscreen'
    window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
    window.focus()
  }, mode)
  await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
}
async function library() {
  await activate(
    page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'Library', exact: true }),
  )
}
async function seed(kind = 'gog') {
  workId = (await control<{ workId: number }>('seed', { kind })).workId
  await library()
  await expect(tiles()).toHaveCount(kind === 'claim' || kind === 'pipeline' ? 2 : kind === 'mapping' ? 0 : 1)
}
async function openDetails(section = 'Overview') {
  await activate(tile())
  await expect(details().locator('h1')).toBeVisible()
  await sectionTo(section)
}
async function sectionTo(section: string) {
  if (mode === 'fullscreen' && section === 'Activity') {
    await activate(details().getByRole('button', { name: 'Play history →', exact: true }))
    await expect(details().getByRole('region', { name: 'History', exact: true })).toBeVisible()
  } else await activate(details().getByRole('tab', { name: section, exact: true }))
}
async function wrongGame() {
  await activate(details().getByRole('button', { name: 'More', exact: true }))
  await activate(page.getByRole('button', { name: 'Wrong game?', exact: true }))
  await expect(match().getByRole('textbox', { name: 'Game title or IGDB ID' })).toBeFocused()
}
async function search() {
  await expect(match().getByRole('textbox', { name: 'Game title or IGDB ID' })).toHaveValue('Prey')
  await activate(match().getByRole('button', { name: 'Search IGDB', exact: true }))
  await expect(match().locator('.igdb-candidate-row')).toHaveCount(1)
  const row = match().locator('.igdb-candidate-row')
  await expect(row).toContainText('Prey')
  await expect(row).toContainText('2017')
  await expect(row).toContainText('PC (Microsoft Windows), PlayStation 4')
}
async function choose() {
  await activate(match().getByRole('button', { name: 'Use this match', exact: true }).first())
}
async function closeDetails() {
  if (mode === 'fullscreen') await tap(1)
  else await activate(details().getByRole('button', { name: 'Close game details', exact: true }))
  await expect(details()).toHaveCount(0)
}
async function shot(name: string, subject: Locator) {
  await subject.scrollIntoViewIfNeeded()
  await expect(subject).toBeInViewport()
  const r = (await subject.boundingBox())!,
    viewport = await page.evaluate(() => ({ w: innerWidth, h: innerHeight }))
  expect(r.x).toBeGreaterThanOrEqual(-1)
  expect(r.y).toBeGreaterThanOrEqual(-1)
  expect(r.x + r.width).toBeLessThanOrEqual(viewport.w + 1)
  expect(r.y + r.height).toBeLessThanOrEqual(viewport.h + 1)
  await page.screenshot({ path: test.info().outputPath(`${mode}-${name}.png`), animations: 'disabled' })
}
async function picture(image: Locator) {
  await expect
    .poll(() =>
      image.evaluate(
        (node) => (node as HTMLImageElement).complete && (node as HTMLImageElement).naturalWidth > 0,
      ),
    )
    .toBe(true)
  return image.evaluate((node) => {
    const canvas = document.createElement('canvas')
    canvas.width = 100
    canvas.height = 150
    canvas.getContext('2d')!.drawImage(node as HTMLImageElement, 0, 0, 100, 150)
    return canvas.toDataURL()
  })
}
async function cachedArt() {
  await mkdir(join(directory, 'covers'), { recursive: true })
  for (const [name, color] of [
    ['igdb_co1r76', 20],
    ['igdb_co2abc', 180],
    ['steam_3900', 90],
    ['igdb-shot_oldshot', 130],
  ] as const) {
    const pixels = await app.evaluate(({ nativeImage }, color) => {
      const bytes = Buffer.alloc(300 * 450 * 4)
      for (let pixel = 0; pixel < 300 * 450; pixel++) {
        bytes[pixel * 4] = color
        bytes[pixel * 4 + 1] = 45
        bytes[pixel * 4 + 2] = 200 - color
        bytes[pixel * 4 + 3] = 255
      }
      return nativeImage.createFromBitmap(bytes, { width: 300, height: 450 }).toJPEG(90).toString('base64')
    }, color)
    await writeFile(join(directory, 'covers', `${name}.src.jpg`), Buffer.from(pixels, 'base64'))
  }
}
async function assertCorrected(section: string) {
  await expect(match()).toHaveCount(0)
  if (mode === 'fullscreen' && section === 'Activity')
    await expect(details().getByRole('region', { name: 'History', exact: true })).toBeVisible()
  else
    await expect(details().getByRole('tab', { name: section, exact: true })).toHaveAttribute(
      'aria-selected',
      'true',
    )
  await expect(details()).toContainText('Now using Prey.')
  await expect(details().locator('[data-details-identity-line]')).toContainText('2017')
  const current = await state(),
    work = current.rows.works.find((row) => row.id === workId)!
  expect(work).toMatchObject({
    name: 'Prey',
    igdb_id: 5678,
    first_release_year: 2017,
    publisher: 'Bethesda Softworks',
    summary: 'Morgan Yu wakes on Talos I.',
    cover_url: 'https://images.igdb.com/igdb/image/upload/t_cover_big/co2abc.jpg',
  })
  expect(current.rows.work_igdb_pins.filter((row) => row.cleared_at === null)).toMatchObject([
    { work_id: workId, igdb_id: 5678 },
  ])
  expect(current.rows.ownerships).toHaveLength(1)
  expect(current.rows.ownerships[0]).toMatchObject({ id: 1, release_id: 1 })
}
async function explicitContent(enabled: boolean) {
  const prefs = await api<Record<string, unknown>>({ route: 'preferences.library.get' })
  await api({ route: 'preferences.library.put', body: { ...prefs, showExplicitContent: enabled } })
}

test.beforeAll(async () => {
  test.setTimeout(120000)
  await promisify(execFile)(
    'dotnet',
    [
      'build',
      resolve('../..', 'tests/Winnow.Electron.Fixtures/Winnow.Electron.Fixtures.csproj'),
      '--artifacts-path',
      resolve('../..', '.tmp/task38113-fixture-artifacts'),
      '--nologo',
      '--verbosity',
      'quiet',
    ],
    { windowsHide: true, timeout: 115000 },
  )
})
test.beforeEach(async ({}, info) => {
  mode = info.title.startsWith('fullscreen') ? 'fullscreen' : 'desktop'
  const variant = info.title.includes('production GamesDB')
    ? 'pipeline'
    : info.title.includes('absent service')
      ? 'noservice'
      : info.title.includes('refused claim')
        ? 'refused'
        : 'ordinary'
  directory = await mkdtemp(join(resolve('../..', '.tmp'), `winnow-electron-matching-${variant}-`))
  errors.length = 0
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('tests/electron/igdb-matching-main.mjs'), '--data-dir', directory, '--no-sync'],
    env: {
      ...(Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ) as Record<string, string>),
      WINNOW_BACKEND_PATH: fixture,
    },
    chromiumSandbox: true,
    timeout: 60000,
  })
  page = await app.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible({ timeout: 45000 })
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
  expect((await fetch(new URL('/__fixture/matching/state', endpoint.address))).status).toBe(401)
  await cachedArt()
  await surface()
  await page.evaluate(() => document.fonts.ready)
})
test.afterEach(async ({}, info) => {
  try {
    if (info.status !== info.expectedStatus && page)
      await info.attach('igdb-matching-failure', { body: await page.screenshot(), contentType: 'image/png' })
    expect(await app.evaluate(() => (globalThis as any).__identityProjections.dispatches)).toEqual([])
  } finally {
    await closeFixture(app, directory)
  }
  expect(errors).toEqual([])
})

for (const surfaceName of ['desktop', 'fullscreen'] as const) {
  for (const section of ['Overview', 'Activity', 'Updates', 'Journal', 'Library'])
    test(`${surfaceName} exact Prey assignment rewrites metadata and cover while preserving ${section}`, async () => {
      await seed()
      const original = await picture(tile().locator('img').first())
      await openDetails(section)
      await wrongGame()
      await search()
      const candidate = await picture(match().locator('.igdb-candidate-row img').first())
      expect(candidate).not.toBe(original)
      if (section === 'Overview') await shot('prey-candidate', match().locator('.igdb-candidate-row'))
      await choose()
      await assertCorrected(section)
      if (section === 'Overview') await shot('corrected-prey', details().locator('h1'))
      if (mode === 'fullscreen' && section === 'Activity') await tap(1)
      await closeDetails()
      await expect.poll(() => picture(tile().locator('img').first())).toBe(candidate)
      await openDetails()
      await expect(details().getByRole('tab', { name: 'Overview', exact: true })).toHaveAttribute(
        'aria-selected',
        'true',
      )
      expect((await api<{ pin: unknown }>({ route: 'metadata.igdb', params: { workId } })).pin).not.toBeNull()
    })

  for (const decision of ['accept', 'decline', 'refused claim'])
    test(`${surfaceName} exact Prey collision ${decision} preserves source identity and pin boundaries`, async () => {
      await seed('claim')
      const before = await state()
      await openDetails()
      await wrongGame()
      await search()
      await choose()
      await expect(match().getByRole('heading', { name: 'Is this the same game as Prey?' })).toBeVisible()
      await expect(match().locator('.conflict-panel')).toContainText('2017')
      expect((await state()).rows).toEqual(before.rows)
      await assertAccessibleControls(page, match())
      if (mode === 'fullscreen') await assertDirectionalReachability(page, match(), tap)
      if (decision === 'decline') {
        await activate(match().getByRole('button', { name: 'Keep them separate' }))
        await expect(match()).toContainText('Another game in your library already uses that IGDB entry.')
        await expect(match().getByRole('heading', { name: 'Is this the same game as Prey?' })).toHaveCount(0)
        expect((await state()).rows).toEqual(before.rows)
        await shot('declined-claim', match().locator('.igdb-candidate-row'))
      } else {
        await activate(match().getByRole('button', { name: 'Yes, group these editions' }))
        if (decision === 'accept') {
          await expect(match()).toHaveCount(0)
          await expect(details()).toContainText('Linked with Prey.')
          const after = await state()
          expect(after.rows.identity_links).toHaveLength(1)
          expect(after.rows.identity_links[0]).toMatchObject({
            parent_work_id: 1,
            child_work_id: 2,
            kind: 'same_game',
            source: 'user',
            retracted_at: null,
          })
          expect(after.rows.work_igdb_pins).toEqual([])
          await closeDetails()
          await expect(tiles()).toHaveCount(1)
          await shot('accepted-claim-one-tile', tiles())
        } else {
          await expect(match().getByRole('heading', { name: 'Is this the same game as Prey?' })).toBeVisible()
          await expect(match().locator('[role="alert"]')).toHaveText("Couldn't link those. Nothing changed.")
          expect((await state()).rows).toEqual(before.rows)
          await shot('refused-claim', match().locator('.conflict-panel').last())
        }
      }
    })

  for (const noCover of [false, true])
    test(`${surfaceName} Steam3900 ${noCover ? 'coverless pin retains capsule' : 'pin beats capsule and clearing restores it'}`, async () => {
      await seed(noCover ? 'no-cover' : 'steam')
      const capsule = await picture(tile().locator('img').first())
      await openDetails()
      await wrongGame()
      await search()
      const candidate = noCover ? null : await picture(match().locator('.igdb-candidate-row img'))
      await choose()
      await expect(match()).toHaveCount(0)
      expect((await state()).rows.external_ids).toMatchObject([
        { release_id: 1, provider: 'steam', provider_id: '3900' },
      ])
      await closeDetails()
      await expect.poll(() => picture(tile().locator('img').first())).toBe(noCover ? capsule : candidate)
      await openDetails()
      await wrongGame()
      await expect(match()).toContainText('Matched to IGDB 5678.')
      await activate(match().getByRole('button', { name: 'Return to automatic matching' }))
      await expect(match()).toHaveCount(0)
      expect((await api<{ pin: unknown }>({ route: 'metadata.igdb', params: { workId } })).pin).toBeNull()
      await closeDetails()
      await expect.poll(() => picture(tile().locator('img').first())).toBe(capsule)
      expect((await state()).rows.works[0]).toMatchObject({ igdb_id: 5678, first_release_year: 2017 })
      await shot(noCover ? 'coverless-pin-capsule' : 'cleared-pin-capsule', tile())
    })

  test(`${surfaceName} absent service removes the optional Wrong game control`, async () => {
    await seed()
    await openDetails()
    expect(
      (await api<{ available: boolean }>({ route: 'metadata.igdb', params: { workId } })).available,
    ).toBe(false)
    await activate(details().getByRole('button', { name: 'More', exact: true }))
    await expect(page.getByRole('button', { name: 'Wrong game?', exact: true })).toHaveCount(0)
    await expect(details().getByRole('textbox', { name: 'Game title or IGDB ID' })).toHaveCount(0)
    await shot(
      'no-assignment-service',
      mode === 'fullscreen'
        ? page.getByRole('dialog').last().getByRole('heading').first()
        : details().locator('h1'),
    )
  })

  for (const place of ['Details', 'Settings'])
    test(`${surfaceName} ${place} candidate grid preserves cover text year platform and trailing action geometry`, async () => {
      await seed('geometry')
      let scope: Locator
      if (place === 'Details') {
        await openDetails()
        await wrongGame()
        scope = match()
      } else {
        await activate(await libraryAction(page, 'Manage library'))
        await activate(page.getByRole('button', { name: 'Manual games', exact: true }))
        await activate(page.getByRole('button', { name: 'Add a game', exact: true }))
        scope = page.locator('.manual-editor')
        await scope.getByLabel('Title', { exact: true }).fill('Prey')
      }
      await activate(
        scope.getByRole('button', {
          name: place === 'Details' ? 'Search IGDB' : 'Find IGDB matches',
          exact: true,
        }),
      )
      const rows = scope.locator('.igdb-candidate-row')
      await expect(rows).toHaveCount(2)
      const before = await state()
      const geometry = await rows.evaluateAll((nodes) =>
        nodes.map((node) => {
          const cover = node.querySelector('.igdb-candidate-cover')!,
            text = node.querySelector('.igdb-candidate-text')!,
            button = node.querySelector('button')!,
            line = node.querySelector('.igdb-candidate-detail')!,
            year = node.querySelector('.igdb-candidate-year')!,
            platforms = node.querySelector('.igdb-candidate-platforms')!
          const rect = (element: Element) => {
            const r = element.getBoundingClientRect()
            return { x: r.x, right: r.right, width: r.width, height: r.height }
          }
          return {
            row: rect(node),
            cover: rect(cover),
            text: rect(text),
            button: rect(button),
            year: rect(year),
            platforms: rect(platforms),
            rowDisplay: getComputedStyle(node).display,
            lineDisplay: getComputedStyle(line).display,
            coverColumn: getComputedStyle(cover).gridColumnStart,
            textColumn: getComputedStyle(text).gridColumnStart,
            buttonColumn: getComputedStyle(button).gridColumnStart,
            overflow: getComputedStyle(platforms).overflow,
            ellipsis: getComputedStyle(platforms).textOverflow,
            whiteSpace: getComputedStyle(platforms).whiteSpace,
            full: platforms.getAttribute('title'),
            value: platforms.textContent,
            scroll: platforms.scrollWidth,
            client: platforms.clientWidth,
          }
        }),
      )
      for (const g of geometry) {
        expect(g.rowDisplay).toBe('grid')
        expect(g.lineDisplay).toBe('grid')
        expect([g.coverColumn, g.textColumn, g.buttonColumn]).toEqual(['1', '2', '3'])
        expect(g.cover.width / g.cover.height).toBeCloseTo(34 / 51, 2)
        expect(g.cover.right).toBeLessThanOrEqual(g.text.x)
        expect(g.text.right).toBeLessThanOrEqual(g.button.x)
        expect(g.button.right).toBeLessThanOrEqual(g.row.right + 1)
        expect(g.year.right).toBeLessThanOrEqual(g.platforms.x + 1)
        expect(g.platforms.right).toBeLessThanOrEqual(g.text.right + 1)
        expect(g.full).toBe(g.value)
        expect(g.ellipsis).toBe('ellipsis')
        expect(g.whiteSpace).toBe('nowrap')
      }
      expect(geometry[0]!.row.height).toBeCloseTo(geometry[1]!.row.height, 0)
      expect(geometry[0]!.scroll).toBeGreaterThan(geometry[0]!.client)
      await assertAccessibleControls(page, scope)
      if (mode === 'fullscreen') await assertDirectionalReachability(page, scope, tap)
      await rows.last().getByRole('button').focus()
      await expect(rows.last().getByRole('button')).toBeInViewport()
      await shot(`${place.toLowerCase()}-candidate-geometry`, rows.first())
      expect((await state()).rows).toEqual(before.rows)
    })

  test(`${surfaceName} mapping111 to222 updates the same open Details and retires old visibility evidence`, async () => {
    await seed('mapping')
    await explicitContent(true)
    await expect(tiles()).toHaveCount(1)
    await openDetails()
    if (mode === 'fullscreen') await activate(details().locator('[data-details-reading="About"]'))
    const marker = await details().evaluate((node) => {
      ;(node as HTMLElement).dataset.fixtureIdentity = 'same-open-details'
      return 'same-open-details'
    })
    await expect(details()).toContainText('95')
    expect(
      (await api<{ images: unknown[]; ratings: unknown[] }>({ route: 'game.details', params: { workId } }))
        .images,
    ).toHaveLength(1)
    const revision = (await api<{ revision: string }>({ route: 'metadata.igdb', params: { workId } }))
      .revision
    expect(
      await api({
        route: 'metadata.assign',
        params: { workId },
        body: { igdbId: 222, expectedRevision: revision },
      }),
    ).toEqual({ outcome: 'Assigned' })
    await explicitContent(false)
    await expect(details().locator('h1')).toHaveText('Corrected game')
    await expect(details()).toHaveAttribute('data-fixture-identity', marker)
    const summary = details().getByText('Corrected summary', { exact: true })
    await expect(summary).toBeVisible()
    const current = await state()
    expect(current.rows.works[0]).toMatchObject({
      igdb_id: 222,
      name: 'Corrected game',
      summary: 'Corrected summary',
      first_release_year: 2020,
      background_url: null,
    })
    for (const table of ['work_facets', 'work_maturity', 'work_images', 'work_ratings'])
      expect(current.rows[table]).toEqual([])
    const read = await api<{ images: unknown[]; ratings: unknown[] }>({
      route: 'game.details',
      params: { workId },
    })
    expect(read.images).toEqual([])
    expect(read.ratings).toEqual([])
    await expect(details().getByText('95', { exact: true })).toHaveCount(0)
    await expect(details().locator('[data-key*="oldshot"]')).toHaveCount(0)
    await shot('corrected-live-details', summary)
    if (mode === 'fullscreen') await tap(1)
    await closeDetails()
    await expect(tiles()).toHaveCount(1)
    await expect(tile()).toContainText('Corrected game')
  })

  test(`${surfaceName} production GamesDB pipeline publishes qualified identity links to both surfaces`, async () => {
    await seed('pipeline')
    await expect(tiles()).toHaveCount(2)
    await control('pipeline', {})
    await expect(tiles()).toHaveCount(1)
    const current = await state()
    expect(current.rows.identity_links.filter((row) => row.retracted_at === null)).toHaveLength(1)
    expect(current.rows.ownerships).toHaveLength(2)
    expect(current.rows.external_ids).toHaveLength(2)
    expect(current.rows.release_edition_evidence).toHaveLength(2)
    expect(current.pending).toBe(0)
    expect(current.rows.releases.every((row) => row.igdb_version_id === null)).toBe(true)
    await expect(tiles()).toContainText('Shared game')
    await shot('qualified-gamesdb-publication', tiles())
    mode = mode === 'desktop' ? 'fullscreen' : 'desktop'
    await surface()
    await library()
    await expect(tiles()).toHaveCount(1)
    await expect(tiles()).toContainText('Shared game')
    await shot('inactive-surface-qualified-publication', tiles())
  })
}
