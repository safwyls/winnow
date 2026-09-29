import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import type { ApiRequest } from '../../src/shared/bridge'
import type { LibraryResponse, ManualGame } from '../../src/renderer/api/types'

let app: ElectronApplication, page: Page, directory: string
const failures: string[] = []
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-library-lifecycle-'))
  const environment = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('.'), '--data-dir', directory, '--seed-sample', '--no-sync'],
    env: environment,
    chromiumSandbox: true,
    timeout: 60000,
  })
  page = await app.firstWindow()
  page.on('pageerror', (error) => failures.push(error.message))
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
  if (await page.getByRole('dialog', { name: 'Winnow setup' }).count())
    await page.getByRole('button', { name: 'Skip setup', exact: true }).click()
})
test.afterAll(async () => {
  let timer: ReturnType<typeof setTimeout> | undefined
  if (app)
    try {
      const child = app.process()
      await Promise.race([
        (async () => {
          await app.close()
          if (child.exitCode === null && child.signalCode === null)
            await new Promise<void>((done) => child.once('exit', () => done()))
        })(),
        new Promise<void>((done) => {
          timer = setTimeout(() => {
            child.kill('SIGKILL')
            done()
          }, 5000)
        }),
      ])
    } finally {
      clearTimeout(timer)
    }
  if (directory)
    try {
      const endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
      if (new URL(endpoint.address).hostname !== '127.0.0.1')
        throw Error('Unexpected fixture backend address')
      await fetch(new URL('/api/v1/lifecycle/shutdown', endpoint.address), {
        method: 'POST',
        headers: { Authorization: `Bearer ${endpoint.token}` },
        signal: AbortSignal.timeout(5000),
      })
    } catch {
      /* Keep the isolated fixture for diagnostics. */
    }
})
async function api<T>(input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const result = await window.winnow.request(input)
    if (!result.ok) throw Error(`${input.route}: ${result.status} ${result.message}`)
    return result.data
  }, input) as Promise<T>
}
const navigate = (name: string) =>
  page.getByRole('navigation', { name: 'Main navigation' }).getByRole('button', { name, exact: true }).click()
const preference = (name: string, value: string) =>
  api({ route: 'preferences.presentation.put', params: { preference: name }, body: { value } })

for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} folds real expansion links and applies a changed default while preserving raw game facts`, async ({}, info) => {
    await app.evaluate(({ BrowserWindow }, mode) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setFullScreen(false)
      window.setContentSize(1440, 900)
      window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
    }, mode)
    await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
    const prefix = `Parity ${mode}`
    const create = (title: string) =>
      api<ManualGame>({
        route: 'manual.create',
        body: {
          title,
          firstReleaseYear: 2006,
          platformLabel: 'PC',
          executablePath: null,
          installPath: null,
          igdbId: null,
          steamAppId: null,
        },
      })
    const base = await create(`${prefix} Base`),
      pack = await create(`${prefix} Pack`)
    const review = await api<{ revision: string }>({ route: 'identity.get' })
    await api({
      route: 'identity.link',
      body: {
        expectedRevision: review.revision,
        parentWorkId: base.workId,
        childWorkIds: [pack.workId],
        kind: 'expansion_of',
        relationLabel: 'expansion',
        rejectedCandidateIds: [],
        refusedPairs: [],
      },
    })
    await preference('GroupExpansions', 'false')
    await preference('DefaultSort', 'DormantLongest')
    await navigate('Library')
    await page.getByLabel('Search games', { exact: true }).fill(prefix)
    await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(2)
    await expect(page.getByLabel('Sort', { exact: true })).toHaveValue('dormant')
    await page.getByLabel('Sort', { exact: true }).selectOption('title-desc')
    await page.locator(`.avalon-library [data-avalon-game="${pack.workId}"]`).focus()
    const rawBefore = await api<LibraryResponse>({ route: 'library.get' })
    const workspaceBefore = await api<{ buckets: { workId: number; bucket: string }[] }>({ route: 'library.workspace' })
    expect(workspaceBefore.buckets.some((row) => row.workId === pack.workId && row.bucket === 'never_played')).toBe(true)
    await navigate('Settings')
    await page
      .getByRole('navigation', { name: 'Settings section' })
      .getByRole('button', { name: 'Library', exact: true })
      .click()
    await page.getByRole('combobox', { name: 'Default library sort', exact: true }).selectOption('NameAscending')
    await expect(page.getByRole('combobox', { name: 'Default library sort', exact: true })).toHaveValue('NameAscending')
    await expect(page.getByLabel('Group expansions with their base game', { exact: true })).toBeEnabled()
    await expect(page.getByLabel('Group expansions with their base game', { exact: true })).not.toBeChecked()
    await page.getByLabel('Group expansions with their base game', { exact: true }).click()
    await expect(page.getByLabel('Group expansions with their base game', { exact: true })).toBeChecked()
    await expect(page.getByLabel('Group expansions with their base game', { exact: true })).toBeEnabled()
    await navigate('Library')
    await expect(page.getByLabel('Sort', { exact: true })).toHaveValue('title')
    await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(1)
    const cover = page.locator(`.avalon-library [data-avalon-game="${base.workId}"]`)
    await expect(cover).toHaveAccessibleName(
      `View ${base.title}. Includes 1 expansion, one of them never played.`,
    )
    const mark = cover.locator('.avalon-expansion-mark')
    await expect(mark).toHaveText('+1')
    await page.getByLabel('Search games', { exact: true }).focus()
    await page.mouse.move(0, 0)
    await expect(mark).toHaveCSS('opacity', '1')
    const markBounds = await mark.boundingBox(),
      coverBounds = await cover.boundingBox()
    expect(markBounds).not.toBeNull()
    expect(coverBounds).not.toBeNull()
    expect(markBounds!.x).toBeGreaterThanOrEqual(coverBounds!.x)
    expect(markBounds!.y + markBounds!.height).toBeLessThanOrEqual(coverBounds!.y + coverBounds!.height)
    await page.screenshot({ path: info.outputPath(`${mode}-expansion-mark.png`) })
    const rawAfter = await api<LibraryResponse>({ route: 'library.get' })
    expect(rawAfter.games).toEqual(rawBefore.games)
    const workspaceAfter = await api<{ buckets: unknown[] }>({ route: 'library.workspace' })
    expect(workspaceAfter.buckets).toEqual(workspaceBefore.buckets)
    const packList = await api<{ id: number }>({
      route: 'list.create',
      body: { name: `${prefix} pack only`, releaseIds: [pack.releaseId] },
    })
    await expect(
      page.getByRole('combobox', { name: 'My lists', exact: true }).locator(`option[value="${packList.id}"]`),
    ).toBeAttached()
    await page.getByRole('combobox', { name: 'My lists', exact: true }).selectOption(String(packList.id))
    await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(0)
    await page.getByRole('combobox', { name: 'My lists', exact: true }).selectOption('all')
    await navigate('Settings')
    await expect(page.getByLabel('Group expansions with their base game', { exact: true })).toBeChecked()
    await page.getByLabel('Group expansions with their base game', { exact: true }).click()
    await expect(page.getByLabel('Group expansions with their base game', { exact: true })).not.toBeChecked()
    await expect(page.getByLabel('Group expansions with their base game', { exact: true })).toBeEnabled()
    await navigate('Library')
    await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(2)
    await expect(page.locator('.avalon-expansion-mark')).toHaveCount(0)
    expect(failures).toEqual([])
  })
}
