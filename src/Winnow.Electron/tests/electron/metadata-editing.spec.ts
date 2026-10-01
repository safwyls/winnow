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
import { setLibrarySort } from './library-controls'
import { selectCollection } from './collection-controls'
import type { ApiRequest } from '../../src/shared/bridge'
import type { GameDetails, LibraryResponse, Metadata, Mode } from '../../src/renderer/api/types'

const fixture = resolve(
  '../..',
  '.tmp/task38114-fixture-artifacts/bin/Winnow.Electron.Fixtures/debug/Winnow.Electron.Fixtures.dll',
)
let app: ElectronApplication, page: Page, directory: string, mode: Mode, workId: number
let endpoint: { address: string; token: string }
const errors: string[] = []
const details = () => page.locator('.avalon-details')
const editor = () => page.locator('.metadata-dialog')
const tiles = () => page.locator('.avalon-library [data-avalon-game]')
const tile = (id = workId) => page.locator(`.avalon-library [data-avalon-game="${id}"]`)
const row = (field: string) => editor().locator(`[data-metadata-field="${field}"]`)
const metadata = () => api<Metadata>({ route: 'metadata.get', params: { workId } })
type FixtureState = { rows: Record<string, Record<string, any>[]>; oldSessionId: number | null }
async function api<T>(input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const result = await window.winnow.request(input)
    if (!result.ok) throw Error(`${input.route}: ${result.status} ${result.message}`)
    return result.data
  }, input) as Promise<T>
}
async function control<T>(path: string, body?: unknown): Promise<T> {
  const response = await fetch(new URL(`/__fixture/metadata-editing/${path}`, endpoint.address), {
    method: body === undefined ? 'GET' : 'POST',
    headers: { Authorization: `Bearer ${endpoint.token}`, 'Content-Type': 'application/json' },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    signal: AbortSignal.timeout(45000),
  })
  if (!response.ok) throw Error(`Metadata fixture ${path}: HTTP ${response.status}`)
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}
const state = () => control<FixtureState>('state')
async function tap(button: number) {
  // These are standard Gamepad API frames, not verification of a physical device.
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
async function surface(next = mode) {
  mode = next
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
async function seed(kind = 'prey') {
  const seeded = await control<{ workId: number; oldSessionId: number | null }>('seed', { kind })
  workId = seeded.workId
  await activate(
    page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'Library', exact: true }),
  )
  await expect(tiles()).toHaveCount(kind === 'prey' ? 1 : 2)
  return seeded
}
async function openDetails() {
  await activate(tile())
  await expect(details().locator('h1')).toBeVisible()
}
async function edit() {
  await activate(details().getByRole('button', { name: 'More', exact: true }))
  const action = page.getByRole('button', { name: 'Edit details', exact: true })
  await expect(action).toHaveAttribute('title', 'Edit each field by hand')
  await activate(action)
  await expect(editor()).toBeVisible()
  await expect(row('name')).toHaveCount(1)
}
async function field(field: string, label: string) {
  if (mode === 'fullscreen')
    await activate(
      editor()
        .locator('.metadata-field-menu')
        .getByRole('button', { name: new RegExp(`^${label} ·`) }),
    )
  return row(field)
}
async function save(fieldName: string, label: string, value: string) {
  const target = await field(fieldName, label)
  await target.getByRole('textbox', { name: label, exact: true }).fill(value)
  await activate(target.getByRole('button', { name: `Save ${label.toLowerCase()}`, exact: true }))
  await expect(
    editor()
      .getByRole('status')
      .filter({ hasText: /^Saved\.$/ }),
  ).toBeVisible()
}
async function closeEditor() {
  await activate(editor().getByRole('button', { name: 'Back', exact: true }))
  await expect(editor()).toHaveCount(0)
}
async function closeDetails() {
  if (mode === 'fullscreen') await tap(1)
  else await activate(details().getByRole('button', { name: 'Close game details', exact: true }))
  await expect(details()).toHaveCount(0)
}
async function shot(name: string, subject: Locator) {
  await subject.scrollIntoViewIfNeeded()
  await expect(subject).toBeInViewport()
  await page.screenshot({ path: test.info().outputPath(`${mode}-${name}.png`), animations: 'disabled' })
}
async function stageDrafts(targetMode: Mode, values: Record<string, string>, listId?: number) {
  await surface('desktop')
  await activate(
    page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'Library', exact: true }),
  )
  if (listId) await selectCollection(page, listId)
  await openDetails()
  await edit()
  for (const [field, value] of Object.entries(values))
    await row(field).locator('input:not([type="file"]),textarea').fill(value)
  if (targetMode === 'fullscreen') {
    // The source opens a field page over an existing editor. Stage the other real drafts
    // through desktop, then carry them into the fullscreen editor's one-field presentation.
    await closeEditor()
    await closeDetails()
    await surface(targetMode)
    await activate(
      page
        .getByRole('navigation', { name: 'Main navigation' })
        .getByRole('button', { name: 'Library', exact: true }),
    )
    if (listId) await selectCollection(page, listId)
    await openDetails()
    await edit()
  }
}
async function assertDraft(fieldName: string, label: string, value: string) {
  await field(fieldName, label)
  await expect(row(fieldName).getByRole('textbox', { name: label, exact: true })).toHaveValue(value)
  await expect(row(fieldName).locator('.feature-heading small')).toHaveText('AUTO')
  if (mode === 'fullscreen') await activate(editor().getByRole('button', { name: 'Back', exact: true }))
}
async function yearFilter() {
  await page.getByRole('button', { name: 'Filters', exact: true }).click()
  const filter = page.getByRole('region', { name: 'Library filters', exact: true })
  await filter.getByRole('textbox', { name: 'From this year', exact: true }).fill('2006')
  await filter.getByRole('textbox', { name: 'Up to this year', exact: true }).fill('2006')
  await filter.getByRole('button', { name: 'Close filters', exact: true }).click()
  await expect(tiles()).toHaveCount(1)
}

