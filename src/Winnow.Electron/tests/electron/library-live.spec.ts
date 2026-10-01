import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import electronPath from 'electron'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { DatabaseSync } from 'node:sqlite'
import { closeFixture } from './fixture-cleanup'
import type { ApiRequest } from '../../src/shared/bridge'
import type { ManualGame, LibraryResponse, JournalResponse } from '../../src/renderer/api/types'

async function api<T>(page: Page, input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const response = await window.winnow.request(input)
    if (!response.ok) throw Error(`${input.route}: ${response.status} ${response.message}`)
    return response.data
  }, input) as Promise<T>
}
async function launch(directory: string, client = 'one') {
  const application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('tests/electron/library-live-main.mjs'), '--data-dir', directory, '--no-sync'],
    env: {
      ...Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ),
      WINNOW_FIXTURE_CLIENT: client,
    } as Record<string, string>,
    chromiumSandbox: true,
    timeout: 60000,
  })
  const page = await application.firstWindow()
  const errors: string[] = []
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(
    page
      .getByRole('button', { name: 'Skip setup', exact: true })
      .or(page.getByRole('button', { name: 'Winnow home', exact: true })),
  ).toBeVisible({ timeout: 45000 })
  if (await page.getByRole('button', { name: 'Skip setup', exact: true }).isVisible())
    await page.getByRole('button', { name: 'Skip setup', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
  return { application, page, errors }
}
async function surface(application: ElectronApplication, page: Page, mode: 'desktop' | 'fullscreen') {
  await application.evaluate(({ BrowserWindow }, mode) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setContentSize(1920, 1080)
    window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
    window.focus()
  }, mode)
  await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
}
function database(directory: string, run: (db: DatabaseSync) => void) {
  const db = new DatabaseSync(join(directory, 'winnow.db'))
  try {
    db.exec('PRAGMA busy_timeout=5000')
    run(db)
  } finally {
    db.close()
  }
}
async function changed(application: ElectronApplication) {
  await application.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0]!.webContents.send('winnow:event', {
      kind: 'library.changed',
      epoch: 'fixture-refresh',
      sequence: 1,
      occurredAt: new Date().toISOString(),
    }),
  )
}

test('independent desktop and fullscreen API clients retain Alpha and Beta searches through committed edits', async ({}, info) => {
  const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-library-live-'))
  let first: Awaited<ReturnType<typeof launch>> | undefined, second: typeof first
  try {
    first = await launch(directory, 'desktop')
    second = await launch(directory, 'fullscreen')
    await surface(first.application, first.page, 'desktop')
    await surface(second.application, second.page, 'fullscreen')
    const desktopSearch = first.page.locator('[data-library-search]')
    await desktopSearch.fill('Alpha')
    await second.page.getByRole('button', { name: 'Search library', exact: true }).click()
    const fullscreenSearch = second.page.getByRole('searchbox', { name: 'Search games' })
    await fullscreenSearch.fill('Beta')
    const alpha = await api<ManualGame>(first.page, { route: 'manual.create', body: { title: 'Alpha' } })
    const beta = await api<ManualGame>(second.page, { route: 'manual.create', body: { title: 'Beta' } })
    await expect(first.page.getByRole('button', { name: /^View Alpha/ })).toBeVisible()
    await expect(second.page.getByRole('button', { name: /^View Beta/ })).toBeVisible()
    await expect(desktopSearch).toHaveValue('Alpha')
    await expect(fullscreenSearch).toHaveValue('Beta')
    await desktopSearch.fill('')
    await fullscreenSearch.fill('')
    for (const { page } of [first, second]) {
      await expect(page.locator('[data-avalon-game]')).toHaveCount(2)
      await expect(page.getByRole('button', { name: /^View Alpha/ })).toBeVisible()
      await expect(page.getByRole('button', { name: /^View Beta/ })).toBeVisible()
    }
    await desktopSearch.fill('Alpha')
    await fullscreenSearch.fill('Beta')
    await api(second.page, { route: 'hidden.put', body: { workIds: [alpha.workId], hidden: true } })
    await expect(first.page.locator('[data-avalon-game]')).toHaveCount(0)
    await expect(second.page.getByRole('button', { name: /^View Beta/ })).toBeVisible()
    await expect(first.page.locator('.avalon-library-total strong')).toHaveText('1')
    await api(first.page, {
      route: 'manual.update',
      params: { ownershipId: beta.ownershipId },
      body: {
        title: 'Gamma',
        expectedRevision: beta.revision,
        expectedIgdbMappingRevision: beta.igdbMappingRevision,
      },
    })
    await expect(second.page.locator('[data-avalon-game]')).toHaveCount(0)
    await expect(desktopSearch).toHaveValue('Alpha')
    await expect(fullscreenSearch).toHaveValue('Beta')
    // Independent caches must both contain only the committed renamed game.
    await desktopSearch.fill('')
    await fullscreenSearch.fill('')
    for (const { page, errors } of [first, second]) {
      await expect(page.locator('[data-avalon-game]')).toHaveCount(1)
      await expect(page.getByRole('button', { name: /^View Gamma/ })).toBeVisible()
      expect(errors).toEqual([])
    }
    await first.page.screenshot({ path: info.outputPath('desktop-live-library.png') })
    await second.page.screenshot({ path: info.outputPath('fullscreen-live-search.png') })
  } finally {
    if (second) await closeFixture(second.application)
    await closeFixture(first?.application, directory)
  }
})

