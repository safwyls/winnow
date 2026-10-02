import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Page,
  type Locator,
} from '@playwright/test'
import { mkdir, mkdtemp, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { createHash } from 'node:crypto'
import { DatabaseSync } from 'node:sqlite'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import type { ApiRequest } from '../../src/shared/bridge'
import type { GameDetails, LibraryGame, LibraryResponse, Mode } from '../../src/renderer/api/types'

// Frozen ExpansionLinkTests / IdentityReadModelTests at cf45d9f. SQLite writes only seed
// source facts; every identity mutation and subsequent read uses production preload/API.
const identityNow = '2026-08-23T12:00:00Z',
  expansionNow = '2026-09-01T12:00:00Z'
const civ = "Sid Meier's Civilization IV",
  pack = `${civ}: Beyond the Sword`
let application: ElectronApplication, page: Page, directory: string, mode: Mode
const errors: string[] = []
interface Seed {
  title: string
  minutes: number
  days: number | null
  store?: string
  year?: number
  achievements?: [number, number]
}
interface Bucket {
  ownershipId: number
  releaseId: number
  workId: number
  resolvedWorkId: number
  playtimeMinutes: number
  lastPlayedAt: string | null
  bucket: string
}
interface Link {
  id: number
  actId: number
  childWorkId: number
  parentWorkId: number
  kind: string
  retractedAt: string | null
}
interface Workspace {
  buckets: Bucket[]
  identityLinks: Link[]
}
const date = (now: string, days: number) => new Date(Date.parse(now) - days * 86400000).toISOString()
// Match DapperConfig.UtcDateTimeHandler's UTC wall-clock storage, without an ISO zone suffix.
const storedDate = (instant: string) =>
  new Date(instant)
    .toISOString()
    .replace('T', ' ')
    .replace(/\.000Z$/, '')
    .replace(/Z$/, '')
const tile = (id: number) => page.locator(`.avalon-library [data-avalon-game="${id}"]`)
const details = () => page.locator('.avalon-details')
function database<T>(read: (db: DatabaseSync) => T): T {
  const db = new DatabaseSync(join(directory, 'winnow.db'))
  try {
    db.exec('PRAGMA busy_timeout=5000')
    return read(db)
  } finally {
    db.close()
  }
}
async function api<T>(input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const response = await window.winnow.request(input)
    if (!response.ok) throw Error(`${input.route}: ${response.status} ${response.message}`)
    return response.data
  }, input) as Promise<T>
}
const library = () => api<LibraryResponse>({ route: 'library.get' })
const workspace = () => api<Workspace>({ route: 'library.workspace' })
const liveLinks = async () => (await workspace()).identityLinks.filter((link) => !link.retractedAt)
function persistedFacts() {
  return database((db) => ({
    works: db.prepare('SELECT * FROM works ORDER BY id').all(),
    releases: db.prepare('SELECT * FROM releases ORDER BY id').all(),
    ownerships: db.prepare('SELECT * FROM ownerships ORDER BY id').all(),
    plays: db.prepare('SELECT * FROM play_records ORDER BY id').all(),
  }))
}
function ownBuckets(rows: Bucket[]) {
  return rows.map(({ ownershipId, releaseId, workId, playtimeMinutes, lastPlayedAt, bucket }) => ({
    ownershipId,
    releaseId,
    workId,
    playtimeMinutes,
    lastPlayedAt,
    bucket,
  }))
}
function counts(games: LibraryGame[]) {
  const stores: Record<string, number> = {},
    buckets: Record<string, number> = {}
  for (const game of games) {
    buckets[game.bucket] = (buckets[game.bucket] ?? 0) + 1
    for (const store of new Set(game.entries.map((entry) => entry.store)))
      stores[store] = (stores[store] ?? 0) + 1
  }
  return { total: games.length, stores, buckets }
}
async function tap(button: number) {
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
  await control.scrollIntoViewIfNeeded()
  await control.focus()
  if (mode === 'fullscreen') await tap(0)
  else await page.keyboard.press('Enter')
}
async function open(id: number) {
  await activate(tile(id))
  await expect(details()).toBeVisible()
}
async function section(name: string) {
  await activate(details().getByRole('tab', { name, exact: true }))
}
async function expansions() {
  if (mode === 'fullscreen') await section('Library')
  else if ((await details().locator('.detail-expansions').getAttribute('open')) === null)
    await activate(details().locator('.detail-expansions summary'))
}
async function closeDetails() {
  if (mode === 'fullscreen') await tap(1)
  else await activate(details().getByRole('button', { name: 'Close game details', exact: true }))
  await expect(details()).toHaveCount(0)
}
async function link(parentWorkId: number, childWorkIds: number[], kind = 'same_game') {
  const review = await api<{ revision: string }>({ route: 'identity.get' })
  await api({
    route: 'identity.link',
    body: {
      expectedRevision: review.revision,
      parentWorkId,
      childWorkIds,
      kind,
      relationLabel: kind === 'expansion_of' ? 'expansion' : null,
      rejectedCandidateIds: [],
      refusedPairs: [],
    },
  })
}
async function screenshot(name: string) {
  if (await page.locator('.relationship-confirmation').count()) {
    // The active confirmation has its own geometry checks; Radix hides the background from ARIA queries.
    await page.screenshot({ path: test.info().outputPath(`${mode}-${name}.png`), animations: 'disabled' })
    return
  }
  const blurbs = details().getByText(
    /^(Counted separately\. Not added above\.|A separate game, grouped for display\.)$/,
  )
  for (const blurb of await blurbs.all()) {
    await expect(blurb).toBeVisible()
    const colors = await blurb.evaluate((element) => {
      const probe = document.createElement('span')
      probe.style.color = 'var(--text)'
      element.append(probe)
      const expected = getComputedStyle(probe).color
      probe.remove()
      return [getComputedStyle(element).color, expected]
    })
    expect(colors[0]).toBe(colors[1])
  }
  const disclosure = details().locator('.detail-expansions[open]')
  if (await disclosure.count()) {
    const summary = await disclosure.locator('summary').boundingBox()
    const heading = await disclosure.getByRole('heading').first().boundingBox()
    expect(heading!.y).toBeGreaterThanOrEqual(summary!.y + summary!.height)
  }
  await page.screenshot({ path: test.info().outputPath(`${mode}-${name}.png`), animations: 'disabled' })
}
async function showRelationship(row: Locator) {
  await row.scrollIntoViewIfNeeded()
  await expect(row).toBeInViewport({ ratio: 1 })
  for (const button of await row.getByRole('button').all()) await expect(button).toBeInViewport({ ratio: 1 })
}
async function confirmationPresentation(confirmation: Locator) {
  await expect(confirmation).toBeInViewport({ ratio: 1 })
  if (mode !== 'fullscreen') return
  const hints = confirmation.getByRole('group', { name: 'Relationship controls', exact: true })
  await expect(hints.locator(':scope > span')).toHaveText(['D-pad Choose', 'A Select', 'B Cancel'])
  await expect(hints).toBeInViewport({ ratio: 1 })
  for (const glyph of ['D-pad', 'A', 'B']) {
    const artwork = hints.locator(`[data-relationship-glyph="${glyph}"]`)
    await expect(artwork.locator('svg')).toBeVisible()
    await expect(artwork).toBeInViewport({ ratio: 1 })
    expect(
      await artwork.locator('svg').evaluate((element) => {
        const svg = element as SVGSVGElement
        const view = svg.viewBox.baseVal
        return (
          view.width > 0 &&
          view.height > 0 &&
          [...svg.querySelectorAll('path')].every((path) => {
            const ink = path.getBBox()
            return (
              ink.x >= view.x &&
              ink.y >= view.y &&
              ink.x + ink.width <= view.x + view.width &&
              ink.y + ink.height <= view.y + view.height
            )
          })
        )
      }),
      `${glyph} artwork must fit completely inside the SVG viewBox`,
    ).toBe(true)
  }
  expect(
    await confirmation.locator('h2').evaluate((node) => parseFloat(getComputedStyle(node).fontSize)),
  ).toBeGreaterThanOrEqual(48)
  expect(
    await confirmation
      .locator('p')
      .first()
      .evaluate((node) => parseFloat(getComputedStyle(node).fontSize)),
  ).toBeGreaterThanOrEqual(27)
  for (const button of await confirmation.getByRole('button').all()) {
    await expect(button).toBeInViewport({ ratio: 1 })
    expect(
      await button.evaluate((node) => parseFloat(getComputedStyle(node).fontSize)),
    ).toBeGreaterThanOrEqual(31)
  }
}
async function focusedDetailControl() {
  await expect
    .poll(() =>
      details().evaluate((element) => {
        const focused = document.activeElement
        if (
          !(focused instanceof HTMLElement) ||
          !element.contains(focused) ||
          !focused.matches('button, [role="tab"], summary, input, select')
        )
          return false
        const bounds = focused.getBoundingClientRect()
        return bounds.width > 0 && bounds.height > 0 && bounds.top >= 0 && bounds.bottom <= innerHeight
      }),
    )
    .toBe(true)
}
async function seed(entries: Seed[], expansion = false) {
  const now = expansion ? expansionNow : identityNow,
    appBase = expansion ? 800000 : 500000
  await mkdir(join(directory, 'covers'), { recursive: true })
  // Synthetic, distinct cached cover pixels keep artwork selection real and avoid provider traffic.
  for (let index = 0; index < entries.length; index++) {
    const pixels = await application.evaluate(({ nativeImage }, index) => {
      const bytes = Buffer.alloc(300 * 450 * 4)
      for (let pixel = 0; pixel < 300 * 450; pixel++) {
        bytes[pixel * 4] = index % 2 ? 90 : 35
        bytes[pixel * 4 + 1] = index % 2 ? 45 : 110
        bytes[pixel * 4 + 2] = index % 2 ? 155 : 40
        bytes[pixel * 4 + 3] = 255
      }
      return nativeImage.createFromBitmap(bytes, { width: 300, height: 450 }).toJPEG(90).toString('base64')
    }, index)
    for (const provider of ['steam', 'steam-hero', 'steam-hero-standard'])
      await writeFile(
        join(directory, 'covers', `${provider}_${appBase + index + 1}.src.jpg`),
        Buffer.from(pixels, 'base64'),
      )
  }
  database((db) => {
    db.exec('BEGIN')
    try {
      for (let index = 0; index < entries.length; index++) {
        const entry = entries[index],
          id = index + 1
        db.prepare('INSERT INTO works(id,name,sort_name,first_release_year,publisher) VALUES(?,?,?,?,?)').run(
          id,
          entry.title,
          entry.title,
          entry.year ?? (expansion ? 2005 : 2017),
          expansion ? '2K Games' : null,
        )
        db.prepare("INSERT INTO releases(id,work_id,name,platform) VALUES(?,?,?,'windows')").run(
          id,
          id,
          entry.title,
        )
        db.prepare('INSERT INTO ownerships(id,release_id,store,installed) VALUES(?,?,?,0)').run(
          id,
          id,
          entry.store ?? 'steam',
        )
        db.prepare("INSERT INTO external_ids(release_id,provider,provider_id) VALUES(?,'steam',?)").run(
          id,
          String(appBase + id),
        )
        db.prepare(
          "INSERT INTO play_records(ownership_id,playtime_minutes,last_played_at,source,observed_at) VALUES(?,?,?,'steam_localconfig',?)",
        ).run(
          id,
          entry.minutes,
          entry.days === null ? null : storedDate(date(now, entry.days)),
          storedDate(now),
        )
        if (entry.achievements) {
          db.prepare(
            "INSERT OR REPLACE INTO settings(key,value) VALUES('steam.owned_account_ref','12345')",
          ).run()
          const observed = storedDate(new Date().toISOString())
          db.prepare(
            "INSERT INTO achievement_observations(release_id,account_ref,availability,attempted_at,schema_at,progress_at) VALUES(?,'12345',3,?,?,?)",
          ).run(id, observed, observed, observed)
          for (let achievement = 0; achievement < entry.achievements[0]; achievement++) {
            const key = `ach_${id}_${achievement}`
            db.prepare('INSERT INTO achievements(release_id,provider_key,name,hidden) VALUES(?,?,?,0)').run(
              id,
              key,
              `Achievement ${achievement}`,
            )
            if (achievement < entry.achievements[1])
              db.prepare(
                "INSERT INTO account_achievement_unlocks(release_id,provider_key,account_ref,unlocked_at) VALUES(?,?,'12345',?)",
              ).run(id, key, storedDate(date(now, 10)))
          }
        }
      }
      db.exec('COMMIT')
    } catch (error) {
      db.exec('ROLLBACK')
      throw error
    }
  })
  await page.reload()
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
  await application.evaluate(({ BrowserWindow }, mode) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setContentSize(mode === 'desktop' ? 1440 : 1920, mode === 'desktop' ? 900 : 1080)
    window.isFullScreen = () => mode === 'fullscreen'
    window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
    window.focus()
  }, mode)
  await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
  await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(entries.length)
}

