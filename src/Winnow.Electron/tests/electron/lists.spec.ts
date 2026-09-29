import { selectCollection, expectCollection, expectCollectionNames } from './collection-controls'
import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import type { ApiRequest } from '../../src/shared/bridge'
import type { GameList, LibraryResponse, ManualGame, Mode } from '../../src/renderer/api/types'

let application: ElectronApplication, page: Page, directory: string
const entries: ManualGame[] = [],
  errors: string[] = []
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-lists-'))
  const env = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('.'), '--data-dir', directory, '--no-sync'],
    env,
    chromiumSandbox: true,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.getByRole('button', { name: 'Winnow home' })).toBeVisible()
  await page.getByRole('button', { name: 'Skip setup', exact: true }).click()
  for (const title of ['Hades', 'Celeste', 'Tunic'])
    entries.push(await api<ManualGame>({ route: 'manual.create', body: { title } }))
  await api({
    route: 'preferences.presentation.put',
    params: { preference: 'DefaultSort' },
    body: { value: 'NameAscending' },
  })
})
test.afterAll(async () => closeFixture(application, directory))
async function api<T>(input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const result = await window.winnow.request(input)
    if (!result.ok) throw Error(`${input.route}: ${result.status} ${result.message}`)
    return result.data
  }, input) as Promise<T>
}
async function library(mode: Mode, reload = false) {
  if (reload) await page.reload()
  await application.evaluate(({ BrowserWindow }, mode) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setContentSize(1440, 900)
    window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
  }, mode)
  await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
}
const card = (entry: ManualGame) => page.locator(`.avalon-library [data-avalon-game="${entry.workId}"]`)
const visibleIds = () =>
  page
    .locator('.avalon-library [data-avalon-game]')
    .evaluateAll((nodes) => nodes.map((node) => Number((node as HTMLElement).dataset.avalonGame)))

