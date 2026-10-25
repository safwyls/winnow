import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Page,
  type Locator,
} from '@playwright/test'
import electronPath from 'electron'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { DatabaseSync } from 'node:sqlite'
import { closeFixture } from './fixture-cleanup'
import { selectCollection } from './collection-controls'
import { libraryAction, returnToLibrary } from './library-controls'
import { mergeFixture } from '../parity-merge-fixtures'
import type { ApiRequest } from '../../src/shared/bridge'
import type { GameList, LibraryFilter, LibraryResponse, ManualGame } from '../../src/renderer/api/types'

type Mode = 'desktop' | 'fullscreen'
const saved: LibraryFilter = { stores: ['steam'], genreIds: [7], yearFrom: 2000 }
const descriptions = [
  ['All games', 'Every title you own.'],
  ['Patched', 'Games with unread updates after a long break from playing.'],
  ['Never played', 'Games with no recorded playtime or last-played date.'],
  ['Started', "Games you've played beyond a brief trial."],
  ['Invested', "Games you've spent a lot of time playing."],
  ['Derelict', 'Games with evidence of closure, delisting or abandoned development.'],
] as const

async function api<T>(page: Page, input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const response = await window.winnow.request(input)
    if (!response.ok) throw Error(`${input.route}: ${response.status} ${response.message}`)
    return response.data
  }, input) as Promise<T>
}
function database(directory: string, run: (db: DatabaseSync) => void) {
  const db = new DatabaseSync(join(directory, 'winnow.db'))
  try {
    db.exec('PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON')
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
      sequence: Date.now(),
      occurredAt: new Date().toISOString(),
    }),
  )
}
async function surface(application: ElectronApplication, page: Page, mode: Mode) {
  await application.evaluate(({ BrowserWindow }, mode) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setContentSize(mode === 'desktop' ? 1200 : 1920, mode === 'desktop' ? 800 : 1080)
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
async function installPad(page: Page) {
  await page.evaluate(() => {
    const state = ((window as unknown as { labelPad: { pressed: number[] } }).labelPad = {
      pressed: [] as number[],
    })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          id: 'Simulated source parity controller',
          index: 0,
          connected: true,
          mapping: 'standard',
          timestamp: performance.now(),
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, index) => ({
            pressed: state.pressed.includes(index),
            touched: state.pressed.includes(index),
            value: state.pressed.includes(index) ? 1 : 0,
          })),
        },
      ],
    })
  })
}
async function tap(page: Page, index: number) {
  for (const pressed of [[], [index], []])
    await page.evaluate(async (pressed) => {
      ;(window as unknown as { labelPad: { pressed: number[] } }).labelPad.pressed = pressed
      await new Promise<void>((resolve) =>
        requestAnimationFrame(() => requestAnimationFrame(() => resolve())),
      )
    }, pressed)
}
async function openFilters(page: Page, mode: Mode, group?: 'PLATFORM' | 'GENRE') {
  const panel = page.getByRole(mode === 'desktop' ? 'region' : 'dialog', {
    name: 'Library filters',
    exact: true,
  })
  if (!(await panel.isVisible()))
    await page
      .getByRole('button', { name: mode === 'desktop' ? 'Filters' : 'Filter & sort', exact: true })
      .click()
  if (group && mode === 'fullscreen') {
    const back = panel.getByRole('button', { name: 'Back to filters', exact: true })
    if (await back.isVisible()) await back.click()
    await panel.getByRole('button', { name: new RegExp(`^${group} ·`) }).click()
  } else if (group) {
    const summary = panel
      .locator('summary')
      .filter({ hasText: new RegExp(`^${group === 'GENRE' ? 'Genres' : 'Stores'}`) })
    if (!(await summary.evaluate((node) => (node.parentElement as HTMLDetailsElement).open)))
      await summary.click()
  }
  return panel
}
async function apply(page: Page, mode: Mode) {
  if (mode === 'fullscreen') await tap(page, 3)
  else await page.getByRole('button', { name: 'Close filters', exact: true }).click()
  await expect(
    page.getByRole(mode === 'desktop' ? 'region' : 'dialog', { name: 'Library filters', exact: true }),
  ).not.toBeVisible()
}
async function choose(page: Page, mode: Mode, option: Locator) {
  if (mode === 'fullscreen') {
    await option.focus()
    await tap(page, 0)
  } else await option.click()
}
async function seedPair(page: Page, directory: string, secondStore = 'gog') {
  const one = await api<ManualGame>(page, {
    route: 'manual.create',
    body: { title: 'Game 1', firstReleaseYear: 2010 },
  })
  const two = await api<ManualGame>(page, { route: 'manual.create', body: { title: 'Game 2' } })
  database(directory, (db) => {
    const update = db.prepare('UPDATE ownerships SET store=? WHERE id=?')
    update.run('steam', one.ownershipId)
    update.run(secondStore, two.ownershipId)
    db.prepare("INSERT INTO facets(id,kind,slug,name) VALUES(7,'genre','rpg','RPG')").run()
    db.prepare('INSERT INTO work_facets(work_id,facet_id) VALUES(?,7)').run(one.workId)
  })
  return { one, two }
}

