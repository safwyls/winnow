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
    args: [resolve('tests/electron/library-main.mjs'), '--data-dir', directory, '--seed-sample', '--no-sync'],
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
  test(`${mode} opens keyboard-selected Details only on activation and preserves the selection on close`, async () => {
    await app.evaluate(({ BrowserWindow }, mode) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setFullScreen(false)
      window.setContentSize(1440, 900)
      window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
    }, mode)
    await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
    await navigate('Library')
    const nav = page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'Library', exact: true })
    await nav.focus()
    await nav.press('Enter')
    await expect(page.locator('.avalon-details')).toHaveCount(0)
    const card = page.locator('.avalon-library .avalon-cover').nth(2)
    const id = await card.getAttribute('data-work-id')
    const title = await card.locator('.avalon-cover-fallback').innerText()
    await card.focus()
    await expect(card).toHaveAttribute('data-selected', 'true')
    await expect(page.locator('.avalon-details')).toHaveCount(0)
    await card.press('Enter')
    await expect(
      page.locator('.avalon-details').getByRole('heading', { name: title, exact: true }),
    ).toBeVisible()
    await page.keyboard.press('Escape')
    await expect(page.locator('.avalon-details')).toHaveCount(0)
    const returned = page.locator(`.avalon-library .avalon-cover[data-work-id="${id}"]`)
    await expect(returned).toHaveAttribute('data-selected', 'true')
    await expect(page.locator('.avalon-library .avalon-cover[data-selected="true"]')).toHaveCount(1)
    await returned.click()
    await expect(
      page.locator('.avalon-details').getByRole('heading', { name: title, exact: true }),
    ).toBeVisible()
    await page
      .getByRole('button', {
        name: mode === 'desktop' ? 'Close game details' : 'B · Back to Library',
        exact: true,
      })
      .click()
    await expect(returned).toHaveAttribute('data-selected', 'true')
  })
  test(`${mode} saves dormancy independently of reduced motion without replacing cover elements`, async ({}, info) => {
    await app.evaluate(() => {
      ;(globalThis as unknown as { __libraryDimmingArtwork: boolean }).__libraryDimmingArtwork = true
    })
    await page.reload()
    await expect(page.locator('.avalon-shell')).toBeVisible()
    const setMode = async () => {
      await app.evaluate(({ BrowserWindow }, mode) => {
        const window = BrowserWindow.getAllWindows()[0]!
        window.setFullScreen(false)
        window.setContentSize(1440, 900)
        window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
      }, mode)
      await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
    }
    await setMode()
    await preference('DimDormantCovers', 'true')
    await navigate('Library')
    if (mode === 'desktop') await page.getByRole('button', { name: 'Grid view', exact: true }).click()
    await page.getByLabel('Sort', { exact: true }).selectOption('dormant')
    await page.getByLabel('Sort', { exact: true }).focus()
    await page.mouse.move(5, 5)
    const covers = page.locator('.avalon-library .avalon-cover:not([data-selected="true"])')
    await expect(covers.first()).toBeVisible()
    const originals = await covers.elementHandles()
    const artwork = originals[0]!
    await expect
      .poll(() => artwork.evaluate((node) => (node as HTMLElement).querySelector('img')?.naturalWidth))
      .toBe(600)
    const filter = () =>
      artwork.evaluate((node) => getComputedStyle((node as HTMLElement).querySelector('.artwork')!).filter)
    await expect.poll(filter).toContain('saturate(0.22)')
    const images = await artwork.evaluate((node) =>
      [...(node as HTMLElement).querySelectorAll('img')].map((image) => image.src),
    )
    expect(images).toHaveLength(1)
    await page.emulateMedia({ reducedMotion: 'reduce' })
    await preference('DimDormantCovers', 'false')
    await expect.poll(filter).toBe('none')
    for (const cover of originals) expect(await cover.evaluate((node) => node.isConnected)).toBe(true)
    await covers.first().hover()
    expect(await filter()).toBe('none')
    expect(
      await artwork.evaluate((node) =>
        [...(node as HTMLElement).querySelectorAll('img')].map((image) => image.src),
      ),
    ).toEqual(images)
    expect(
      await artwork.evaluate(
        (node) => getComputedStyle((node as HTMLElement).querySelector('.artwork')!).transitionDuration,
      ),
    ).toBe('0s')
    await page.mouse.move(5, 5)
    await preference('DimDormantCovers', 'true')
    await expect.poll(filter).toContain('saturate(0.22)')
    expect(await artwork.evaluate((node) => node.isConnected)).toBe(true)
    expect(await page.evaluate(() => matchMedia('(prefers-reduced-motion: reduce)').matches)).toBe(true)
    await navigate('Settings')
    await page
      .getByRole('navigation', { name: 'Settings section' })
      .getByRole('button', { name: 'Library', exact: true })
      .click()
    const toggle = page.getByRole('checkbox', { name: 'Dim dormant covers', exact: true })
    await expect(toggle).toBeChecked()
    await toggle.click()
    await expect(toggle).not.toBeChecked()
    await expect
      .poll(
        async () =>
          (
            await api<{ preference: string; value: string }[]>({ route: 'preferences.presentation.get' })
          ).find((row) => row.preference === 'DimDormantCovers')?.value,
      )
      .toBe('false')
    await page.reload()
    await expect(page.locator('.avalon-shell')).toBeVisible()
    await setMode()
    await navigate('Library')
    await expect(page.locator('html')).toHaveAttribute('data-dim-dormant', 'false')
    for (const cover of await page.locator('.avalon-library .avalon-cover > .artwork').all())
      expect(await cover.evaluate((node) => getComputedStyle(node).filter)).toBe('none')
    await page.screenshot({ path: info.outputPath(`library-${mode}-dimming-off.png`) })
    await page.emulateMedia({ reducedMotion: 'no-preference' })
    await app.evaluate(() => {
      ;(globalThis as unknown as { __libraryDimmingArtwork: boolean }).__libraryDimmingArtwork = false
    })
    await page.reload()
    await expect(page.locator('.avalon-shell')).toBeVisible()
    expect(failures).toEqual([])
  })
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
    const workspaceBefore = await api<{ buckets: { workId: number; bucket: string }[] }>({
      route: 'library.workspace',
    })
    expect(
      workspaceBefore.buckets.some((row) => row.workId === pack.workId && row.bucket === 'never_played'),
    ).toBe(true)
    await navigate('Settings')
    await page
      .getByRole('navigation', { name: 'Settings section' })
      .getByRole('button', { name: 'Library', exact: true })
      .click()
    await page
      .getByRole('combobox', { name: 'Default library sort', exact: true })
      .selectOption('NameAscending')
    await expect(page.getByRole('combobox', { name: 'Default library sort', exact: true })).toHaveValue(
      'NameAscending',
    )
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