for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} restores alphabetical list names after a saved rename and renderer reload`, async () => {
    const saved: GameList[] = []
    try {
      for (const name of ['Zebra', 'Middle'])
        saved.push(
          await api<GameList>({ route: 'list.create', body: { name, releaseIds: [entries[0].releaseId] } }),
        )
      saved[1] = await api<GameList>({
        route: 'list.update',
        params: { listId: saved[1].id },
        body: { name: 'Aardvark', expectedRevision: saved[1].revision },
      })
      await library(mode, true)
      await expectCollectionNames(page, ['Aardvark', 'Zebra'])
    } finally {
      for (const list of saved)
        await api({
          route: 'list.delete',
          params: { listId: list.id },
          body: { expectedRevision: list.revision },
        })
    }
  })
  test(`${mode} keeps original manual order and commits moves and removal through a renderer reload`, async () => {
    const [hades, celeste, tunic] = entries
    let list = await api<GameList>({
      route: 'list.create',
      body: { name: `Friday night ${mode}`, releaseIds: entries.map((entry) => entry.releaseId) },
    })
    try {
      await library(mode, true)
      await selectCollection(page, list.id)
      await expect.poll(visibleIds).toEqual(entries.map((entry) => entry.workId))
      await card(hades).focus()
      await expect(page.getByRole('button', { name: 'Move earlier', exact: true })).toBeDisabled()
      await card(tunic).focus()
      await expect(page.getByRole('button', { name: 'Move later', exact: true })).toBeDisabled()
      await page.getByRole('button', { name: 'Move earlier', exact: true }).click()
      await expect.poll(visibleIds).toEqual([hades.workId, tunic.workId, celeste.workId])
      await card(celeste).focus()
      await page.getByRole('button', { name: `Remove from Friday night ${mode}`, exact: true }).click()
      await expect.poll(visibleIds).toEqual([hades.workId, tunic.workId])
      list = (await api<LibraryResponse>({ route: 'library.get' })).lists.find(
        (saved) => saved.id === list.id,
      )!
      expect(list.releaseIds).toEqual([hades.releaseId, tunic.releaseId])
      await library(mode, true)
      await selectCollection(page, list.id)
      await expect.poll(visibleIds).toEqual([hades.workId, tunic.workId])
      expect(
        (await api<LibraryResponse>({ route: 'library.get' })).games.some(
          (game) => game.workId === celeste.workId,
        ),
      ).toBe(true)
    } finally {
      const saved = (await api<LibraryResponse>({ route: 'library.get' })).lists.find(
        (saved) => saved.id === list.id,
      )
      if (saved)
        await api({
          route: 'list.delete',
          params: { listId: saved.id },
          body: { expectedRevision: saved.revision },
        })
    }
    expect(errors).toEqual([])
  })

  test(`${mode} saves and opens a named live cut, discovers a new matching game and restores the rules after reload`, async ({}, info) => {
    let list: GameList | undefined, added: ManualGame | undefined
    try {
      await library(mode, true)
      await page.getByRole('textbox', { name: 'Search games' }).fill('Hades')
      await expect.poll(visibleIds).toEqual([entries[0].workId])
      await page.getByRole('button', { name: 'Save filters as a live list…' }).click()
      const dialog = page.getByRole('dialog', { name: 'Name this live list' })
      await expect(dialog.getByLabel('List name', { exact: true })).toHaveValue('Hades')
      await dialog.getByLabel('List name', { exact: true }).fill(`Every Hades ${mode}`)
      await dialog.getByRole('button', { name: 'Create live list' }).click()
      await expect(dialog).toHaveCount(0)
      list = (await api<LibraryResponse>({ route: 'library.get' })).lists.find(
        (saved) => saved.name === `Every Hades ${mode}`,
      )!
      expect(list.isLive).toBe(true)
      expect(list.releaseIds).toEqual([])
      expect(list.filter?.search).toBe('Hades')
      await expectCollection(page, list.id)
      if (mode === 'desktop') {
        const panel = page.getByRole('region', { name: 'Library filters', exact: true })
        await expect(panel).toBeVisible()
        for (const width of [1200, 1440]) {
          await application.evaluate(
            ({ BrowserWindow }, width) => BrowserWindow.getAllWindows()[0]!.setContentSize(width, 900),
            width,
          )
          await expect
            .poll(async () => {
              const edge = (await panel.boundingBox())!.x
              const controls = await page
                .locator(
                  '.avalon-toolbar button, .avalon-toolbar select, .avalon-toolbar input, .avalon-cut-bar button',
                )
                .all()
              return Promise.all(
                controls.map(async (control) => {
                  const box = await control.boundingBox()
                  return !box || box.x + box.width <= edge
                }),
              )
            })
            .not.toContain(false)
        }
      } else await expect(page.getByRole('dialog', { name: 'Library filters' })).toHaveCount(0)
      await expect(page.getByRole('button', { name: 'Remove search filter' })).toHaveAttribute(
        'data-filter-origin',
        'list',
      )
      added = await api<ManualGame>({ route: 'manual.create', body: { title: 'Hades II' } })
      await expect.poll(visibleIds).toEqual([entries[0].workId, added.workId])
      await page.getByRole('button', { name: 'Winnow home' }).click()
      await expect(page.locator('.avalon-library')).toHaveCount(0)
      await library(mode)
      await expectCollection(page, list.id)
      await expect.poll(visibleIds).toEqual([entries[0].workId, added.workId])
      await page.screenshot({ path: info.outputPath(`${mode}-live-list.png`) })
      await library(mode, true)
      await selectCollection(page, list.id)
      await expect(page.getByRole('textbox', { name: 'Search games' })).toHaveValue('Hades')
      await expect.poll(visibleIds).toEqual([entries[0].workId, added.workId])
      await page.getByRole('button', { name: 'Leave this list' }).click()
      await expect(page.getByRole('textbox', { name: 'Search games' })).toHaveValue('')
      await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(4)
      if (mode === 'desktop') {
        await expect(page.getByRole('region', { name: 'Library filters', exact: true })).toBeVisible()
        await selectCollection(page, list.id)
        await expect.poll(visibleIds).toEqual([entries[0].workId, added.workId])
        await selectCollection(page, list.id)
        await expectCollection(page, 'all')
        await expect(page.getByRole('textbox', { name: 'Search games' })).toHaveValue('')
        await expect(page.getByRole('region', { name: 'Library filters', exact: true })).toBeVisible()
      }
    } finally {
      if (list)
        await api({
          route: 'list.delete',
          params: { listId: list.id },
          body: { expectedRevision: list.revision },
        })
      if (added) await api({ route: 'manual.delete', params: { ownershipId: added.ownershipId } })
    }
    expect(errors).toEqual([])
  })
}

test('desktop independently collapses list sections with the keyboard and retains their state across Home', async () => {
  let manual: GameList | undefined, live: GameList | undefined
  try {
    manual = await api<GameList>({
      route: 'list.create',
      body: { name: 'Rail static', releaseIds: [entries[0].releaseId] },
    })
    live = await api<GameList>({
      route: 'list.live',
      body: { name: 'Rail live', filter: { search: 'Hades' } },
    })
    await library('desktop', true)
    const manualHeader = page.getByRole('button', { name: 'LISTS', exact: true })
    const liveHeader = page.getByRole('button', { name: 'LIVE LISTS', exact: true })
    const manualRow = page.getByRole('button', { name: 'Rail static, 1 game', exact: true })
    const liveRow = page.getByRole('button', { name: 'Rail live, 1 game', exact: true })
    await expect(manualRow).toBeVisible()
    await expect(liveRow).toBeVisible()
    await manualHeader.focus()
    await page.keyboard.press('Enter')
    await expect(manualHeader).toHaveAttribute('aria-expanded', 'false')
    await expect(manualRow).toHaveCount(0)
    await expect(liveRow).toBeVisible()
    await liveHeader.focus()
    await page.keyboard.press('Enter')
    await expect(liveRow).toHaveCount(0)
    await manualHeader.focus()
    await page.keyboard.press('Enter')
    await expect(manualRow).toBeVisible()
    await page.getByRole('button', { name: 'Winnow home' }).click()
    await library('desktop')
    await expect(manualHeader).toHaveAttribute('aria-expanded', 'true')
    await expect(liveHeader).toHaveAttribute('aria-expanded', 'false')
    await expect(manualRow).toBeVisible()
    await expect(liveRow).toHaveCount(0)
    expect(errors).toEqual([])
  } finally {
    for (const list of [manual, live])
      if (list)
        await api({
          route: 'list.delete',
          params: { listId: list.id },
          body: { expectedRevision: list.revision },
        })
  }
})