for (const mode of ['desktop', 'fullscreen'] as const) {
  test.describe(`${mode} source library labels`, () => {
    let directory: string, application: ElectronApplication, page: Page, errors: string[]
    test.beforeEach(async () => {
      directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-library-labels-'))
      application = await electron.launch({
        executablePath: electronPath as unknown as string,
        args: [resolve('tests/electron/library-labels-main.mjs'), '--data-dir', directory, '--no-sync'],
        env: Object.fromEntries(
          Object.entries(process.env).filter(
            ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
          ),
        ) as Record<string, string>,
        chromiumSandbox: true,
        timeout: 60000,
      })
      page = await application.firstWindow()
      errors = []
      page.on('pageerror', (error) => errors.push(error.message))
      await expect(page.getByRole('button', { name: 'Skip setup', exact: true })).toBeVisible({
        timeout: 45000,
      })
      await page.getByRole('button', { name: 'Skip setup', exact: true }).click()
      await surface(application, page, mode)
      await installPad(page)
    })
    test.afterEach(async ({}, info) => {
      try {
        if (page && !page.isClosed()) await page.screenshot({ path: info.outputPath(`${mode}-result.png`) })
        expect(errors).toEqual([])
      } finally {
        await closeFixture(application, directory)
      }
    })

    test('exposes original collection explanations, Invested and visible fullscreen choice descriptions', async () => {
      await seedPair(page, directory)
      await changed(application)
      await expect(page.locator('[data-avalon-game]')).toHaveCount(2)
      if (mode === 'fullscreen') {
        const panel = await openFilters(page, mode)
        await panel.getByRole('button', { name: /^Collection ·/ }).click()
      }
      for (const [label, description] of descriptions) {
        const button =
          mode === 'fullscreen'
            ? page
                .getByRole('dialog', { name: 'Library filters' })
                .getByRole('button', { name: label, exact: true })
            : page.locator('.avalon-rail button').and(page.locator(`[title="${description}"]`))
        // title and ARIA description replace Avalonia ToolTip/AutomationProperties.HelpText.
        const target = button
        await target.scrollIntoViewIfNeeded()
        await expect(target).toBeVisible()
        await expect(target).toHaveAttribute('title', description)
        await expect(target).toHaveAccessibleDescription(description)
        if (mode === 'fullscreen') {
          await expect(target).toHaveAccessibleName(label)
          await expect(target.getByText(description, { exact: true })).toBeVisible()
        }
        await target.hover()
      }
    })

    test('fits three owned store chips and draws every merge member store without clipping', async ({}, info) => {
      const { one } = await seedPair(page, directory)
      database(directory, (db) => {
        const insert = db.prepare('INSERT INTO ownerships(release_id,store,installed) VALUES(?,?,0)')
        insert.run(one.releaseId, 'epic')
        insert.run(one.releaseId, 'gog')
      })
      await changed(application)
      const game = page.locator(`[data-avalon-game][data-work-id="${one.workId}"]`)
      await expect(game).toHaveAccessibleName(/Owned on Steam, Epic, GOG/)
      if (mode === 'desktop') {
        await page.getByRole('button', { name: 'List view', exact: true }).click()
        const column = game.locator('.avalon-record-stores')
        await expect(column.locator('.avalon-store-chip')).toHaveText(['Steam', 'Epic', 'GOG'])
        const bounds = await column.evaluate((node) => {
          const box = node.getBoundingClientRect(),
            chips = [...node.children].map((chip) => chip.getBoundingClientRect())
          const header = document.querySelector('.avalon-record-header')!.children[2]!.getBoundingClientRect()
          const next = node.nextElementSibling!.getBoundingClientRect()
          return {
            width: box.width,
            headerWidth: header.width,
            sameStart: Math.abs(header.x - box.x),
            clear:
              chips.every((chip) => chip.width > 0 && chip.left >= box.left && chip.right <= box.right - 8) &&
              chips.at(-1)!.right <= next.left,
          }
        })
        expect(bounds.width).toBeGreaterThanOrEqual(131.1)
        expect(bounds.headerWidth).toBe(bounds.width)
        expect(bounds.sameStart).toBeLessThan(1)
        expect(bounds.clear).toBe(true)
      } else {
        await expect(game.locator('.avalon-store-initials > span')).toHaveText(['S', 'E', 'G'])
        for (const chip of await game.locator('.avalon-store-initials > span').all())
          await expect(chip).toBeVisible()
      }
      await page.screenshot({ path: info.outputPath(`${mode}-three-stores.png`) })
      const review = mergeFixture()
      review.workspace.works[0].name =
        'A very long game title that must yield its space to the three owned storefront chips instead of covering them'
      ;(review.workspace.ownerships as unknown[]).push(
        { id: 11, releaseId: 101, store: 'epic' },
        { id: 12, releaseId: 101, store: 'gog' },
      )
      await application.evaluate((_, review) => {
        ;(globalThis as unknown as { __libraryLabelsReview: unknown }).__libraryLabelsReview = review
      }, review)
      const navigation = page.getByRole('navigation', { name: 'Main navigation' })
      if (mode === 'desktop') await navigation.getByRole('button', { name: 'Merges', exact: true }).click()
      else {
        await page.getByRole('button', { name: 'Settings', exact: true }).click()
        await page
          .getByRole('navigation', { name: 'Settings section' })
          .getByRole('button', { name: 'Library', exact: true })
          .click()
        await page.getByRole('button', { name: 'Library tools', exact: true }).click()
        await page.getByRole('button', { name: 'Identity review', exact: true }).click()
      }
      const expected: Record<string, string[]> = {
        '1': ['STEAM', 'EPIC', 'GOG'],
        '2': ['GOG'],
        '3': ['STEAM'],
        '4': ['STEAM'],
      }
      const seen: string[] = []
      async function checkRows(rows: Locator) {
        for (const row of await rows.all()) {
          const id =
            mode === 'desktop'
              ? (await row.locator('[data-merge-row]').getAttribute('data-merge-row'))!.split(':').at(-1)!
              : (await row.getAttribute('data-merge-member'))!
          seen.push(id)
          await expect(row.locator('.merge-store')).toHaveText(expected[id])
          const intact = await row.evaluate((node) => {
            const box = node.getBoundingClientRect(),
              stores = node.querySelector('.merge-row-stores')!.getBoundingClientRect()
            const title = node.querySelector('.merge-row-title')?.getBoundingClientRect()
            return (
              stores.width > 0 &&
              stores.left >= box.left &&
              stores.right <= box.right &&
              (!title || title.right <= stores.left) &&
              [...node.querySelectorAll('.merge-store')].every((chip) => {
                const rect = chip.getBoundingClientRect()
                return rect.width > 0 && rect.right <= stores.right
              })
            )
          })
          expect(intact).toBe(true)
        }
      }
      if (mode === 'desktop') {
        await expect(page.locator('[data-merge-row]')).toHaveCount(4)
        await checkRows(page.locator('.merge-row'))
      } else {
        await expect(page.locator('[data-merge-proposal]')).toHaveCount(2)
        for (const proposal of await page.locator('[data-merge-proposal]').all()) {
          await proposal.click()
          await expect(page.locator('[data-merge-member]')).toHaveCount(2)
          await checkRows(page.locator('[data-merge-member]'))
          await page.screenshot({ path: info.outputPath(`fullscreen-merge-${seen.length}.png`) })
          await page.getByRole('button', { name: 'Back to proposals', exact: true }).click()
        }
      }
      expect(seen.sort()).toEqual(['1', '2', '3', '4'])
    })

    test('adds Xbox after the initial Steam-only snapshot and preserves both residual counts', async () => {
      const { two } = await seedPair(page, directory, 'steam')
      await changed(application)
      await expect(page.locator('[data-avalon-game]')).toHaveCount(2)
      let panel = await openFilters(page, mode)
      await expect(panel.getByRole('button', { name: /^PLATFORM ·/ })).toHaveCount(0)
      await expect(panel.locator('summary').filter({ hasText: /^Stores/ })).toHaveCount(0)
      await apply(page, mode)
      database(directory, (db) => {
        db.prepare("UPDATE ownerships SET store='plugin:xbox' WHERE id=?").run(two.ownershipId)
      })
      await changed(application)
      await expect
        .poll(async () => {
          const snapshot = await api<LibraryResponse>(page, { route: 'library.get' })
          return snapshot.games
            .find((game) => game.workId === two.workId)
            ?.entries.map((entry) => entry.store)
        })
        .toEqual(['plugin:xbox'])
      panel = await openFilters(page, mode, 'PLATFORM')
      const xbox = panel.getByRole(mode === 'desktop' ? 'checkbox' : 'button', {
        name: 'Xbox, 1 matching title',
        exact: true,
      })
      await expect(xbox).toBeEnabled()
      await choose(page, mode, xbox)
      await apply(page, mode)
      await expect(page.locator('[data-avalon-game]')).toHaveCount(1)
      await expect(page.locator('[data-avalon-game]')).toHaveAttribute('data-work-id', String(two.workId))
      panel = await openFilters(page, mode, 'PLATFORM')
      for (const label of ['Xbox', 'Steam'])
        await expect(
          panel.getByRole(mode === 'desktop' ? 'checkbox' : 'button', {
            name: `${label}, 1 matching title`,
            exact: true,
          }),
        ).toBeVisible()
    })

    for (const change of ['account', 'hide', 'remove', 'facets'] as const) {
      test(`keeps current and freshly opened saved rules restrictive after ${change}, then clears only the missing RPG choice`, async () => {
        const { one } = await seedPair(page, directory)
        await changed(application)
        let list = await api<GameList>(page, {
          route: 'list.live',
          body: { name: 'Steam RPGs', filter: saved },
        })
        await changed(application)
        await selectCollection(page, list.id)
        await expect(page.locator('[data-avalon-game]')).toHaveCount(1)
        await expect(page.locator('[data-avalon-game]')).toHaveAttribute('data-work-id', String(one.workId))
        if (change === 'hide')
          await api(page, { route: 'hidden.put', body: { workIds: [one.workId], hidden: true } })
        else
          database(directory, (db) => {
            if (change === 'account') {
              db.prepare(
                "INSERT INTO ownership_accounts(ownership_id,account_ref,playtime_minutes,source,first_seen_at,last_seen_at) VALUES(?,'10002',0,'steam_local',datetime('now'),datetime('now'))",
              ).run(one.ownershipId)
              db.exec(
                "INSERT OR REPLACE INTO settings(key,value) VALUES('steam.owned_account_ref','10001'),('library.account_scope','own'); INSERT INTO account_inventory_observations(store,account_ref,source,revision,attempted_at,is_complete,observed_at,item_count) VALUES('steam','10001','steam_web_api',1,datetime('now'),1,datetime('now'),0)",
              )
            } else if (change === 'remove')
              db.prepare('DELETE FROM ownerships WHERE id=?').run(one.ownershipId)
            else db.prepare('DELETE FROM work_facets WHERE work_id=?').run(one.workId)
          })
        await changed(application)
        await expect(page.locator('[data-avalon-game]')).toHaveCount(0)
        async function unchanged() {
          const snapshot = await api<LibraryResponse>(page, { route: 'library.get' })
          list = snapshot.lists.find((item) => item.id === list.id)!
          expect(list.filter).toMatchObject(saved)
          expect(list.releaseIds).toEqual([])
          await libraryAction(page, 'Remove Steam filter')
          await expect(page.getByRole('button', { name: 'Remove Steam filter', exact: true })).toHaveCount(1)
          await expect(page.getByRole('button', { name: 'Remove RPG filter', exact: true })).toHaveCount(1)
          await expect(
            page.getByRole('button', { name: 'Remove release year filter', exact: true }),
          ).toHaveCount(1)
          await expect(page.getByText('Unsaved rules for Steam RPGs', { exact: true })).toHaveCount(0)
          await returnToLibrary(page)
        }
        await unchanged()
        // A fresh renderer has never observed the vanished choices; persisted rules still constrain it.
        await page.reload()
        await surface(application, page, mode)
        await installPad(page)
        await selectCollection(page, list.id)
        await expect(page.locator('[data-avalon-game]')).toHaveCount(0)
        await unchanged()
        let panel = await openFilters(page, mode, 'GENRE')
        let rpg = panel.getByRole(mode === 'desktop' ? 'checkbox' : 'button', {
          name: 'RPG, 0 matching titles',
          exact: true,
        })
        await expect(rpg).toBeEnabled()
        if (mode === 'desktop') await expect(rpg).toBeChecked()
        else await expect(rpg).toHaveAttribute('aria-pressed', 'true')
        await apply(page, mode)
        // Source calls UpdateLiveList even unchanged; the frontend omits that no-op action.
        list = await api<GameList>(page, {
          route: 'list.filter',
          params: { listId: list.id },
          body: { filter: saved, expectedRevision: list.revision },
        })
        await changed(application)
        await unchanged()
        panel = await openFilters(page, mode, 'GENRE')
        rpg = panel.getByRole(mode === 'desktop' ? 'checkbox' : 'button', {
          name: 'RPG, 0 matching titles',
          exact: true,
        })
        await choose(page, mode, rpg)
        await apply(page, mode)
        await libraryAction(page, 'Remove Steam filter')
        await expect(page.getByRole('button', { name: 'Remove RPG filter', exact: true })).toHaveCount(0)
        await expect(page.getByRole('button', { name: 'Remove Steam filter', exact: true })).toHaveCount(1)
        await expect(
          page.getByRole('button', { name: 'Remove release year filter', exact: true }),
        ).toHaveCount(1)
        await expect(page.locator('[data-avalon-game]')).toHaveCount(change === 'facets' ? 1 : 0)
        await expect(page.getByText('Unsaved rules for Steam RPGs', { exact: true })).toHaveCount(1)
        await returnToLibrary(page)
        const persisted = (await api<LibraryResponse>(page, { route: 'library.get' })).lists.find(
          (item) => item.id === list.id,
        )!
        expect(persisted.filter).toMatchObject(saved)
      })
    }
  })
}