test.beforeAll(async () => {
  test.setTimeout(120000)
  await promisify(execFile)(
    'dotnet',
    [
      'build',
      resolve('../..', 'tests/Winnow.Electron.Fixtures/Winnow.Electron.Fixtures.csproj'),
      '--artifacts-path',
      resolve('../..', '.tmp/task38114-fixture-artifacts'),
      '--nologo',
      '--verbosity',
      'quiet',
    ],
    { windowsHide: true, timeout: 115000 },
  )
})
test.beforeEach(async ({}, info) => {
  test.setTimeout(120000)
  mode = info.title.startsWith('fullscreen') ? 'fullscreen' : 'desktop'
  const variant = info.title.includes('absent service') ? 'noservice' : 'ordinary'
  directory = await mkdtemp(join(resolve('../..', '.tmp'), `winnow-electron-metadata-editing-${variant}-`))
  errors.length = 0
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('tests/electron/metadata-editing-main.mjs'), '--data-dir', directory, '--no-sync'],
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
  expect((await fetch(new URL('/__fixture/metadata-editing/state', endpoint.address))).status).toBe(401)
  await mkdir(join(directory, 'covers'), { recursive: true })
  const bytes = await app.evaluate(({ nativeImage }) =>
    nativeImage
      .createFromBitmap(Buffer.alloc(600 * 400 * 4, 180), { width: 600, height: 400 })
      .toJPEG(90)
      .toString('base64'),
  )
  await writeFile(join(directory, 'covers', 'igdb-shot_newshot.src.jpg'), Buffer.from(bytes, 'base64'))
  await surface()
})
test.afterEach(async ({}, info) => {
  try {
    if (info.status !== info.expectedStatus && page)
      await info.attach('metadata-editing-failure', {
        body: await page.screenshot(),
        contentType: 'image/png',
      })
    expect(await app.evaluate(() => (globalThis as any).__identityProjections.dispatches)).toEqual([])
  } finally {
    await closeFixture(app, directory)
  }
  expect(errors).toEqual([])
})