test.beforeEach(async ({}, info) => {
  mode = info.title.startsWith('fullscreen') ? 'fullscreen' : 'desktop'
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-identity-projections-'))
  errors.length = 0
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('tests/electron/identity-projections-main.mjs'), '--data-dir', directory, '--no-sync'],
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
  await page.getByRole('button', { name: 'Skip setup', exact: true }).click({ timeout: 45000 })
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
})
test.afterEach(async ({}, info) => {
  try {
    if (info.status !== info.expectedStatus && !page.isClosed())
      await info.attach('identity-failure', { body: await page.screenshot(), contentType: 'image/png' })
    expect(await application.evaluate(() => (globalThis as any).__identityProjections.dispatches)).toEqual([])
  } finally {
    await closeFixture(application, directory)
  }
  expect(errors).toEqual([])
})

for (const surface of ['desktop', 'fullscreen'] as const) {
  test(`${surface} expansion linking preserves every count bucket date and independent playtime`, async () => {
    await seed(
      [
        { title: civ, minutes: 12000, days: 900 },
        { title: pack, minutes: 0, days: null, year: 2007 },
      ],
      true,
    )
    const before = await library(),
      buckets = ownBuckets((await workspace()).buckets),
      stored = persistedFacts()
    const visibleCounts = await page.locator('.avalon-buckets button').allTextContents()
    await link(1, [2], 'expansion_of')
    const after = await library(),
      read = await workspace()
    expect(after).toEqual(before)
    expect(counts(after.games)).toEqual(counts(before.games))
    expect(after.games).toHaveLength(2)
    expect(after.games.find((game) => game.workId === 1)?.playtimeMinutes).toBe(12000)
    expect(after.games.find((game) => game.workId === 2)).toMatchObject({
      playtimeMinutes: 0,
      bucket: 'never_played',
    })
    expect(ownBuckets(read.buckets)).toEqual(buckets)
    expect(read.buckets.every((row) => row.workId === row.resolvedWorkId)).toBe(true)
    expect(await liveLinks()).toEqual([
      expect.objectContaining({ childWorkId: 2, parentWorkId: 1, kind: 'expansion_of' }),
    ])
    expect(persistedFacts()).toEqual(stored)
    await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(2)
    await expect(page.locator('.avalon-buckets button')).toHaveText(visibleCounts)
    if (mode === 'desktop') await expect(page.locator('.avalon-library-total strong')).toHaveText('2')
    await screenshot('expansion-counts')
  })
  test(`${surface} expansion hours stay outside same-game coverage and its base headline`, async () => {
    await seed(
      [
        { title: civ, minutes: 12000, days: 900 },
        { title: pack, minutes: 4000, days: 10, year: 2007 },
      ],
      true,
    )
    await link(1, [2], 'expansion_of')
    expect((await library()).games.find((game) => game.workId === 1)).toMatchObject({
      playtimeMinutes: 12000,
      entries: [expect.objectContaining({ workId: 1 })],
    })
    await open(1)
    await expansions()
    const row = details().getByRole('article', { name: pack, exact: true })
    await expect(row).toContainText('66h')
    await expect(details().getByRole('heading', { name: 'Expansions', exact: true })).toBeVisible()
    await expect(
      details().getByRole('heading', { name: 'Related games & editions', exact: true }),
    ).toHaveCount(0)
    await showRelationship(row)
    await screenshot('independent-expansion-hours')
  })
  test(`${surface} same-game coverage and expansions remain separate and the pack names its base`, async () => {
    await seed(
      [
        { title: civ, minutes: 12000, days: 900 },
        { title: civ, minutes: 60, days: 5, store: 'gog' },
        { title: pack, minutes: 0, days: null, year: 2007 },
      ],
      true,
    )
    await link(1, [2])
    await link(1, [3], 'expansion_of')
    const root = (await library()).games.find((game) => game.workId === 1)!
    expect(root.playtimeMinutes).toBe(12060)
    expect(root.entries.map((entry) => entry.workId).sort()).toEqual([1, 2])
    await open(1)
    await section('Library')
    await expect(details().locator('.avalon-copy')).toHaveCount(2)
    await expect(
      details().getByRole('heading', { name: 'Related games & editions', exact: true }),
    ).toBeVisible()
    if (mode === 'desktop') await section('Overview')
    await expansions()
    await expect(details().getByRole('heading', { name: 'Expansions', exact: true })).toBeVisible()
    await expect(details().getByRole('heading', { name: 'Extends', exact: true })).toHaveCount(0)
    await expect(details().getByRole('article', { name: pack, exact: true })).toContainText(
      'Last played never',
    )
    await closeDetails()
    await open(3)
    await expansions()
    await expect(details().getByRole('heading', { name: 'Extends', exact: true })).toBeVisible()
    await expect(details().getByRole('heading', { name: 'Expansions', exact: true })).toHaveCount(0)
    const base = details().getByRole('article', { name: civ, exact: true })
    await expect(base).toContainText(civ)
    await expect(base.getByRole('button', { name: `Separate ${pack}…`, exact: true })).toBeVisible()
    await showRelationship(base)
    await screenshot('pack-base-relationship')
    await activate(base.getByRole('button', { name: `Separate ${pack}…`, exact: true }))
    const confirmation = page.getByRole('dialog', { name: `Separate ${pack} from ${civ}?`, exact: true })
    await expect(confirmation.getByRole('button', { name: 'Keep relationship', exact: true })).toBeFocused()
    // Keep the first Back immediate: visual checks must not mask nested-layer registration races.
    if (mode === 'fullscreen') await tap(1)
    else await page.keyboard.press('Escape')
    expect((await liveLinks()).filter((link) => link.kind === 'expansion_of')[0].childWorkId).toBe(3)
    await expect(base.getByRole('button', { name: `Separate ${pack}…`, exact: true })).toBeFocused()
    await activate(base.getByRole('button', { name: `Separate ${pack}…`, exact: true }))
    await confirmationPresentation(confirmation)
    await screenshot('pack-safe-separation-confirmation')
    if (mode === 'fullscreen') await tap(1)
    else await page.keyboard.press('Escape')
    await expect(base.getByRole('button', { name: `Separate ${pack}…`, exact: true })).toBeFocused()
    await activate(base.getByRole('button', { name: `View ${civ}`, exact: true }))
    await expect(details().locator('h1')).toHaveText(civ)
    await focusedDetailControl()
    if (mode === 'desktop') await section('Overview')
    await expansions()
    await activate(
      details()
        .getByRole('article', { name: pack, exact: true })
        .getByRole('button', { name: `View ${pack}`, exact: true }),
    )
    await expect(details().locator('h1')).toHaveText(pack)
    await focusedDetailControl()
  })
  test(`${surface} reparenting the Steam base under GOG keeps its pack an independent expansion`, async () => {
    await seed(
      [
        { title: civ, minutes: 12000, days: 900 },
        { title: civ, minutes: 60, days: 5, store: 'gog' },
        { title: pack, minutes: 4000, days: 10, year: 2007 },
      ],
      true,
    )
    await link(1, [3], 'expansion_of')
    await link(2, [1])
    const links = await liveLinks(),
      games = (await library()).games
    expect(links).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ childWorkId: 3, parentWorkId: 2, kind: 'expansion_of' }),
        expect.objectContaining({ childWorkId: 1, parentWorkId: 2, kind: 'same_game' }),
      ]),
    )
    expect(links.some((link) => link.childWorkId === 3 && link.kind === 'same_game')).toBe(false)
    expect(games).toHaveLength(2)
    expect(games.find((game) => game.workId === 2)?.playtimeMinutes).toBe(12060)
    expect(games.find((game) => game.workId === 3)?.playtimeMinutes).toBe(4000)
    await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(2)
    await open(2)
    await expansions()
    await expect(details().getByRole('article', { name: pack, exact: true })).toContainText('66h')
  })
  test(`${surface} same-game linking collapses one tile without changing ownership facts or store counts`, async () => {
    await seed([
      { title: 'Prey', minutes: 300, days: 30 },
      { title: 'Prey', minutes: 90, days: 400, store: 'epic' },
      { title: 'Dishonored', minutes: 0, days: null },
    ])
    const before = await library(),
      buckets = ownBuckets((await workspace()).buckets),
      stored = persistedFacts()
    expect(before.games).toHaveLength(3)
    await link(1, [2])
    const after = await library()
    expect(after.games).toHaveLength(2)
    expect(ownBuckets((await workspace()).buckets)).toEqual(buckets)
    expect(persistedFacts()).toEqual(stored)
    expect(counts(after.games).stores).toEqual(counts(before.games).stores)
    expect(counts(after.games).stores).toEqual({ steam: 2, epic: 1 })
    await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(2)
    await expect(page.locator('.avalon-buckets').getByRole('button', { name: /^All games/ })).toContainText(
      '2',
    )
    if (mode === 'desktop') await expect(page.locator('.avalon-library-total strong')).toHaveText('2')
  })
  test(`${surface} linked Prey uses the primary title cover and store chips with exact composite and per-copy dates`, async () => {
    await seed([
      { title: 'Prey', minutes: 300, days: 30 },
      { title: 'Prey (2017)', minutes: 90, days: 40, store: 'epic' },
    ])
    type Art = { current: { previewKey: { provider: string; id: string } } }
    const art = (workId: number) => api<Art>({ route: 'artworkState', params: { workId, slot: 'Cover' } })
    const primary = (await art(1)).current.previewKey,
      other = (await art(2)).current.previewKey
    expect(primary).toEqual({ provider: 'steam', id: '500001', cacheStem: 'steam_500001' })
    expect(other).not.toEqual(primary)
    await expect(tile(1).locator('.artwork')).toHaveAttribute('data-state', 'ready')
    await expect(tile(2).locator('.artwork')).toHaveAttribute('data-state', 'ready')
    const imageHash = async (id: number) =>
      createHash('sha256')
        .update(
          await tile(id)
            .locator('.artwork img')
            .first()
            .evaluate((image) => {
              const canvas = document.createElement('canvas')
              canvas.width = 100
              canvas.height = 150
              canvas.getContext('2d')!.drawImage(image as HTMLImageElement, 0, 0, 100, 150)
              return canvas.toDataURL('image/png')
            }),
        )
        .digest('hex')
    const primaryImage = await imageHash(1)
    expect(await imageHash(2)).not.toBe(primaryImage)
    await link(1, [2])
    await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(1)
    const game = (await library()).games[0]
    expect(game).toMatchObject({
      workId: 1,
      title: 'Prey',
      playtimeMinutes: 390,
    })
    expect(Date.parse(game.lastPlayedAt!)).toBe(Date.parse(date(identityNow, 30)))
    expect(game.entries.map((entry) => entry.store)).toEqual(['steam', 'epic'])
    expect(game.entries.find((entry) => entry.store === 'steam')?.playtimeMinutes).toBe(300)
    const epic = game.entries.find((entry) => entry.store === 'epic')!
    expect(epic.playtimeMinutes).toBe(90)
    expect(Date.parse(epic.lastPlayedAt!)).toBe(Date.parse(date(identityNow, 40)))
    expect((await art(1)).current.previewKey).toEqual(primary)
    expect(await imageHash(1)).toBe(primaryImage)
    await expect(tile(1)).toContainText('Prey')
    await expect(tile(1)).toHaveAttribute('aria-label', /Steam.*Epic|Epic.*Steam/)
    await screenshot('primary-cover-and-stores')
  })
  test(`${surface} covered titles retain their own Steam and Epic playtime and last-played figures`, async () => {
    await seed([
      { title: 'Prey', minutes: 300, days: 30 },
      { title: 'Prey Deluxe', minutes: 90, days: 400, store: 'epic' },
    ])
    await link(1, [2])
    await open(1)
    await section('Library')
    const copies = details().locator('.avalon-copy')
    await expect(copies).toHaveCount(2)
    await expect(
      copies.filter({ has: page.getByRole('heading', { name: 'Prey', exact: true }) }),
    ).toContainText('5h')
    const epic = copies.filter({ has: page.getByRole('heading', { name: 'Prey Deluxe', exact: true }) })
    await expect(epic).toContainText('Epic Games')
    await expect(epic).toContainText('1h')
    await expect(epic).toContainText('Jul 19, 2025')
    await screenshot('covered-title-figures')
    await epic.scrollIntoViewIfNeeded()
    await screenshot('covered-copy-last-played')
  })
  test(`${surface} per-release achievements show Steam 100 percent and Epic 30 percent without a blend`, async () => {
    await seed([
      { title: 'Prey', minutes: 300, days: 30, achievements: [10, 10] },
      { title: 'Prey', minutes: 90, days: 40, store: 'epic', achievements: [10, 3] },
    ])
    await link(1, [2])
    await open(1)
    await section('Library')
    const achieved = details().locator('.detail-achievements')
    await expect(achieved).toContainText('Steam: 10 of 10 unlocked · 100%')
    await expect(achieved).toContainText('Epic Games: 3 of 10 unlocked · 30%')
    await expect(achieved).not.toContainText('65%')
    const facts = await api<GameDetails>({ route: 'game.details', params: { workId: 1 } })
    expect(facts.achievements).toEqual([
      expect.objectContaining({ releaseId: 1, total: 10, unlocked: 10, percentComplete: 100 }),
      expect.objectContaining({ releaseId: 2, total: 10, unlocked: 3, percentComplete: 30 }),
    ])
    await achieved.scrollIntoViewIfNeeded()
    await screenshot('per-release-achievements')
  })
  test(`${surface} unsupported Epic achievements remain distinct from Steam known progress and zero percent`, async () => {
    await seed([
      { title: 'Prey', minutes: 300, days: 30, achievements: [4, 1] },
      { title: 'Prey', minutes: 90, days: 40, store: 'epic' },
    ])
    await link(1, [2])
    await open(1)
    await section('Library')
    const achieved = details().locator('.detail-achievements')
    await expect(achieved).toContainText('Steam: 1 of 4 unlocked · 25%')
    await expect(achieved).toContainText('Epic Games: Not supported')
    await expect(achieved).not.toContainText('0%')
    await achieved.scrollIntoViewIfNeeded()
    await screenshot('unsupported-achievements')
  })
  test(`${surface} standalone Dishonored shows one own copy and no coverage relationships`, async () => {
    await seed([{ title: 'Dishonored', minutes: 40, days: 9 }])
    await open(1)
    await section('Library')
    await expect(details().locator('.avalon-copy')).toHaveCount(1)
    await expect(
      details().getByRole('heading', { name: 'Related games & editions', exact: true }),
    ).toHaveCount(0)
    await expect(details().getByRole('heading', { name: 'Expansions', exact: true })).toHaveCount(0)
    await expect(details().getByText(/summed across/)).toHaveCount(0)
  })
  test(`${surface} Separate retracts only Prey Epic and keeps the GOG sibling and original records`, async () => {
    await seed([
      { title: 'Prey', minutes: 300, days: 30 },
      { title: 'Prey Epic', minutes: 90, days: 40, store: 'epic' },
      { title: 'Prey GOG', minutes: 20, days: 50, store: 'gog' },
    ])
    await link(1, [2, 3])
    const before = await liveLinks(),
      stored = persistedFacts()
    expect(new Set(before.map((link) => link.actId)).size).toBe(1)
    await open(1)
    await section('Library')
    await expect(details().locator('.avalon-copy')).toHaveCount(3)
    const opener = details().getByRole('button', { name: 'Separate Prey Epic…', exact: true })
    await activate(opener)
    const confirmation = page.getByRole('dialog', { name: 'Separate Prey Epic from Prey?', exact: true })
    await expect(confirmation.getByRole('button', { name: 'Keep relationship', exact: true })).toBeFocused()
    await confirmationPresentation(confirmation)
    await screenshot('safe-separation-confirmation')
    if (mode === 'fullscreen') await tap(1)
    else await page.keyboard.press('Escape')
    await expect(opener).toBeFocused()
    expect(await liveLinks()).toEqual(before)
    await activate(opener)
    await activate(confirmation.getByRole('button', { name: 'Separate games', exact: true }))
    await expect(confirmation).toHaveCount(0)
    await expect(details().locator('.avalon-copy')).toHaveCount(2)
    await expect(details().getByRole('button', { name: 'Separate Prey GOG…', exact: true })).toBeVisible()
    expect(await liveLinks()).toEqual([before.find((link) => link.childWorkId === 3)])
    expect(persistedFacts()).toEqual(stored)
    const writes = await application.evaluate(() => (globalThis as any).__identityProjections.mutations)
    expect(writes.filter((write: { method: string }) => write.method === 'DELETE')).toEqual([
      {
        path: '/api/v1/identity/links/2',
        method: 'DELETE',
        body: { expectedLinkId: before.find((link) => link.childWorkId === 2)!.id },
      },
    ])
    await closeDetails()
    await expect(tile(2)).toBeVisible()
    await expect(tile(2)).toContainText('Prey Epic')
    await screenshot('one-child-separated')
  })
  test(`${surface} hidden and account-scoped counterparts retain links but disappear from figures and actions`, async () => {
    await seed(
      [
        { title: civ, minutes: 12000, days: 900 },
        { title: 'Hidden GOG copy', minutes: 60, days: 5, store: 'gog' },
        { title: 'Other account copy', minutes: 900, days: 6 },
        { title: pack, minutes: 4000, days: 10, year: 2007 },
      ],
      true,
    )
    database((db) => {
      db.prepare("INSERT OR REPLACE INTO settings(key,value) VALUES('steam.owned_account_ref','12345')").run()
      db.prepare(
        "INSERT INTO account_inventory_observations(store,account_ref,source,revision,attempted_at,is_complete,observed_at,item_count) VALUES('steam','12345','steam_web_api',1,?,1,?,1)",
      ).run(storedDate(expansionNow), storedDate(expansionNow))
      for (const [id, minutes, days] of [
        [1, 12000, 900],
        [3, 900, 6],
        [4, 4000, 10],
      ])
        db.prepare(
          "INSERT INTO ownership_accounts(ownership_id,account_ref,playtime_minutes,last_played_at,source,first_seen_at,last_seen_at) VALUES(?,?,?,?,'fixture',?,?)",
        ).run(
          id,
          id === 1 ? '12345' : '99999',
          minutes,
          storedDate(date(expansionNow, days)),
          storedDate(expansionNow),
          storedDate(expansionNow),
        )
    })
    await link(1, [2, 3])
    await link(1, [4], 'expansion_of')
    const links = await liveLinks()
    await api({ route: 'hidden.put', body: { workIds: [2], hidden: true } })
    await api({ route: 'connections.visibility.put', body: { ownAccountOnly: true } })
    const games = (await library()).games
    expect(games).toHaveLength(1)
    expect(games[0]).toMatchObject({ workId: 1, playtimeMinutes: 12000 })
    expect(games[0].entries.map((entry) => entry.workId)).toEqual([1])
    expect(await liveLinks()).toEqual(links)
    await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(1)
    await open(1)
    await section('Library')
    await expect(details().locator('.avalon-copy')).toHaveCount(1)
    await expect(
      details().getByRole('heading', { name: 'Related games & editions', exact: true }),
    ).toHaveCount(0)
    await expect(details().getByRole('heading', { name: 'Expansions', exact: true })).toHaveCount(0)
    await expect(details().getByRole('button', { name: /^Separate / })).toHaveCount(0)
    await expect(details().getByRole('button', { name: /^View / })).toHaveCount(0)
    await screenshot('omitted-counterparts')
  })
}
