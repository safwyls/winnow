import { prebuiltFixture as fixture, prebuiltActivationHelper } from './prebuilt-backend'
import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Locator,
  type Page,
} from '@playwright/test'
import { mkdir, mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { DatabaseSync } from 'node:sqlite'
import electronPath from 'electron'

import { closeFixture } from './fixture-cleanup'
import { fillLibrarySearch, libraryAction, setLibrarySort } from './library-controls'
import type { ApiRequest } from '../../src/shared/bridge'
import type { LibraryResponse, Mode, Workspace } from '../../src/renderer/api/types'
import type { MergeReview } from '../../src/renderer/features/parity-merge-model'

// The test-only backend runs the frozen edition fixture's external substitutes through the real
// evidence acquirer and sync service. All named preload/API, SQLite and UI mutation paths remain real.

let app: ElectronApplication, page: Page, directory: string, mode: Mode
let endpoint: { address: string; token: string }
const errors: string[] = []
interface EditionState {
  pending: number
  externalIds: { releaseId: number; provider: string; providerId: string }[]
  history: { id: number; childWorkId: number; parentWorkId: number; retractedAt: string | null }[]
  acts: unknown[]
  requested: { source: number; uid: string }[]
}
interface SyncResult {
  linked: number
  result: { eligible: number; protectedOrChanged: number }
  state: EditionState
}
const details = () => page.locator('.avalon-details')
const tiles = () => page.locator('.avalon-library [data-avalon-game]')
const tile = (id: number) => page.locator(`.avalon-library [data-avalon-game="${id}"]`)
const editor = () => page.locator('.metadata-dialog')

async function api<T>(input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const response = await window.winnow.request(input)
    if (!response.ok) throw Error(`${input.route}: ${response.status} ${response.message}`)
    return response.data
  }, input) as Promise<T>
}
async function control<T>(path: string, body?: unknown): Promise<T> {
  const response = await fetch(new URL(`/__fixture/edition/${path}`, endpoint.address), {
    method: body === undefined ? 'GET' : 'POST',
    headers: { Authorization: `Bearer ${endpoint.token}`, 'Content-Type': 'application/json' },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    signal: AbortSignal.timeout(15000),
  })
  if (!response.ok) throw Error(`Edition fixture ${path}: HTTP ${response.status}`)
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}
function database<T>(read: (db: DatabaseSync) => T): T {
  const db = new DatabaseSync(join(directory, 'winnow.db'))
  try {
    db.exec('PRAGMA busy_timeout=5000')
    return read(db)
  } finally {
    db.close()
  }
}
function records() {
  return database((db) => ({
    works: db.prepare('SELECT * FROM works ORDER BY id').all(),
    releases: db.prepare('SELECT * FROM releases ORDER BY id').all(),
    ownerships: db.prepare('SELECT * FROM ownerships ORDER BY id').all(),
    externalIds: db.prepare('SELECT * FROM external_ids ORDER BY release_id').all(),
  }))
}
async function tap(button: number) {
  // Native Electron polls these simulated standard Gamepad API frames; no physical controller is claimed.
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
  await app.evaluate(
    ({ BrowserWindow }, { mode, edition }) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setFullScreen(false)
      window.setContentSize(
        mode === 'desktop' ? (edition ? 1100 : 1000) : 1920,
        mode === 'desktop' ? (edition ? 900 : 850) : 1080,
      )
      window.isFullScreen = () => mode === 'fullscreen'
      window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
      window.focus()
    },
    { mode, edition: test.info().title.includes('acquired native edition') },
  )
  await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
}
async function library() {
  await activate(
    page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'Library', exact: true }),
  )
}
async function queue() {
  await library()
  await activate(await libraryAction(page, 'Manage library'))
  await activate(page.getByRole('button', { name: 'Identity review', exact: true }))
  await expect(page.locator('.merge-queue')).toBeVisible()
}
async function closeQueue() {
  await activate(page.getByRole('button', { name: 'Close tools', exact: true }))
}
async function closeDetails() {
  if (mode === 'fullscreen') await tap(1)
  else await activate(details().getByRole('button', { name: 'Close game details', exact: true }))
  await expect(details()).toHaveCount(0)
}
async function capture(name: string, subject: Locator) {
  await subject.scrollIntoViewIfNeeded()
  await expect(subject).toBeInViewport({ ratio: 1 })
  await page.screenshot({ path: test.info().outputPath(`${mode}-${name}.png`), animations: 'disabled' })
}
async function headerHints(choice: Locator) {
  const hints = choice.locator('.merge-header-hints')
  await expect(hints).toBeInViewport({ ratio: 1 })
  await expect(hints.locator(':scope > span')).toHaveText(['A Choose', 'B Back'])
  for (const button of ['A', 'B']) {
    const glyph = hints.locator(`[data-merge-header-glyph="${button}"] svg`)
    await expect(glyph).toHaveAttribute('viewBox', '8 8 48 48')
    await expect(glyph).toBeInViewport({ ratio: 1 })
  }
}
async function cachedArt(ids: string[]) {
  await mkdir(join(directory, 'covers'), { recursive: true })
  const pixels = await app.evaluate(({ nativeImage }) =>
    nativeImage
      .createFromBitmap(Buffer.alloc(300 * 450 * 4, 70), { width: 300, height: 450 })
      .toJPEG(90)
      .toString('base64'),
  )
  for (const id of ids)
    for (const provider of ['steam', 'steam-hero', 'steam-hero-standard'])
      await writeFile(join(directory, 'covers', `${provider}_${id}.src.jpg`), Buffer.from(pixels, 'base64'))
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
async function automaticCover(workId: number, color: number) {
  const id = `co381110${workId}`
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
  await writeFile(join(directory, 'covers', `igdb_${id}.src.jpg`), Buffer.from(pixels, 'base64'))
  database((db) =>
    db
      .prepare('UPDATE works SET cover_url=? WHERE id=?')
      .run(`https://images.igdb.com/igdb/image/upload/t_cover_big/${id}.jpg`, workId),
  )
}
async function seed(
  rows: { work: string; release?: string; store?: string; year?: number; external?: boolean }[],
  sourceHeader = false,
) {
  await cachedArt(rows.map((_, index) => String(700001 + index)))
  database((db) => {
    db.exec('BEGIN')
    for (const [index, row] of rows.entries()) {
      const id = index + 1,
        store = row.store ?? 'steam'
      db.prepare('INSERT INTO works(id,name,sort_name,first_release_year) VALUES(?,?,?,?)').run(
        id,
        row.work,
        null,
        row.year ?? null,
      )
      db.prepare('INSERT INTO releases(id,work_id,name,platform) VALUES(?,?,?,?)').run(
        id,
        id,
        row.release ?? row.work,
        sourceHeader ? null : 'windows',
      )
      db.prepare('INSERT INTO ownerships(id,release_id,store,installed) VALUES(?,?,?,0)').run(id, id, store)
      if (row.external !== false)
        db.prepare('INSERT INTO external_ids(release_id,provider,provider_id) VALUES(?,?,?)').run(
          id,
          store,
          String(700001 + index),
        )
      if (!sourceHeader)
        db.prepare(
          "INSERT INTO play_records(ownership_id,playtime_minutes,last_played_at,source,observed_at) VALUES(?,0,NULL,'steam_localconfig','2026-09-05 12:00:00')",
        ).run(id)
    }
    db.exec('COMMIT')
  })
  await page.reload()
  await surface()
  await library()
  await expect(tiles()).toHaveCount(rows.length)
}
async function openName(id: number) {
  await activate(tile(id))
  await activate(details().getByRole('button', { name: 'More', exact: true }))
  await activate(page.getByRole('button', { name: 'Edit details', exact: true }))
  if (mode === 'fullscreen') await activate(editor().getByRole('button', { name: /^Name ·/ }))
  await expect(editor().getByRole('textbox', { name: 'Name', exact: true })).toBeVisible()
}
async function saveName(value: string) {
  await editor().getByRole('textbox', { name: 'Name', exact: true }).fill(value)
  await activate(editor().getByRole('button', { name: 'Save name', exact: true }))
  await expect
    .poll(
      async () =>
        (await api<Workspace>({ route: 'library.workspace' })).works.find((work) => work.id === 1)?.name,
    )
    .toBe(value)
}
async function closeEditor() {
  if (mode === 'fullscreen') await expect(editor().getByRole('button', { name: /^Name ·/ })).toBeVisible()
  await activate(editor().getByRole('button', { name: 'Back', exact: true }))
  await expect(editor()).toHaveCount(0)
}

test.beforeEach(async ({}, info) => {
  mode = info.title.startsWith('fullscreen') ? 'fullscreen' : 'desktop'
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-editions-'))
  errors.length = 0
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('tests/electron/identity-projections-main.mjs'), '--data-dir', directory, '--no-sync'],
    env: {
      ...(Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ) as Record<string, string>),
      WINNOW_BACKEND_PATH: fixture,
      WINNOW_ACTIVATION_HELPER_PATH: prebuiltActivationHelper,
    },
    chromiumSandbox: true,
    timeout: 60000,
  })
  page = await app.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible({ timeout: 45000 })
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
  expect((await fetch(new URL('/__fixture/edition/state', endpoint.address))).status).toBe(401)
  await surface()
})
test.afterEach(async ({}, info) => {
  try {
    if (info.status !== info.expectedStatus)
      await info.attach('names-editions-failure', { body: await page.screenshot(), contentType: 'image/png' })
    expect(await app.evaluate(() => (globalThis as any).__identityProjections.dispatches)).toEqual([])
  } finally {
    await closeFixture(app, directory)
  }
  expect(errors).toEqual([])
})