for (const targetMode of ['desktop', 'fullscreen'] as const) {
  test(`${targetMode} exact Prey name write retains the original work identity, six sources, drafts and editor`, async () => {
    await seed()
    const before = await state()
    await stageDrafts(targetMode, { summary: 'A half-written sentence.', publisher: 'Human Head Studios' })
    const originalDetails = await details().elementHandle(),
      originalEditor = await editor().elementHandle()
    expect((await metadata()).fields).toHaveLength(6)
    expect((await metadata()).fields.every((value) => value.source == null)).toBe(true)
    await save('name', 'Name', 'Prey (2006)')
    await expect(details().locator('h1')).toHaveText('Prey (2006)')
    expect(await originalDetails!.evaluate((node) => node.isConnected)).toBe(true)
    expect(await originalEditor!.evaluate((node) => node.isConnected)).toBe(true)
    await assertDraft('summary', 'About', 'A half-written sentence.')
    await assertDraft('publisher', 'Publisher', 'Human Head Studios')
    const after = await state()
    expect(after.rows.works[0]).toEqual({ ...before.rows.works[0], name: 'Prey (2006)' })
    for (const table of ['releases', 'ownerships', 'play_records'])
      expect(after.rows[table]).toEqual(before.rows[table])
    const saved = await metadata()
    expect(saved.fields.find((field) => field.field === 'name')).toMatchObject({
      value: 'Prey (2006)',
      source: 'user',
    })
    expect(
      saved.fields.filter((field) => field.field !== 'name').every((value) => value.source == null),
    ).toBe(true)
    await shot('saved-prey-drafts', editor())
    await closeEditor()
    await edit()
    await assertDraft('summary', 'About', 'A half-written sentence.')
    await closeEditor()
    await closeDetails()
    await expect(tile()).toContainText('Prey (2006)')
    expect((await api<LibraryResponse>({ route: 'library.get' })).games[0]).toMatchObject({
      workId,
      title: 'Prey (2006)',
      firstReleaseYear: 2006,
      playtimeMinutes: 120,
    })
  })

  test(`${targetMode} exact Prey publisher saves then returns to automatic without claiming another field`, async () => {
    await seed()
    await openDetails()
    await edit()
    await save('publisher', 'Publisher', 'Human Head Studios')
    await field('publisher', 'Publisher')
    await expect(row('publisher').locator('.feature-heading small')).toHaveText('YOU')
    await activate(row('publisher').getByRole('button', { name: 'Use automatic publisher', exact: true }))
    if (mode === 'fullscreen')
      await activate(
        page
          .getByRole('dialog', { name: 'Reset Publisher?', exact: true })
          .getByRole('button', { name: 'Use automatic publisher', exact: true }),
      )
    await expect(
      editor().getByRole('status').filter({ hasText: 'Publisher returned to automatic.' }),
    ).toBeVisible()
    await field('publisher', 'Publisher')
    await expect(row('publisher').getByRole('textbox', { name: 'Publisher', exact: true })).toHaveValue('')
    await expect(row('publisher').locator('.feature-heading small')).toHaveText('AUTO')
    await expect(
      row('publisher').getByRole('button', { name: 'Use automatic publisher', exact: true }),
    ).toHaveCount(0)
    expect((await state()).rows.works[0].publisher).toBeNull()
    expect((await metadata()).fields.every((field) => field.source == null)).toBe(true)
    await shot('automatic-publisher', row('publisher'))
  })

  test(`${targetMode} exact Alpha Protocol rename immediately follows Name ascending order`, async () => {
    await seed('sort')
    await setLibrarySort(page, 'title')
    await expect(tiles()).toHaveText([/Alpha Protocol/, /Zeno Clash/])
    await openDetails()
    await edit()
    await save('name', 'Name', 'Zzz Protocol')
    await expect(details().locator('h1')).toHaveText('Zzz Protocol')
    await closeEditor()
    await closeDetails()
    await expect(tiles()).toHaveText([/Zeno Clash/, /Zzz Protocol/])
    expect((await state()).rows.works.map((work) => work.name)).toEqual(['Zzz Protocol', 'Zeno Clash'])
  })

  test(`${targetMode} absent service leaves Details readable and removes the metadata action`, async () => {
    await seed()
    await openDetails()
    expect((await metadata()).available).toBe(false)
    await activate(details().getByRole('button', { name: 'More', exact: true }))
    await expect(page.getByRole('button', { name: 'Edit details', exact: true })).toHaveCount(0)
    await expect(editor()).toHaveCount(0)
    await expect(details().locator('h1')).toHaveText('Prey')
    expect((await state()).rows.work_field_sources).toEqual([])
  })

  for (const [fieldName, label, value] of [
    ['summary', 'About', 'A saved summary.'],
    ['publisher', 'Publisher', 'A saved publisher'],
    ['first_release_year', 'Release year', '2017'],
  ] as const)
    test(`${targetMode} exact Game 1 ${fieldName} save refreshes facts and 2006 rules while preserving the title draft`, async () => {
      await seed('refresh')
      await surface('desktop')
      await activate(
        page
          .getByRole('navigation', { name: 'Main navigation' })
          .getByRole('button', { name: 'Library', exact: true }),
      )
      await yearFilter()
      const list = await api<{ id: number }>({
        route: 'list.live',
        body: { name: '2006 only', filter: { yearFrom: 2006, yearTo: 2006 } },
      })
      await stageDrafts(targetMode, { name: 'An unfinished title' }, list.id)
      const originalDetails = await details().elementHandle(),
        originalEditor = await editor().elementHandle()
      await save(fieldName, label, value)
      expect(await originalDetails!.evaluate((node) => node.isConnected)).toBe(true)
      expect(await originalEditor!.evaluate((node) => node.isConnected)).toBe(true)
      await assertDraft('name', 'Name', 'An unfinished title')
      const current = await api<LibraryResponse>({ route: 'library.get' }),
        game = current.games.find((game) => game.workId === workId)!
      expect(game).toMatchObject({
        title: 'Game 1',
        summary: fieldName === 'summary' ? value : 'Original summary',
        publisher: fieldName === 'publisher' ? value : 'Original publisher',
        firstReleaseYear: fieldName === 'first_release_year' ? 2017 : 2006,
      })
      expect(current.lists.find((item) => item.id === list.id)?.filter).toMatchObject({
        yearFrom: 2006,
        yearTo: 2006,
      })
      expect(
        (await metadata()).fields
          .filter((field) => field.field !== fieldName)
          .every((field) => field.source == null),
      ).toBe(true)
      await closeEditor()
      await expect(details().locator('h1')).toHaveText('Game 1')
      await expect(details().locator('.game-summary')).toHaveText(
        fieldName === 'summary' ? value : 'Original summary',
      )
      await expect(details().locator('[data-details-identity-line]')).toHaveText(
        `${fieldName === 'first_release_year' ? '2017' : '2006'} · ${fieldName === 'publisher' ? value : 'Original publisher'}`,
      )
      await shot(`refresh-${fieldName}`, details().locator('h1'))
      await closeDetails()
      await expect(tiles()).toHaveCount(fieldName === 'first_release_year' ? 0 : 1)
      const count = fieldName === 'first_release_year' ? 0 : 1
      if (mode === 'fullscreen') {
        await activate(page.getByRole('button', { name: 'My lists', exact: true }))
        await expect(page.locator(`[data-avalon-list="${list.id}"]`)).toContainText(
          `2006 only · ${count} games`,
        )
      } else
        await expect(page.locator(`[data-avalon-list="${list.id}"]`)).toHaveAttribute(
          'aria-label',
          `2006 only, ${count} ${count === 1 ? 'game' : 'games'}`,
        )
    })

  test(`${targetMode} exact background refresh preserves focused journal and update identities while every read projection advances`, async () => {
    const seeded = await seed('background')
    await openDetails()
    const originalDetails = await details().elementHandle()
    if (mode === 'fullscreen')
      await activate(details().getByRole('button', { name: 'Play history →', exact: true }))
    else await activate(details().getByRole('tab', { name: 'Activity', exact: true }))
    await activate(details().getByRole('button', { name: 'Tracked sessions', exact: true }))
    if (mode === 'fullscreen')
      await activate(details().getByRole('button', { name: 'Back to Overview', exact: true }))
    await activate(details().getByRole('tab', { name: 'Journal', exact: true }))
    const originalEntry = await details().locator('.timeline-entry').elementHandle()
    await activate(details().getByRole('button', { name: 'Edit note', exact: true }))
    const journal = page.getByRole('dialog', { name: 'Remember this session', exact: true }),
      note = journal.getByRole('textbox', { name: 'Your note', exact: true })
    await expect(note).toHaveValue('Old saved note')
    await note.fill('My unfinished note')
    await note.focus()
    const originalNote = await note.elementHandle()
    await control('background', {})
    await expect(details().locator('.timeline-entry')).toHaveCount(2)
    await expect(note).toHaveValue('My unfinished note')
    await expect(note).toBeFocused()
    for (const node of [originalDetails, originalEntry, originalNote])
      expect(await node!.evaluate((node) => node.isConnected)).toBe(true)
    await expect(
      details().getByRole('tab', { name: 'Updates, 1 unread update', exact: true, includeHidden: true }),
    ).toHaveCount(1)
    const fresh = await api<GameDetails>({ route: 'game.details', params: { workId } })
    expect(fresh.journalEntries).toHaveLength(2)
    expect(fresh.journalEntries.find((entry) => entry.sessionId === seeded.oldSessionId)?.note).toBe(
      'Old saved note',
    )
    expect(fresh.ratings).toMatchObject([{ score: 90, ratingCount: 20 }])
    expect(fresh.images).toMatchObject([{ imageIds: 'newshot' }])
    expect(fresh.events).toHaveLength(2)
    const game = (await api<LibraryResponse>({ route: 'library.get' })).games.find(
      (game) => game.workId === workId,
    )!
    expect(game).toMatchObject({ playtimeMinutes: 180, lastPlayedAt: '2026-09-05T12:00:00Z' })
    await shot('focused-journal-refresh', note)
    await activate(journal.getByRole('button', { name: 'Close journal editor', exact: true }))
    await expect(details().getByRole('tab', { name: 'Journal', exact: true })).toHaveAttribute(
      'aria-selected',
      'true',
    )
    if (mode === 'desktop') {
      const rating = details().locator('.reception-line.compact')
      await expect(
        details().getByRole('group', {
          name: 'IGDB user rating: 90 out of 100, from 20 ratings.',
          exact: true,
        }),
      ).toHaveCount(1)
      await expect(rating).toContainText('90')
      await expect(rating).not.toContainText('20')
      await expect(rating.locator('span').first()).toHaveAttribute('title', /20/)
      await activate(details().getByRole('tab', { name: 'Activity', exact: true }))
    } else {
      await activate(details().getByRole('tab', { name: 'Overview', exact: true }))
      await activate(details().getByRole('button', { name: 'Play history →', exact: true }))
    }
    await expect(details().getByRole('button', { name: 'Tracked sessions', exact: true })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
    await expect(details().locator('.activity-tracker')).toContainText('3h played')
    await expect(details().locator('.activity-tracker')).toContainText('1 unread update')
    if (mode === 'fullscreen')
      await activate(details().getByRole('button', { name: 'Back to Overview', exact: true }))
    else await activate(details().getByRole('tab', { name: 'Overview', exact: true }))
    await expect(details().locator('.screenshot-strip button')).toHaveCount(1)
    await expect
      .poll(() =>
        details()
          .locator('.screenshot-strip img')
          .evaluate((image) => (image as HTMLImageElement).naturalWidth),
      )
      .toBeGreaterThan(0)
    if (mode === 'fullscreen') {
      await activate(details().getByRole('button', { name: 'Read more →', exact: true }))
      await expect(details().locator('.reception-line')).toContainText('90')
      await expect(
        details().getByRole('group', {
          name: 'IGDB user rating: 90 out of 100, from 20 ratings.',
          exact: true,
        }),
      ).toHaveCount(1)
      await activate(details().getByRole('button', { name: 'Back to Overview', exact: true }))
    }
    await activate(details().getByRole('tab', { name: 'Updates, 1 unread update', exact: true }))
    const oldUpdate = details()
      .getByRole('article', { name: /^New patch ·/ })
      .getByRole('button', { name: 'Read', exact: true })
    await oldUpdate.focus()
    const originalUpdate = await oldUpdate.elementHandle()
    await control('patch', {})
    await expect(details().getByRole('article', { name: /^Newest patch ·/ })).toBeVisible()
    await expect(oldUpdate).toBeFocused()
    expect(await originalUpdate!.evaluate((node) => node.isConnected)).toBe(true)
    await expect(
      details().getByRole('tab', { name: 'Updates, 2 unread updates', exact: true }),
    ).toHaveAttribute('aria-selected', 'true')
    await shot('focused-update-refresh', oldUpdate)
    expect(
      (await state()).rows.session_notes.find((note) => note.session_id === seeded.oldSessionId)?.note,
    ).toBe('Old saved note')
  })
}
