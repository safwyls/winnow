import { libraryAction } from './library-controls'
import { closeFixture } from './fixture-cleanup'
import { test, expect, _electron as electron, type Page } from '@playwright/test'
import { mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { DatabaseSync } from 'node:sqlite'
import electronPath from 'electron'
import type { ApiRequest } from '../../src/shared/bridge'
import type { ManualGame, LibraryResponse } from '../../src/renderer/api/types'

async function api<T>(page: Page, input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const response = await window.winnow.request(input)
    if (!response.ok) throw Error(`${input.route}: ${response.status}`)
    return response.data
  }, input) as Promise<T>
}
async function controller(page: Page) {
  await page.evaluate(() => {
    const state = { pressed: [] as number[] }
    ;(window as unknown as { manualController: typeof state }).manualController = state
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
            touched: false,
            value: state.pressed.includes(index) ? 1 : 0,
          })),
        },
      ],
    })
  })
  return async (button: number) => {
    for (const value of [[], [button], []])
      await page.evaluate(async (value) => {
        ;(window as unknown as { manualController: { pressed: number[] } }).manualController.pressed = value
        await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
      }, value)
  }
}

for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} Platforms and manual forms preserve counts, modal focus, validation, tracked corrections and confirmed removal`, async ({}, info) => {
    test.setTimeout(90000)
    const directory = await mkdtemp(join(resolve('../..', '.tmp'), `winnow-manual-${mode}-`))
    const application = await electron.launch({
      executablePath: electronPath as unknown as string,
      args: [resolve('.'), '--data-dir', directory, '--no-sync'],
      env: Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ) as Record<string, string>,
      chromiumSandbox: true,
      timeout: 60000,
    })
    const page = await application.firstWindow(),
      errors: string[] = []
    page.on('pageerror', (error) => errors.push(error.message))
    try {
      await expect(page.getByRole('button', { name: 'Skip setup', exact: true })).toBeVisible()
      await page.getByRole('button', { name: 'Skip setup', exact: true }).click()
      await application.evaluate(({ BrowserWindow }, mode) => {
        const window = BrowserWindow.getAllWindows()[0]
        window.setFullScreen(false)
        window.setContentSize(mode === 'desktop' ? 1000 : 1280, mode === 'desktop' ? 720 : 800)
        window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
      }, mode)
      await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
      const tap = await controller(page)
      const nav = page.getByRole('navigation', { name: 'Main navigation' })
      await page.getByRole('button', { name: 'Settings', exact: true }).click()
      await page
        .getByRole('navigation', { name: 'Settings section' })
        .getByRole('button', { name: 'Platforms', exact: true })
        .click()
      await expect(page.getByRole('region', { name: 'Steam connection' })).toBeVisible()
      await page.getByRole('button', { name: 'GOG', exact: true }).click()
      await expect(page.getByRole('region', { name: 'GOG connection' })).toBeVisible()
      await expect(page.getByRole('region', { name: 'Steam connection' })).not.toBeVisible()
      await expect(page.getByText('Not needed', { exact: true })).toBeVisible()
      await page.getByRole('button', { name: 'STEAM', exact: true }).click()
      const methods = page.getByRole('button', { name: 'Which one should I use?' })
      await methods.click()
      await expect(page.getByRole('dialog', { name: 'Ways to connect Steam' })).toBeVisible()
      if (mode === 'fullscreen') await tap(1)
      else await page.keyboard.press('Escape')
      await expect(methods).toBeFocused()
      await page.getByRole('button', { name: 'Import purchase history', exact: true }).click()
      const purchase = page.getByRole('dialog', { name: 'Import Steam purchase history' })
      await expect(purchase.getByLabel('Saved Steam pages')).toBeEnabled()
      await expect(purchase.getByRole('button', { name: 'Capture account pages in Winnow' })).toBeEnabled()
      await purchase.getByRole('button', { name: 'Close', exact: true }).click()
      await page.screenshot({ path: info.outputPath(`${mode}-platforms.png`) })

      await nav.getByRole('button', { name: 'Library', exact: true }).click()
      await (await libraryAction(page, 'Manage library')).click()
      await page.getByRole('button', { name: 'Manual games', exact: true }).click()
      await page.getByRole('button', { name: 'Add a game', exact: true }).click()
      await expect(page.getByLabel('Title', { exact: true })).toBeFocused()
      await page.getByLabel('Title', { exact: true }).fill(' ')
      await page.getByRole('button', { name: 'Save game', exact: true }).click()
      await expect(page.getByText('A title is needed.', { exact: true })).toBeVisible()
      expect(await api<ManualGame[]>(page, { route: 'manual.get' })).toHaveLength(0)
      await page.getByLabel('Title', { exact: true }).fill(`Hand added ${mode}`)
      await page.getByLabel('Release year', { exact: true }).fill('2019')
      await page.getByLabel('Platform', { exact: true }).fill('itch.io')
      await page.getByLabel('Executable path', { exact: true }).fill('C:\\Games\\HandAdded\\game.exe')
      const save = page.getByRole('button', { name: 'Save game', exact: true })
      await save.focus()
      if (mode === 'fullscreen') await tap(0)
      else await page.keyboard.press('Enter')
      await expect(page.locator('.manual-editor')).toHaveCount(0)
      await expect(page.getByRole('button', { name: 'Add a game', exact: true })).toBeFocused()
      const added = (await api<ManualGame[]>(page, { route: 'manual.get' }))[0]
      expect(added).toMatchObject({
        title: `Hand added ${mode}`,
        firstReleaseYear: 2019,
        platformLabel: 'itch.io',
        installPath: 'C:\\Games\\HandAdded',
      })
      expect(
        (await api<LibraryResponse>(page, { route: 'library.get' })).games.some(
          (game) => game.title === added.title,
        ),
      ).toBe(true)
      await page.getByRole('button', { name: 'Edit game', exact: true }).click()
      await page.getByLabel('Title', { exact: true }).fill(`Edited ${mode}`)
      await page.getByLabel('Platform', { exact: true }).fill('Switch')
      await save.click()
      await expect(page.locator('.manual-editor')).toHaveCount(0)
      expect(
        await api<ManualGame>(page, { route: 'manual.detail', params: { ownershipId: added.ownershipId } }),
      ).toMatchObject({ title: `Edited ${mode}`, platformLabel: 'Switch' })
      await page.getByRole('button', { name: 'Remove…', exact: true }).click()
      const remove = page.getByRole('dialog', { name: `Remove “Edited ${mode}”?` })
      await expect(remove.getByRole('button', { name: 'Keep game' })).toBeFocused()
      if (mode === 'fullscreen') await tap(1)
      else await page.keyboard.press('Escape')
      expect(await api<ManualGame[]>(page, { route: 'manual.get' })).toHaveLength(1)
      await page.getByRole('button', { name: 'Remove…', exact: true }).click()
      await remove.getByRole('button', { name: 'Remove entry' }).click()
      await expect(remove).toHaveCount(0)
      await expect(page.getByRole('button', { name: `Edited ${mode}`, exact: true })).toHaveCount(0)
      expect(await api<ManualGame[]>(page, { route: 'manual.get' })).toHaveLength(0)

      for (const [index, scenario] of ['correction', 'legacy', 'store', 'mapping'].entries()) {
        const offset = index * 1000
        const entry = await api<ManualGame>(page, {
          route: 'manual.create',
          body: { title: `Original ${scenario}`, igdbId: 333 + offset, steamAppId: String(123 + offset) },
        })
        await expect(page.getByRole('button', { name: `Original ${scenario}`, exact: true })).toBeVisible()
        const database = new DatabaseSync(join(directory, 'winnow.db'))
        try {
          // The backend may still be publishing the preceding manual.create.
          // Give this fixture-only writer the same chance to wait as normal SQLite clients.
          database.exec('PRAGMA busy_timeout=5000')
          database
            .prepare(
              'UPDATE works SET igdb_id=?, name=?, igdb_mapping_revision=igdb_mapping_revision+1 WHERE id=?',
            )
            .run(444 + offset, `Chosen ${scenario}`, entry.workId)
          const row = page
            .locator('.feature-panel')
            .filter({ has: page.getByRole('button', { name: `Original ${scenario}`, exact: true }) })
          await row.getByRole('button', { name: 'Edit game', exact: true }).click()
          await expect(page.getByLabel('IGDB ID', { exact: true })).toHaveValue(String(444 + offset))
          await expect(page.getByLabel('Title', { exact: true })).toHaveValue(`Chosen ${scenario}`)
          if (scenario === 'legacy')
            database
              .prepare('DELETE FROM manual_entry_identifiers WHERE ownership_id=?')
              .run(entry.ownershipId)
          if (scenario === 'store')
            database
              .prepare("INSERT INTO ownerships(release_id,store,installed) VALUES(?,'steam',0)")
              .run(entry.releaseId)
          if (scenario === 'mapping')
            database
              .prepare('UPDATE works SET igdb_id=?, igdb_mapping_revision=igdb_mapping_revision+1 WHERE id=?')
              .run(555 + offset, entry.workId)
          await page.getByLabel('Title', { exact: true }).fill(`Corrected ${scenario}`)
          await page.getByLabel('Release year', { exact: true }).fill('2020')
          await page.getByLabel('IGDB ID', { exact: true }).fill(String(666 + offset))
          await page.getByLabel('Steam app ID', { exact: true }).fill(String(456 + offset))
          await save.focus()
          if (mode === 'fullscreen') await tap(0)
          else await page.keyboard.press('Enter')
          if (scenario === 'correction') {
            await expect(page.locator('.manual-editor')).toHaveCount(0)
            expect(
              await api<ManualGame>(page, {
                route: 'manual.detail',
                params: { ownershipId: entry.ownershipId },
              }),
            ).toMatchObject({
              title: `Corrected ${scenario}`,
              firstReleaseYear: 2020,
              igdbId: 666 + offset,
              steamAppId: String(456 + offset),
            })
          } else {
            const field = page.getByLabel(scenario === 'mapping' ? 'IGDB ID' : 'Steam app ID', {
              exact: true,
            })
            await expect(field).toHaveAttribute('aria-invalid', 'true')
            await expect(page.getByRole('alert')).toContainText(
              scenario === 'mapping' ? 'Cancel and reopen' : 'Keep it to edit details',
            )
            await expect(page.getByLabel('Title', { exact: true })).toHaveValue(`Corrected ${scenario}`)
            const stored = await api<ManualGame>(page, {
              route: 'manual.detail',
              params: { ownershipId: entry.ownershipId },
            })
            expect(stored.igdbId).toBe((scenario === 'mapping' ? 555 : 444) + offset)
            expect(stored.steamAppId).toBe(String(123 + offset))
            await page.screenshot({ path: info.outputPath(`${mode}-manual-${scenario}.png`) })
            await page.getByRole('button', { name: 'Cancel', exact: true }).click()
          }
        } finally {
          database.close()
        }
        await api(page, { route: 'manual.delete', params: { ownershipId: entry.ownershipId } })
      }
      await page.getByRole('button', { name: 'Settings', exact: true }).click()
      const count = page.locator('#platform-steam .platform-title-count')
      await expect(count).toHaveText('1 game in your library')
      expect(await count.evaluate((element) => getComputedStyle(element).fontVariantNumeric)).toBe(
        'tabular-nums',
      )
      expect(errors).toEqual([])
    } finally {
      await closeFixture(application, directory)
    }
  })
}
