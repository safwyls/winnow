import { prebuiltFixture as fixture, prebuiltActivationHelper } from './prebuilt-backend'
import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'

import { mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import { libraryAction, returnToLibrary } from './library-controls'
import type { ApiRequest } from '../../src/shared/bridge'
import type { LibraryResponse, LibraryPreferences } from '../../src/renderer/api/types'
import { avalonFilter } from '../../src/renderer/themes/avalon-data'
import { assertAccessibleControls, assertDirectionalReachability } from './controller-accessibility-helpers'

let application: ElectronApplication | undefined, page: Page, directory: string
let endpoint: { address: string; token: string }
const errors: string[] = []
interface VisibilityState {
  exemptions: number[]
  hidden: number[]
  explicit: string | null
  lifecycleCounts: Record<string, number>
  ownershipCount: number
}

test.beforeEach(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-visibility-'))
  errors.length = 0
  await launch()
})
test.afterEach(async () => {
  await closeFixture(application, directory)
  application = undefined
  expect(errors).toEqual([])
})
async function launch() {
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('.'), '--data-dir', directory, '--no-sync'],
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
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible({ timeout: 45000 })
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
}
async function restart(mode: 'desktop' | 'fullscreen') {
  await closeFixture(application, directory)
  application = undefined
  await launch()
  await surface(mode)
}
async function control<T = void>(path: string, body?: unknown): Promise<T> {
  const response = await fetch(new URL(`/__fixture/visibility/${path}`, endpoint.address), {
    method: body === undefined ? 'GET' : 'POST',
    headers: { Authorization: `Bearer ${endpoint.token}`, 'Content-Type': 'application/json' },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    signal: AbortSignal.timeout(15000),
  })
  if (!response.ok) throw Error(`Visibility fixture ${path}: HTTP ${response.status}`)
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}
async function api<T>(input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const response = await window.winnow.request(input)
    if (!response.ok) throw Error(response.message || `API ${input.route}: ${response.status}`)
    return response.data
  }, input) as Promise<T>
}
async function surface(mode: 'desktop' | 'fullscreen', width = 1920, height = 1080) {
  await application!.evaluate(
    ({ BrowserWindow }, { mode, width, height }) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setContentSize(width, height)
      window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
      window.focus()
    },
    { mode, width, height },
  )
  await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
}
async function navigate(name: 'Library' | 'Settings') {
  if (name === 'Settings') {
    await page.getByRole('button', { name, exact: true }).click()
    return
  }
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name, exact: true })
    .click()
}
async function bucket(name: string) {
  await returnToLibrary(page)
  const direct = page.locator('.avalon-buckets').getByRole('button', { name: new RegExp(`^${name}`) })
  if (await direct.count()) await direct.click()
  else {
    await page.getByRole('button', { name: 'Filter & sort', exact: true }).click()
    const panel = page.getByRole('dialog', { name: 'Library filters', exact: true })
    await panel.getByRole('button', { name: /^Collection ·/ }).click()
    await panel.getByRole('button', { name, exact: true }).click()
    await panel.getByRole('button', { name: 'Apply filters', exact: true }).click()
  }
}
const game = (id: number) => page.locator(`.avalon-library [data-avalon-game="${id}"]`)
async function contextFor(id: number) {
  if (await page.locator('.avalon-shell.fullscreen').count()) {
    await game(id).focus()
    await tap(3)
    await expect(page.getByRole('dialog', { name: 'Library options', exact: true })).toBeVisible()
  } else await game(id).click({ button: 'right' })
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
async function seed(kind: string, count: number) {
  expect((await fetch(new URL('/__fixture/visibility/state', endpoint.address))).status).toBe(401)
  await control('seed', { kind })
  await expect
    .poll(async () => (await api<LibraryResponse>({ route: 'library.get' })).games.length)
    .toBe(count)
  await navigate('Library')
  await expect(page.locator('.avalon-results-count')).toHaveText(`${count} ${count === 1 ? 'game' : 'games'}`)
}
async function hideSelected(count: number) {
  await (await libraryAction(page, count === 1 ? 'Hide game…' : `Hide ${count} games…`)).click()
  const dialog = page.getByRole('dialog', {
    name: count === 1 ? 'Hide this game?' : `Hide ${count} games?`,
    exact: true,
  })
  await expect(dialog).toBeVisible()
  await expect(page.getByRole('dialog')).toHaveCount(1)
  await expect(dialog.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused()
  await dialog.evaluate(async (element) => {
    await Promise.all(element.getAnimations({ subtree: true }).map((animation) => animation.finished))
  })
  await page.screenshot({ path: test.info().outputPath('hide-confirmation.png') })
  const confirm = dialog.getByRole('button', {
    name: count === 1 ? 'Hide game' : `Hide ${count} games`,
    exact: true,
  })
  if (await page.locator('.avalon-shell.fullscreen').count()) {
    await assertAccessibleControls(page, dialog)
    await assertDirectionalReachability(page, dialog, tap)
    await confirm.focus()
    await tap(0)
  } else await confirm.click()
  await expect(dialog).toHaveCount(0)
}
async function expectLibraryFocus() {
  await expect(page.locator(':focus')).toBeVisible()
  await expect(page.locator(':focus')).toBeInViewport()
  await expect
    .poll(() =>
      page.evaluate(() => !!document.activeElement?.matches('[data-avalon-game], [data-library-search]')),
    )
    .toBe(true)
}
async function librarySettings() {
  await navigate('Settings')
  await page
    .getByRole('navigation', { name: 'Settings section' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
}

for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} hiding removes the tile and bucket count then restores the hidden row one at a time`, async ({}, info) => {
    await surface(mode)
    await seed('settings', 2)
    await bucket('Never played')
    await expect(
      page.locator('.avalon-buckets').getByRole('button', { name: /^Never played/ }),
    ).toContainText('2')
    await contextFor(2)
    await hideSelected(1)
    await expect(page.locator('.avalon-results-count')).toHaveText('1 game')
    await expectLibraryFocus()
    await expect(game(2)).toHaveCount(0)
    await expect(game(1)).toBeVisible()
    await expect(
      page.locator('.avalon-buckets').getByRole('button', { name: /^Never played/ }),
    ).toContainText('1')
    await expect(page.locator('.avalon-buckets').getByRole('button', { name: /^All games/ })).toContainText(
      '1',
    )
    expect(await control<VisibilityState>('state')).toMatchObject({ hidden: [2], ownershipCount: 2 })
    await (await libraryAction(page, 'Manage library')).click()
    await page
      .getByRole('navigation', { name: 'Library tools' })
      .getByRole('button', { name: 'Hidden games', exact: true })
      .click()
    const row = page
      .locator('.metadata-row')
      .filter({ has: page.locator('strong').filter({ hasText: /^Hidden$/ }) })
    await expect(row).toHaveCount(1)
    await expect(row).toContainText('1 editions')
    await page.screenshot({ path: info.outputPath(`${mode}-hidden-game.png`) })
    await row.getByRole('button', { name: 'Restore to library', exact: true }).click()
    await expect(page.getByText('No hidden games.', { exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Close tools', exact: true }).click()
    await expect(page.locator('.avalon-results-count')).toHaveText('2 games')
    await expect(game(2)).toBeVisible()
    expect((await control<VisibilityState>('state')).hidden).toEqual([])
  })

  test(`${mode} context Hide acts on every picked game and leaves the unselected game`, async () => {
    await surface(mode)
    await seed('hide-selection', 3)
    await game(1).click({ modifiers: ['Control'] })
    await game(3).click({ modifiers: ['Control'] })
    await contextFor(1)
    await expect(page.getByText('2 selected', { exact: true })).toBeVisible()
    await hideSelected(2)
    await expect(page.locator('.avalon-results-count')).toHaveText('1 game')
    await expectLibraryFocus()
    await expect(game(2)).toBeVisible()
    expect(await control<VisibilityState>('state')).toMatchObject({ hidden: [1, 3], ownershipCount: 3 })
  })

  test(`${mode} explicit preference starts off, leaves unrated games visible and survives fresh backend services`, async ({}, info) => {
    await surface(mode)
    await seed('explicit', 1)
    expect((await api<{ explicitHidden: number }>({ route: 'library.visibility' })).explicitHidden).toBe(0)
    await librarySettings()
    const toggle =
      mode === 'desktop'
        ? page.getByRole('checkbox', { name: 'Show explicit content', exact: true })
        : page.getByRole('switch', { name: 'Explicit content', exact: true })
    await expect(toggle).not.toBeChecked()
    await expect(page.getByText('No titles identified as explicit yet.', { exact: true })).toBeVisible()
    await toggle.click()
    await expect(toggle).toBeChecked()
    await expect.poll(async () => (await control<VisibilityState>('state')).explicit).toBe('true')
    expect((await api<LibraryPreferences>({ route: 'preferences.library.get' })).showExplicitContent).toBe(
      true,
    )
    expect((await api<LibraryResponse>({ route: 'library.get' })).games.map((value) => value.title)).toEqual([
      'Anything',
    ])
    await page.screenshot({ path: info.outputPath(`${mode}-explicit-unrated.png`) })
    await restart(mode)
    await librarySettings()
    await expect(
      mode === 'desktop'
        ? page.getByRole('checkbox', { name: 'Show explicit content', exact: true })
        : page.getByRole('switch', { name: 'Explicit content', exact: true }),
    ).toBeChecked()
    await navigate('Library')
    await expect(page.locator('.avalon-results-count')).toHaveText('1 game')
  })

  test(`${mode} Derelict removal updates every grouped copy and both surfaces, surviving later evidence and new services`, async ({}, info) => {
    await surface(mode)
    await seed('grouped', 2)
    if (mode === 'desktop') {
      await game(1).click({ button: 'right' })
      await expect(page.getByRole('button', { name: 'Remove from Derelict', exact: true })).toHaveCount(0)
      await page.getByRole('button', { name: 'Clear selection', exact: true }).click()
    }
    await bucket('Derelict')
    if (mode === 'desktop')
      await expect(page.getByRole('button', { name: 'Remove from Derelict', exact: true })).not.toBeVisible()
    if (mode === 'fullscreen') {
      await game(1).focus()
      await tap(3)
      await expect(page.getByRole('dialog', { name: 'Library options', exact: true })).toBeVisible()
    } else await game(1).click({ button: 'right' })
    const remove = await libraryAction(page, 'Remove from Derelict')
    await expect(remove).toBeEnabled()
    await remove.focus()
    if (mode === 'fullscreen') await tap(0)
    else await page.keyboard.press('Enter')
    await returnToLibrary(page)
    await expect(page.locator('.avalon-results-count')).toHaveText('1 game')
    await expect(game(2)).toBeVisible()
    await expect(game(1)).toHaveCount(0)
    const restored = (await api<LibraryResponse>({ route: 'library.get' })).games.find(
      (value) => value.workId === 1,
    )!
    expect(restored.bucket).toBe('never_played')
    expect(restored.entries.map((entry) => entry.releaseId).sort()).toEqual([1, 2])
    expect((await control<VisibilityState>('state')).exemptions).toEqual([1, 2])
    await page.screenshot({ path: info.outputPath(`${mode}-derelict-group-removed.png`) })
    await surface(mode === 'desktop' ? 'fullscreen' : 'desktop')
    await navigate('Library')
    await bucket('Derelict')
    await expect(page.locator('.avalon-results-count')).toHaveText('1 game')
    await expect(game(2)).toBeVisible()
    await control('later-evidence', {})
    expect((await control<VisibilityState>('state')).lifecycleCounts).toEqual({ 1: 2, 2: 2, 3: 1 })
    await restart(mode)
    await navigate('Library')
    await bucket('Derelict')
    await expect(page.locator('.avalon-results-count')).toHaveText('1 game')
    expect(
      (await api<LibraryResponse>({ route: 'library.get' })).games.find((value) => value.workId === 1),
    ).toMatchObject({ bucket: 'never_played' })
    expect((await control<VisibilityState>('state')).exemptions).toEqual([1, 2])
  })

  test(`${mode} failed Derelict persistence keeps the grouped game and reports the storage problem`, async () => {
    await surface(mode)
    await seed('grouped', 2)
    await control('fail-exemptions', {})
    await bucket('Derelict')
    await contextFor(1)
    await (await libraryAction(page, 'Remove from Derelict')).click()
    await expect(page.getByRole('alert')).toBeVisible()
    await expect(page.getByRole('alert')).not.toHaveText('')
    expect((await control<VisibilityState>('state')).exemptions).toEqual([])
    const result = await api<LibraryResponse>({ route: 'library.get' })
    expect(result.games.map((value) => value.bucket)).toEqual(['derelict', 'derelict'])
    await returnToLibrary(page)
    await expect(page.locator('.avalon-results-count')).toHaveText('2 games')
    await expect(game(1)).toBeVisible()
  })
}

for (const grid of [true, false])
  for (const derelict of [true, false])
    test(`grid=${grid} bulk ${derelict ? 'Derelict removal' : 'Mark as read'} updates both picked games and preserves the unselected game after reload`, async () => {
      await surface('desktop', 1280, 800)
      await seed(derelict ? 'bulk-derelict' : 'bulk-read', 3)
      await bucket(derelict ? 'Derelict' : 'Patched')
      if (!grid) await page.getByRole('button', { name: 'List view', exact: true }).click()
      await game(1).click({ modifiers: ['Control'] })
      await game(2).click({ modifiers: ['Control'] })
      await game(1).click({ button: 'right' })
      await expect(page.getByText('2 selected', { exact: true })).toBeVisible()
      await page
        .getByRole('button', { name: derelict ? 'Remove from Derelict' : 'Mark as read', exact: true })
        .click()
      await expect(page.locator('.avalon-results-count')).toHaveText('1 game')
      await expect(game(3)).toBeVisible()
      await expect(page.getByRole('group', { name: 'Selected games', exact: true })).not.toBeVisible()
      const response = await api<LibraryResponse>({ route: 'library.get' })
      for (const id of [1, 2])
        expect(response.games.find((value) => value.workId === id)!.bucket).not.toBe(
          derelict ? 'derelict' : 'stale_but_patched',
        )
      if (!derelict) {
        const workspace = await api<{
          buckets: { resolvedWorkId: number; game: { unreadUpdateCount: number } }[]
        }>({ route: 'library.workspace' })
        expect(
          workspace.buckets
            .filter((row) => [1, 2].includes(row.resolvedWorkId))
            .map((row) => row.game.unreadUpdateCount),
        ).toEqual([0, 0])
      }
      await page.reload()
      await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
      await navigate('Library')
      await bucket(derelict ? 'Derelict' : 'Patched')
      await expect(page.locator('.avalon-results-count')).toHaveText('1 game')
      await expect(game(3)).toBeVisible()
    })

for (const [grid, selection, atBottom] of [
  [true, false, false],
  [false, false, false],
  [true, true, false],
  [false, true, false],
  [true, true, true],
  [false, true, true],
] as const)
  test(`hide preserves the 100-game viewport and search resets it: grid=${grid} selection=${selection} bottom=${atBottom}`, async () => {
    await surface('desktop', 1024, 600)
    await seed('scroll', 100)
    if (!grid) await page.getByRole('button', { name: 'List view', exact: true }).click()
    const snapshot = await api<LibraryResponse>({ route: 'library.get' })
    const ordered = avalonFilter(snapshot.games, snapshot.lists, {
      query: '',
      bucket: 'all',
      store: 'all',
      listId: 'all',
      sort: 'dormant',
    })
    const target = ordered[20]!,
      last = ordered.at(-1)!
    const scroll = page.locator('.avalon-library-scroll')
    async function pick(id: number, index: number) {
      await scroll.evaluate((element, index) => {
        element.scrollTop = (element.scrollHeight * index) / 100
      }, index)
      await expect(game(id)).toBeAttached()
      await game(id).click({ modifiers: ['Control'] })
    }
    await pick(target.workId, 20)
    if (selection) await pick(last.workId, 99)
    await scroll.evaluate((element, bottom) => {
      element.scrollTop = bottom ? element.scrollHeight : 600
    }, atBottom)
    await expect.poll(() => scroll.evaluate((element) => element.scrollTop)).toBeGreaterThan(0)
    const before = await scroll.evaluate((element) => element.scrollTop)
    await hideSelected(selection ? 2 : 1)
    await expect(page.locator('.avalon-results-count')).toHaveText(`${selection ? 98 : 99} games`)
    await expectLibraryFocus()
    expect(
      (await api<LibraryResponse>({ route: 'library.get' })).games.some(
        (value) => value.workId === target.workId,
      ),
    ).toBe(false)
    await expect
      .poll(() =>
        scroll.evaluate(
          (element, before) =>
            element.scrollTop - Math.min(before, element.scrollHeight - element.clientHeight),
          before,
        ),
      )
      .toBe(0)
    await page.getByRole('textbox', { name: 'Search games', exact: true }).fill('Game 1')
    await expect.poll(() => scroll.evaluate((element) => element.scrollTop)).toBe(0)
  })