for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} API client resynchronizes its open library after a real backend restart`, async ({}, info) => {
    const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-library-restart-'))
    const { application, page, errors } = await launch(directory)
    try {
      await surface(application, page, mode)
      await api(page, { route: 'manual.create', body: { title: 'Before restart' } })
      await expect(page.getByRole('button', { name: /^View Before restart/ })).toBeVisible()
      const epoch = await page.evaluate(async () => (await window.winnow.connection()).epoch)
      await page.evaluate(() => window.winnow.restartBackend!())
      await expect
        .poll(
          () =>
            page.evaluate(async (epoch) => {
              const state = await window.winnow.connection()
              return !!state.connected && !!state.epoch && state.epoch !== epoch
            }, epoch),
          { timeout: 30000 },
        )
        .toBe(true)
      await api(page, { route: 'manual.create', body: { title: 'After restart' } })
      await expect(page.locator('[data-avalon-game]')).toHaveCount(2)
      await expect(page.getByRole('button', { name: /^View Before restart/ })).toBeVisible()
      await expect(page.getByRole('button', { name: /^View After restart/ })).toBeVisible()
      await expect(page.locator('.connection-banner')).toHaveCount(0)
      await page.screenshot({ path: info.outputPath(`${mode}-reconnected-library.png`) })
      expect(errors).toEqual([])
    } finally {
      await closeFixture(application, directory)
    }
  })

  test(`${mode} Activity journal and Spending use API-only services with a real twenty-minute manual session`, async ({}, info) => {
    const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-library-journal-'))
    const { application, page, errors } = await launch(directory)
    try {
      const game = await api<ManualGame>(page, { route: 'manual.create', body: { title: 'Journal game' } })
      let sessionId = 0
      database(directory, (db) => {
        sessionId = Number(
          db
            .prepare(
              'INSERT INTO sessions(ownership_id,started_at,ended_at,duration_s,detection_method) VALUES(?,?,?,1200,?)',
            )
            .run(
              game.ownershipId,
              new Date(Date.now() - 1200000).toISOString().replace('T', ' ').replace('Z', ''),
              new Date().toISOString().replace('T', ' ').replace('Z', ''),
              'manual',
            ).lastInsertRowid,
        )
      })
      await page.reload()
      await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
      await surface(application, page, mode)
      await page
        .getByRole('navigation', { name: 'Main navigation' })
        .getByRole('button', { name: /Activity|Journal/, exact: true })
        .click()
      await expect(page.getByRole('heading', { name: 'Journal game', exact: true })).toBeVisible()
      await page.getByRole('button', { name: 'Add selected note', exact: true }).click()
      const dialog = page.getByRole('dialog', { name: 'Remember this session' })
      await dialog.getByRole('textbox', { name: 'Your note', exact: true }).fill('Saved through the API')
      await dialog.getByRole('button', { name: 'Save note', exact: true }).click()
      await expect(dialog).toHaveCount(0)
      expect((await api<JournalResponse>(page, { route: 'journal.get', params: { sessionId } })).note).toBe(
        'Saved through the API',
      )
      await page.getByRole('button', { name: 'Settings', exact: true }).click()
      if (mode === 'fullscreen')
        await page
          .getByRole('navigation', { name: 'Settings section' })
          .getByRole('button', { name: 'Library', exact: true })
          .click()
      await page.getByRole('button', { name: /^Spending/ }).click()
      await expect(
        page.getByText('Spending and licences from your captured Steam account pages.'),
      ).toBeVisible()
      await expect(page.getByText('Loading captured spending…')).toHaveCount(0)
      await expect(page.getByText(/^No Steam spending has been captured\./)).toBeVisible()
      await expect(page.locator('[role="alert"]')).toHaveCount(0)
      await page.screenshot({ path: info.outputPath(`${mode}-api-spending.png`) })
      expect(errors).toEqual([])
    } finally {
      await closeFixture(application, directory)
    }
  })
}

function setPreference(db: DatabaseSync, key: string, value: string) {
  db.prepare(
    'INSERT INTO settings(key,value) VALUES(?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value',
  ).run(key, value)
}
async function seedPair(page: Page, directory: string, preference: string) {
  const first = await api<ManualGame>(page, { route: 'manual.create', body: { title: 'Game 1' } })
  const second = await api<ManualGame>(page, { route: 'manual.create', body: { title: 'Game 2' } })
  database(directory, (db) => {
    db.prepare("UPDATE ownerships SET store='steam' WHERE id IN (?,?)").run(
      first.ownershipId,
      second.ownershipId,
    )
    if (preference === 'non-game') {
      db.prepare("UPDATE works SET steam_app_type='tool' WHERE id=?").run(second.workId)
      setPreference(db, 'library.show_non_game_entries', 'true')
    } else if (preference === 'maturity') {
      db.prepare(
        "INSERT INTO work_maturity(work_id,source,ratings,descriptors,observed_at) VALUES(?,'steam_store','esrb:m','',datetime('now'))",
      ).run(second.workId)
    } else {
      const membership = db.prepare(
        "INSERT INTO ownership_accounts(ownership_id,account_ref,playtime_minutes,source,first_seen_at,last_seen_at) VALUES(?,?,0,'steam_local',datetime('now'),datetime('now'))",
      )
      membership.run(first.ownershipId, '10001')
      membership.run(second.ownershipId, '10002')
      setPreference(db, 'steam.owned_account_ref', '10001')
      db.exec(
        "INSERT INTO account_inventory_observations(store,account_ref,source,revision,attempted_at,is_complete,observed_at,item_count) VALUES('steam','10001','steam_web_api',1,datetime('now'),1,datetime('now'),1)",
      )
    }
  })
  return { first, second }
}
function restrict(directory: string, preference: string) {
  database(directory, (db) =>
    setPreference(
      db,
      preference === 'non-game'
        ? 'library.show_non_game_entries'
        : preference === 'maturity'
          ? 'library.maturity_cap'
          : 'library.account_scope',
      preference === 'non-game' ? 'false' : preference === 'maturity' ? 'everyone' : 'own',
    ),
  )
}
for (const mode of ['desktop', 'fullscreen'] as const) {
  for (const preference of ['maturity', 'account', 'non-game'])
    test(`${mode} delayed library cannot replace the committed ${preference} and open metadata editor snapshot`, async ({}, info) => {
      const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-library-order-'))
      const { application, page, errors } = await launch(directory)
      try {
        const { first } = await seedPair(page, directory, preference)
        await page.reload()
        await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
        await surface(application, page, mode)
        await expect(page.locator('[data-avalon-game]')).toHaveCount(2)
        await page.getByRole('button', { name: /^View Game 1/ }).click()
        const details = page.locator('.avalon-details')
        await details.evaluate((node) => node.setAttribute('data-retained-details', 'yes'))
        await details.getByRole('button', { name: 'More', exact: true }).click()
        await page.getByRole('button', { name: 'Edit details', exact: true }).click()
        const editor = page.locator('.metadata-dialog')
        await expect(editor).toBeVisible()
        if (mode === 'fullscreen')
          await editor
            .locator('.metadata-field-menu')
            .getByRole('button', { name: /^About/ })
            .click()
        const about = editor.getByRole('textbox', { name: 'About', exact: true })
        await about.fill('Current summary')
        await application.evaluate(() =>
          Object.assign((globalThis as any).__libraryFixture, { hold: true, captured: false }),
        )
        await changed(application)
        await expect
          .poll(() => application.evaluate(() => (globalThis as any).__libraryFixture.captured))
          .toBe(true)
        // As in the source fixture, a stored restriction precedes the metadata commit.
        restrict(directory, preference)
        await editor.getByRole('button', { name: 'Save about', exact: true }).click()
        await expect
          .poll(() => application.evaluate(() => (globalThis as any).__libraryFixture.writes.length))
          .toBe(1)
        const winning = async () => {
          const library = await api<LibraryResponse>(page, { route: 'library.get' })
          expect(library.games.map((game) => game.workId)).toEqual([first.workId])
          expect(library.games[0].summary).toBe('Current summary')
          await expect(details).toHaveAttribute('data-retained-details', 'yes')
          await expect(editor.locator('[data-metadata-field="summary"] textarea')).toHaveValue(
            'Current summary',
          )
          if (mode === 'fullscreen')
            await expect(
              editor
                .locator('.metadata-field-menu')
                .getByRole('button', { name: 'About · YOU', exact: true }),
            ).toBeVisible()
          await expect(details.locator('.game-summary').first()).toHaveText('Current summary')
          if (mode === 'desktop') await expect(page.locator('.avalon-library-total strong')).toHaveText('1')
        }
        await expect
          .poll(() =>
            application.evaluate(() =>
              (globalThis as any).__libraryFixture.libraries
                .at(-1)
                ?.games.map((game: any) => [game.workId, game.summary]),
            ),
          )
          .toEqual([[first.workId, 'Current summary']])
        await winning()
        await application.evaluate(() => (globalThis as any).__libraryFixture.release())
        await expect
          .poll(() => application.evaluate(() => (globalThis as any).__libraryFixture.released))
          .toBe(true)
        await winning()
        await editor.getByRole('button', { name: 'Back', exact: true }).click()
        await expect(editor).toHaveCount(0)
        await expect(details.getByText('Current summary', { exact: true }).first()).toBeVisible()
        await page.screenshot({ path: info.outputPath(`${mode}-${preference}-retained-details.png`) })
        await page.keyboard.press('Escape')
        await expect(page.locator('[data-avalon-game]')).toHaveCount(1)
        await expect(page.locator(`[data-avalon-game="${first.workId}"]`)).toHaveAttribute(
          'data-selected',
          'true',
        )
        expect(errors).toEqual([])
      } finally {
        await application.evaluate(() => (globalThis as any).__libraryFixture.release?.()).catch(() => {})
        await closeFixture(application, directory)
      }
    })
}