for (const surfaceName of ['desktop', 'fullscreen'] as const) {
  test(`${surfaceName} exact Steam GOG header selection persists and Automatic restores without relinking`, async () => {
    await seed(
      [
        { work: 'Steam title', year: 2011, external: false },
        { work: 'GOG title', store: 'gog', year: 2011, external: false },
      ],
      true,
    )
    const before = await api<MergeReview>({ route: 'identity.get' })
    await api({
      route: 'identity.link',
      body: {
        expectedRevision: before.revision,
        parentWorkId: 1,
        childWorkIds: [2],
        kind: 'same_game',
        relationLabel: null,
        rejectedCandidateIds: [],
        refusedPairs: [],
      },
    })
    const linked = await api<MergeReview>({ route: 'identity.get' }),
      original = records()
    await queue()
    const choose = async (title: string, value: string, label: string) => {
      const card = page.getByRole('article', { name: `${title} saved group`, exact: true })
      if (mode === 'desktop') {
        const selector = card.getByRole('combobox', { name: `Header store for ${title}`, exact: true })
        await selector.focus()
        expect((await selector.boundingBox())!.width).toBeGreaterThanOrEqual(160)
        await selector.selectOption(value)
        await expect(
          page.getByRole('combobox', {
            name: `Header store for ${label === 'Automatic' ? 'Steam title' : 'GOG title'}`,
            exact: true,
          }),
        ).toBeFocused()
      } else {
        await activate(card.getByRole('button'))
        await activate(page.getByRole('dialog').getByRole('button', { name: /^Header store ·/ }))
        const choice = page.getByRole('dialog', { name: `Header store for ${title}`, exact: true })
        await expect(choice.getByRole('button', { name: 'Automatic', exact: true })).toBeVisible()
        await expect(choice.getByRole('button', { name: 'Steam', exact: true })).toBeVisible()
        await expect(choice.getByRole('button', { name: 'GOG', exact: true })).toBeVisible()
        await headerHints(choice)
        await capture('header-options', choice)
        await activate(choice.getByRole('button', { name: label, exact: true }))
        await expect(
          page.getByRole('dialog').getByRole('button', { name: `Header store · ${label}`, exact: true }),
        ).toBeFocused()
        await tap(1)
      }
      await expect(
        page.getByRole('article', {
          name: `${label === 'Automatic' ? 'Steam title' : 'GOG title'} saved group`,
          exact: true,
        }),
      ).toBeVisible()
    }
    await choose('Steam title', 'gog', 'GOG')
    const preferred = await api<LibraryResponse>({ route: 'library.get' })
    expect(preferred.games).toHaveLength(1)
    // The shared renderer projects the saved header over the unchanged canonical API snapshot.
    expect(preferred.games[0]).toMatchObject({ workId: 1, title: 'Steam title', firstReleaseYear: 2011 })
    expect(
      (
        (await api<MergeReview>({ route: 'identity.get' })).workspace.preferredHeaderStores as Record<
          string,
          string | null
        >
      )['1'],
    ).toBe('gog')
    expect((await api<MergeReview>({ route: 'identity.get' })).history).toEqual(linked.history)
    expect(records()).toEqual(original)
    await capture(
      'gog-saved-header',
      page.getByRole('article', { name: 'GOG title saved group', exact: true }),
    )
    await closeQueue()
    await expect(tile(1)).toContainText('GOG title')
    await page.reload()
    await surface()
    await queue()
    await choose('GOG title', '', 'Automatic')
    const reset = await api<MergeReview>({ route: 'identity.get' })
    expect(reset.history).toEqual(linked.history)
    expect((await control<EditionState>('state')).acts).toHaveLength(1)
    expect((reset.workspace.preferredHeaderStores as Record<string, string | null>)['1']).toBeNull()
    expect(records()).toEqual(original)
    await closeQueue()
    await expect(tile(1)).toContainText('Steam title')
    await page.reload()
    await surface()
    await library()
    await expect(tile(1)).toContainText('Steam title')
  })
  test(`${surfaceName} an unowned stale GOG header choice preserves Automatic and offers focused recovery`, async () => {
    await seed(
      [
        { work: 'Steam title', year: 2011, external: false },
        { work: 'GOG title', store: 'gog', year: 2011, external: false },
      ],
      true,
    )
    const before = await api<MergeReview>({ route: 'identity.get' })
    await api({
      route: 'identity.link',
      body: {
        expectedRevision: before.revision,
        parentWorkId: 1,
        childWorkIds: [2],
        kind: 'same_game',
        relationLabel: null,
        rejectedCandidateIds: [],
        refusedPairs: [],
      },
    })
    const linked = await api<MergeReview>({ route: 'identity.get' }),
      acts = (await control<EditionState>('state')).acts
    await queue()
    const card = page.getByRole('article', { name: 'Steam title saved group', exact: true })
    if (mode === 'fullscreen') {
      await activate(card.getByRole('button'))
      await activate(
        page.getByRole('dialog').getByRole('button', { name: 'Header store · Automatic', exact: true }),
      )
      await expect(page.getByRole('dialog').getByRole('button', { name: 'GOG', exact: true })).toBeVisible()
    } else await expect(card.getByRole('combobox').locator('option[value="gog"]')).toHaveCount(1)
    // Ownership can disappear without changing the identity review hash. Keep the already-open
    // choices stale and use the real API refusal; only this disposable fixture mutation bypasses it.
    database((db) => db.prepare('DELETE FROM ownerships WHERE release_id=2').run())
    const unowned = await api<MergeReview>({ route: 'identity.get' }),
      remaining = records()
    expect(unowned.revision).toBe(linked.revision)
    if (mode === 'fullscreen')
      await activate(page.getByRole('dialog').getByRole('button', { name: 'GOG', exact: true }))
    else {
      const select = card.getByRole('combobox')
      await select.focus()
      await select.selectOption('gog')
    }
    const scope =
      mode === 'fullscreen'
        ? page.getByRole('dialog', { name: 'Header store for Steam title', exact: true })
        : page.locator('.merge-queue')
    const recovery = scope.getByRole('button', { name: 'Check saved review', exact: true })
    await expect(scope).toContainText(
      'This group changed. Check the saved review before choosing its header again.',
    )
    await expect(recovery).toBeFocused()
    if (mode === 'fullscreen') {
      await expect(scope.getByRole('button', { name: 'Automatic', exact: true })).toHaveAttribute(
        'aria-pressed',
        'true',
      )
      await expect(scope.getByRole('button', { name: 'GOG', exact: true })).toHaveAttribute(
        'aria-pressed',
        'false',
      )
      await headerHints(scope)
    } else await expect(card.getByRole('combobox')).toHaveValue('')
    const refused = await api<MergeReview>({ route: 'identity.get' })
    expect(refused.revision).toBe(linked.revision)
    expect(refused.workspace.preferredHeaderStores).toEqual(linked.workspace.preferredHeaderStores)
    expect(refused.history).toEqual(linked.history)
    expect((await control<EditionState>('state')).acts).toEqual(acts)
    expect(database((db) => db.prepare('SELECT * FROM group_header_preferences').all())).toEqual([])
    expect(records()).toEqual(remaining)
    await capture('unowned-header-refusal', recovery)
    await activate(recovery)
    await expect(recovery).toHaveCount(0)
    if (mode === 'fullscreen') {
      await expect(scope.getByRole('button', { name: 'GOG', exact: true })).toHaveCount(0)
      await expect(scope.getByRole('button', { name: 'Automatic', exact: true })).toBeFocused()
    } else {
      await expect(card.getByRole('combobox')).toBeEnabled()
      await expect(card.getByRole('combobox').locator('option')).toHaveText(['Automatic', 'Steam'])
    }
  })
  test(`${surfaceName} preferred GOG automatic cover reaches tile and Details while canonical metadata stays Steam`, async () => {
    await seed([
      { work: 'Steam title', year: 2011, external: false },
      { work: 'GOG title', store: 'gog', year: 2012, external: false },
    ])
    await automaticCover(1, 35)
    await automaticCover(2, 170)
    await page.reload()
    await surface()
    await library()
    const steam = await picture(tile(1).locator('img').first()),
      gog = await picture(tile(2).locator('img').first())
    expect(gog).not.toBe(steam)
    const before = await api<MergeReview>({ route: 'identity.get' })
    await api({
      route: 'identity.link',
      body: {
        expectedRevision: before.revision,
        parentWorkId: 1,
        childWorkIds: [2],
        kind: 'same_game',
        relationLabel: null,
        rejectedCandidateIds: [],
        refusedPairs: [],
      },
    })
    await expect(tiles()).toHaveCount(1)
    expect(await picture(tile(1).locator('img').first())).toBe(steam)
    const linked = await api<MergeReview>({ route: 'identity.get' }),
      original = records()
    await api({
      route: 'identity.header',
      body: { expectedRevision: linked.revision, workId: 1, store: 'gog' },
    })
    await expect(tile(1)).toContainText('GOG title')
    await expect.poll(() => picture(tile(1).locator('img').first())).toBe(gog)
    await capture('preferred-cover-tile', tile(1))
    await activate(tile(1))
    await expect(details().locator('h1')).toHaveText('GOG title')
    await expect(details().locator('[data-details-identity-line]')).toHaveText('2011')
    const selectedImage =
      mode === 'desktop'
        ? details().locator('.avalon-detail-cover img')
        : details().locator('[data-key="igdb:co3811102"] img')
    await expect(selectedImage).toBeVisible()
    expect(await picture(selectedImage)).toBe(gog)
    expect(records()).toEqual(original)
    expect((await api<MergeReview>({ route: 'identity.get' })).history).toEqual(linked.history)
    await capture('preferred-cover-details', details().locator('h1'))
  })
  test(`${surfaceName} acquired native edition evidence updates library queue and details and separation prevents relinking`, async () => {
    await cachedArt(['620'])
    await control('seed', {})
    expect((await control<EditionState>('state')).pending).toBe(1)
    await library()
    await expect(tiles()).toHaveCount(2)
    const original = records(),
      first = await control<SyncResult>('sync', {})
    expect(first.linked).toBe(1)
    expect(first.result.eligible).toBe(1)
    expect(first.state.pending).toBe(0)
    expect(first.state.requested).toEqual(
      expect.arrayContaining([
        { source: 1, uid: '620' },
        { source: 26, uid: '442f123b4d884d8ca85236aa30b99a79' },
        { source: 26, uid: '78e2d1ca-9ff2-4179-95d4-f67c1acf3b76' },
      ]),
    )
    expect(first.state.requested.some((request) => request.uid === '7a70b499513441c792b541d53505e0b2')).toBe(
      false,
    )
    await expect(tiles()).toHaveCount(1)
    const merged = await api<LibraryResponse>({ route: 'library.get' })
    expect(merged.games[0].entries.map((entry) => entry.releaseId).sort()).toEqual([1, 2])
    await activate(tile(2))
    await activate(details().getByRole('tab', { name: 'Library', exact: true }))
    await expect(details().locator('.avalon-copy')).toHaveCount(2)
    const epic = details()
      .locator('.avalon-copy')
      .filter({ has: page.getByRole('heading', { name: 'Epic edition', exact: true }) })
    await capture(
      'acquired-edition-details',
      epic.getByRole('heading', { name: 'Epic edition', exact: true }),
    )
    await closeDetails()
    await queue()
    const saved = page.getByRole('article', { name: 'Steam edition saved group', exact: true })
    await expect(saved).toBeVisible()
    await capture('acquired-edition-saved-group', mode === 'fullscreen' ? saved.locator('strong') : saved)
    if (mode === 'fullscreen') await activate(saved.getByRole('button'))
    await activate(
      (mode === 'desktop' ? saved : page.getByRole('dialog')).getByRole('button', {
        name: /^Separate again:/,
      }),
    )
    await expect(page.getByRole('article', { name: 'Steam edition saved group', exact: true })).toHaveCount(0)
    await closeQueue()
    await expect(tiles()).toHaveCount(2)
    const separated = await control<EditionState>('state')
    expect(separated.history.every((link) => link.retractedAt !== null)).toBe(true)
    const second = await control<SyncResult>('sync', {})
    expect(second.linked).toBe(0)
    expect(second.result.protectedOrChanged).toBe(1)
    expect(second.state.externalIds).toHaveLength(2)
    expect(records()).toEqual(original)
    await activate(tile(1))
    await activate(details().getByRole('tab', { name: 'Library', exact: true }))
    await expect(details().locator('.avalon-copy')).toHaveCount(1)
    await expect(
      details().getByRole('heading', { name: 'Related games & editions', exact: true }),
    ).toHaveCount(0)
  })
  test(`${surfaceName} user name reaches tile and headline and Automatic retains text while restoring provisional ownership`, async () => {
    await seed([{ work: 'Automatic Name', release: 'Storefront Title' }])
    await expect(tile(1)).toContainText('Automatic Name')
    await openName(1)
    await saveName('My Own Name')
    await closeEditor()
    await expect(details().locator('h1')).toHaveText('My Own Name')
    await expect(
      details().getByText('Name not yet available. Showing the app id until metadata loads.', {
        exact: true,
      }),
    ).toHaveCount(0)
    await closeDetails()
    await expect(tile(1)).toContainText('My Own Name')
    await page.reload()
    await surface()
    await library()
    await expect(tile(1)).toContainText('My Own Name')
    await openName(1)
    await activate(editor().getByRole('button', { name: 'Use automatic name', exact: true }))
    if (mode === 'fullscreen') {
      const confirm = page.getByRole('dialog', { name: 'Reset Name?', exact: true })
      await expect(confirm.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused()
      await activate(confirm.getByRole('button', { name: 'Use automatic name', exact: true }))
    }
    await expect
      .poll(
        async () =>
          (await api<Workspace>({ route: 'library.workspace' })).works.find((work) => work.id === 1)
            ?.nameIsProvisional,
      )
      .toBe(true)
    await closeEditor()
    await expect(details().locator('h1')).toHaveText('My Own Name')
    await expect(
      details().getByText('Name not yet available. Showing the app id until metadata loads.', {
        exact: true,
      }),
    ).toBeVisible()
    expect(
      database((db) => db.prepare("SELECT * FROM work_field_sources WHERE work_id=1 AND field='name'").all()),
    ).toEqual([])
    await capture('automatic-name-retains-text', details().locator('h1'))
  })
  test(`${surfaceName} user title governs search and title sort without the old storefront alias`, async () => {
    await seed([{ work: 'Automatic Name', release: 'Storefront Title' }, { work: 'Almanac' }])
    await openName(1)
    await saveName('Zenith')
    await closeEditor()
    await closeDetails()
    await setLibrarySort(page, 'title')
    await expect(tiles().locator('.avalon-cover-fallback')).toHaveText(['Almanac', 'Zenith'])
    await fillLibrarySearch(page, 'Zeni')
    await expect(tiles()).toHaveCount(1)
    await expect(tile(1)).toContainText('Zenith')
    await fillLibrarySearch(page, 'Storefront')
    await expect(tiles()).toHaveCount(0)
  })
  test(`${surfaceName} user chosen Prey name survives reload and appears in the real merges queue`, async () => {
    await seed([{ work: 'Prey' }, { work: 'Prey', store: 'gog' }])
    await api({ route: 'identity.refresh', body: {} })
    await openName(1)
    await saveName('Prey (2017)')
    await closeEditor()
    await closeDetails()
    await page.reload()
    await surface()
    await queue()
    await expect(page.locator('.merge-queue')).toContainText('Prey (2017)')
    const review = await api<MergeReview>({ route: 'identity.get' })
    expect(review.workspace.works.find((work) => work.id === 1)?.name).toBe('Prey (2017)')
    expect(
      review.candidates.some(
        (candidate) =>
          (candidate.leftReleaseId === 1 && candidate.rightReleaseId === 2) ||
          (candidate.leftReleaseId === 2 && candidate.rightReleaseId === 1),
      ),
    ).toBe(true)
    await capture('user-name-in-merges', page.locator('.merge-queue article').first())
  })
}