for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} presents clearable real list cuts and source library chrome`, async ({}, info) => {
    await app.evaluate(({ BrowserWindow }, mode) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setFullScreen(false)
      window.setContentSize(1440, 900)
      window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
    }, mode)
    await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
    const prefix = `Chrome ${mode}`
    const created: ManualGame[] = []
    for (const title of [`${prefix} Alpha`, `${prefix} Bravo`])
      created.push(
        await api<ManualGame>({
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
        }),
      )
    const list = await api<{ id: number }>({
      route: 'list.create',
      body: { name: `${prefix} favorites`, releaseIds: [created[0].releaseId] },
    })
    await navigate('Library')
    await page.getByRole('combobox', { name: 'My lists', exact: true }).selectOption('all')
    await page.getByRole('textbox', { name: 'Search games', exact: true }).fill(prefix)
    await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(2)
    await expect(page.locator('.avalon-cut-count')).toContainText('→ 2')
    const cut = page.getByRole('region', { name: 'Current library filters' })
    await expect(cut.getByRole('button', { name: 'Remove search filter' })).toHaveAttribute(
      'aria-description',
      `SEARCH: ${prefix}`,
    )
    if (mode === 'desktop') {
      const slider = page.getByRole('slider', { name: 'Density', exact: true })
      await slider.focus()
      await slider.press('Home')
      await expect(slider).toHaveValue('108')
      const wide = await page.locator('.avalon-library [data-avalon-game]').first().boundingBox()
      await slider.press('End')
      await expect(slider).toHaveValue('200')
      await expect
        .poll(
          async () => (await page.locator('.avalon-library [data-avalon-game]').first().boundingBox())!.width,
        )
        .toBeLessThan(wide!.width)
      await page.getByRole('button', { name: 'List view', exact: true }).click()
      const header = page.getByRole('group', { name: 'Library columns' })
      for (const [column, first, second] of [
        ['playtime', 'time', 'time-low'],
        ['title', 'title', 'title-desc'],
        ['idle', 'dormant', 'recent'],
      ]) {
        await header.getByRole('button', { name: `Sort by ${column}`, exact: true }).click()
        await expect(page.getByRole('combobox', { name: 'Sort', exact: true })).toHaveValue(first)
        await header.getByRole('button', { name: `Sort by ${column}`, exact: true }).click()
        await expect(page.getByRole('combobox', { name: 'Sort', exact: true })).toHaveValue(second)
        await expect(header.locator('[data-sort-direction]')).toHaveCount(1)
      }
      const geometry = await page.evaluate(() => {
        const header = document.querySelector<HTMLElement>('.avalon-record-header')!,
          rows = [...document.querySelectorAll<HTMLElement>('.avalon-record')]
        return {
          headerColumns: getComputedStyle(header).gridTemplateColumns,
          rows: rows.map((row) => ({
            columns: getComputedStyle(row).gridTemplateColumns,
            cover: {
              width: row.querySelector('.artwork')!.getBoundingClientRect().width,
              height: row.querySelector('.artwork')!.getBoundingClientRect().height,
            },
            starts: [...row.children].map((cell) => cell.getBoundingClientRect().left),
          })),
          starts: [...header.children].map((cell) => cell.getBoundingClientRect().left),
          titleJustification: getComputedStyle(header.children[1]).justifyContent,
          placeholderText: [...document.querySelectorAll('.avalon-record .art-placeholder span')].map(
            (label) => getComputedStyle(label).display,
          ),
        }
      })
      expect(geometry.titleJustification).toBe('flex-start')
      expect(geometry.placeholderText.length).toBeGreaterThan(0)
      expect(geometry.placeholderText.every((display) => display === 'none')).toBe(true)
      for (const row of geometry.rows) {
        expect(row.columns).toBe(geometry.headerColumns)
        expect(row.cover).toEqual({ width: 24, height: 36 })
        for (const index of [1, 2, 4, 5])
          expect(Math.abs(row.starts[index] - geometry.starts[index])).toBeLessThanOrEqual(1)
      }
    } else {
      await expect(page.getByRole('group', { name: 'Library columns' })).toHaveCount(0)
      await expect(page.getByRole('slider', { name: 'Density' })).toHaveCount(0)
    }
    await page.getByRole('combobox', { name: 'My lists', exact: true }).selectOption(String(list.id))
    await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(1)
    await expect(cut.getByRole('button', { name: 'Leave this list' })).toHaveText(`LIST${prefix} favorites`)
    await cut.getByRole('button', { name: 'Remove search filter' }).click()
    await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(1)
    await page.screenshot({ path: info.outputPath(`${mode}-library-chrome.png`) })
    await cut.getByRole('button', { name: 'Leave this list' }).click()
    await expect(cut).toHaveCount(0)
    expect(failures).toEqual([])
  })
}
