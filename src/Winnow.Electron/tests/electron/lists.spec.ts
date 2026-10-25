import { selectCollection, expectCollection, expectCollectionNames } from './collection-controls'
import { libraryAction, fillLibrarySearch, expectLibrarySearch, returnToLibrary } from './library-controls'
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
  await page.getByRole('button', { name: 'Skip setup', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Winnow home' })).toBeVisible()
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

test('fullscreen options rename the open list and keep deletion cancellable before removing only the list', async () => {
  const list = await api<GameList>({
    route: 'list.create',
    body: { name: 'Weekend', releaseIds: entries.map((entry) => entry.releaseId) },
  })
  try {
    await library('fullscreen', true)
    await selectCollection(page, list.id)
    await selectCollection(page, list.id)
    await expectCollection(page, list.id)
    await (await libraryAction(page, 'Rename list')).click()
    await page.getByLabel('List name', { exact: true }).fill('Quiet weekend')
    await page.getByRole('button', { name: 'Save list', exact: true }).click()
    await expect(page.getByRole('dialog')).toHaveCount(0)
    await expect(page.getByRole('heading', { name: 'Quiet weekend', exact: true })).toBeVisible()
    await expect(card(entries[0])).toBeFocused()
    await (await libraryAction(page, 'Delete list')).click()
    const confirmation = page.getByRole('dialog', { name: 'Delete Quiet weekend?', exact: true })
    await expect(confirmation.getByRole('button', { name: 'Keep list' })).toBeFocused()
    await confirmation.getByRole('button', { name: 'Keep list' }).click()
    await expect(confirmation).toHaveCount(0)
    expect(
      (await api<LibraryResponse>({ route: 'library.get' })).lists.some((saved) => saved.id === list.id),
    ).toBe(true)
    await (await libraryAction(page, 'Delete list')).click()
    await confirmation.getByRole('button', { name: 'Delete list', exact: true }).click()
    await expect(page.getByRole('dialog')).toHaveCount(0)
    await expectCollection(page, 'all')
    await expect.poll(visibleIds).toHaveLength(3)
    expect(
      (await api<LibraryResponse>({ route: 'library.get' })).lists.some((saved) => saved.id === list.id),
    ).toBe(false)
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
      await expect(await libraryAction(page, 'Move earlier')).toBeDisabled()
      await returnToLibrary(page)
      await card(tunic).focus()
      await expect(await libraryAction(page, 'Move later')).toBeDisabled()
      await (await libraryAction(page, 'Move earlier')).click()
      await expect.poll(visibleIds).toEqual([hades.workId, tunic.workId, celeste.workId])
      await card(celeste).focus()
      await (await libraryAction(page, `Remove from Friday night ${mode}`)).click()
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
      await fillLibrarySearch(page, 'Hades')
      await expect.poll(visibleIds).toEqual([entries[0].workId])
      await (await libraryAction(page, 'Save filters as a live list…')).click()
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
      await expect(await libraryAction(page, 'Remove search filter')).toHaveAttribute(
        'data-filter-origin',
        'list',
      )
      await returnToLibrary(page)
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
      await expectLibrarySearch(page, 'Hades')
      await expect.poll(visibleIds).toEqual([entries[0].workId, added.workId])
      await (await libraryAction(page, 'Leave this list')).click()
      await expectLibrarySearch(page, '')
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

test('desktop rail footer stays reachable in a short window and its keyboard menu names either list with focus restoration', async ({}, info) => {
  const saved: GameList[] = []
  try {
    for (let index = 0; index < 24; index++)
      saved.push(
        await api<GameList>({
          route: 'list.create',
          body: { name: `Long collection ${index}: an evening with friends`, releaseIds: [] },
        }),
      )
    await library('desktop', true)
    const trigger = page.getByRole('button', { name: 'New list', exact: true })
    const settings = page.getByRole('button', { name: 'Settings', exact: true })
    for (const width of [1200, 800]) {
      await application.evaluate(({ BrowserWindow }, width) => {
        const window = BrowserWindow.getAllWindows()[0]!
        window.setMinimumSize(0, 0)
        window.setContentSize(width, 600)
      }, width)
      await expect(trigger).toBeVisible()
      await expect(settings).toBeVisible()
      const left = (await trigger.boundingBox())!,
        right = (await settings.boundingBox())!
      expect(left.x + left.width).toBeLessThan(right.x)
      expect(Math.abs(left.y + left.height / 2 - right.y - right.height / 2)).toBeLessThan(1)
      expect(right.y + right.height).toBeLessThanOrEqual(600)
      expect(
        await page
          .locator('.avalon-rail-scroll')
          .evaluate((element) => element.scrollHeight > element.clientHeight),
      ).toBe(true)
      for (const kind of ['Static list', 'Live list']) {
        await trigger.focus()
        await page.keyboard.press('Enter')
        const menu = page.getByRole('menu', { name: 'New list' })
        await expect(menu.getByRole('menuitem', { name: 'Static list' })).toBeFocused()
        if (kind === 'Live list') await page.keyboard.press('ArrowDown')
        const choice = menu.getByRole('menuitem', { name: kind, exact: true })
        await expect(choice).toBeFocused()
        expect(await choice.getAttribute('title')).toBe(
          kind === 'Static list'
            ? 'Choose the games yourself. Add or remove titles whenever you like.'
            : 'Save the current library filters. Matching games update automatically.',
        )
        await page.keyboard.press('Enter')
        await expect(menu).toHaveCount(0)
        const dialog = page.getByRole('dialog', {
          name: kind === 'Static list' ? 'Name this list' : 'Name this live list',
          exact: true,
        })
        await expect(dialog.getByLabel('List name', { exact: true })).toBeFocused()
        await dialog.getByLabel('List name', { exact: true }).fill('Unconfirmed')
        await page.keyboard.press('Escape')
        await expect(dialog).toHaveCount(0)
        await expect(trigger).toBeFocused()
      }
    }
    await trigger.click()
    await page.screenshot({ path: info.outputPath('desktop-short-rail-footer.png') })
    await page.keyboard.press('Escape')
    expect((await api<LibraryResponse>({ route: 'library.get' })).lists).toHaveLength(24)
    await settings.click()
    await expect(settings).toHaveAttribute('aria-current', 'page')
    await expect(page.getByRole('heading', { name: 'Make yourself at home.' })).toBeVisible()
    expect(errors).toEqual([])
  } finally {
    for (const list of saved)
      await api({
        route: 'list.delete',
        params: { listId: list.id },
        body: { expectedRevision: list.revision },
      })
  }
})

test('desktop footer creates an empty static list and saves the current live rules through the real API', async () => {
  const saved: GameList[] = []
  try {
    await library('desktop', true)
    const trigger = page.getByRole('button', { name: 'New list', exact: true })
    await card(entries[0]).focus()
    await trigger.click()
    await page.getByRole('menuitem', { name: 'Static list' }).click()
    await page
      .getByRole('dialog', { name: 'Name this list', exact: true })
      .getByLabel('List name')
      .fill('Empty weekend')
    await page.getByRole('button', { name: 'Create list', exact: true }).click()
    await expect(page.getByRole('dialog')).toHaveCount(0)
    let list = (await api<LibraryResponse>({ route: 'library.get' })).lists.find(
      (list) => list.name === 'Empty weekend',
    )!
    saved.push(list)
    expect(list.releaseIds).toEqual([])
    expect(list.isLive).toBe(false)
    await expectCollection(page, 'all')
    await page.getByRole('textbox', { name: 'Search games' }).fill('Hades')
    await trigger.click()
    await page.getByRole('menuitem', { name: 'Live list' }).click()
    const dialog = page.getByRole('dialog', { name: 'Name this live list', exact: true })
    await expect(dialog.getByLabel('List name')).toHaveValue('Hades')
    await dialog.getByLabel('List name').fill('Footer Hades')
    await dialog.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(dialog).toHaveCount(0)
    list = (await api<LibraryResponse>({ route: 'library.get' })).lists.find(
      (list) => list.name === 'Footer Hades',
    )!
    saved.push(list)
    expect(list.isLive).toBe(true)
    expect(list.filter?.search).toBe('Hades')
    await expectCollection(page, list.id)
    await expect.poll(visibleIds).toEqual([entries[0].workId])
    await expect(page.getByRole('region', { name: 'Library filters', exact: true })).toBeVisible()
    await library('desktop', true)
    await selectCollection(page, list.id)
    await expect.poll(visibleIds).toEqual([entries[0].workId])
    expect(errors).toEqual([])
  } finally {
    for (const list of saved)
      await api({
        route: 'list.delete',
        params: { listId: list.id },
        body: { expectedRevision: list.revision },
      })
  }
})
