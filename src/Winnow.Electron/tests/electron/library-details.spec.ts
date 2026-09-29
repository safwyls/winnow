import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import type { ApiRequest } from '../../src/shared/bridge'
import type { LibraryResponse, ManualGame } from '../../src/renderer/api/types'
import type { MergeReview } from '../../src/renderer/features/parity-merge-model'

let application: ElectronApplication, page: Page, directory: string
const failures: string[] = []
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-library-'))
  const environment = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('.'), '--data-dir', directory, '--seed-sample', '--no-sync'],
    env: environment,
    chromiumSandbox: true,
    timeout: 60_000,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => failures.push(error.message))
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
  if (await page.getByRole('dialog', { name: 'Winnow setup' }).count())
    await page.getByRole('button', { name: 'Skip setup', exact: true }).click()
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
})
test.afterAll(async () => {
  if (application) {
    let timer: ReturnType<typeof setTimeout> | undefined
    try {
      await Promise.race([
        application.close(),
        new Promise<void>((done) => {
          timer = setTimeout(() => {
            application.process().kill()
            done()
          }, 5000)
        }),
      ])
    } finally {
      clearTimeout(timer)
    }
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
      /* Preserve the isolated fixture for diagnostics if startup or shutdown failed. */
    }
})
async function api<T>(input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const result = await window.winnow.request(input)
    if (!result.ok) throw Error(`${input.route}: ${result.status} ${result.message}`)
    return result.data
  }, input) as Promise<T>
}
async function surface(mode: 'desktop' | 'fullscreen') {
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
async function manual(title: string) {
  return api<ManualGame>({
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
}
for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} searches real workspace names, groups editions and undoes the exact saved identity act`, async () => {
    const parent = await manual(`Parity ${mode} primary`),
      child = await manual(`Parity ${mode} edition`)
    await surface(mode)
    await page.getByRole('button', { name: 'Manage library' }).click()
    await page.getByRole('button', { name: 'Identity review', exact: true }).click()
    await page.getByRole('button', { name: 'Create a relationship' }).click()
    await page.getByRole('combobox', { name: 'Main game', exact: true }).selectOption(String(parent.workId))
    await page.getByLabel('Find games to include').fill(`Parity ${mode}`)
    await page.getByRole('checkbox', { name: child.title, exact: true }).check()
    const before = await api<LibraryResponse>({ route: 'library.get' })
    expect(before.games.find((game) => game.workId === parent.workId)?.entries).toHaveLength(1)
    expect(before.games.some((game) => game.workId === child.workId)).toBe(true)
    await page.getByRole('button', { name: 'Confirm relationship', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Undo last decision' })).toBeEnabled()
    const grouped = await api<LibraryResponse>({ route: 'library.get' })
    expect(
      grouped.games
        .find((game) => game.workId === parent.workId)
        ?.entries.map((entry) => entry.releaseId)
        .sort(),
    ).toEqual([parent.releaseId, child.releaseId].sort())
    expect(grouped.games.some((game) => game.workId === child.workId)).toBe(false)
    await page.getByRole('button', { name: 'Undo last decision' }).click()
    await expect(page.getByText('Decision undone.', { exact: true })).toBeVisible()
    const restored = await api<LibraryResponse>({ route: 'library.get' })
    expect(restored.games.find((game) => game.workId === parent.workId)?.entries).toHaveLength(1)
    expect(restored.games.find((game) => game.workId === child.workId)?.entries).toHaveLength(1)
    await page.getByRole('button', { name: 'Create a relationship' }).click()
    await page.getByLabel('Find games to include').fill(child.title)
    await expect(page.getByRole('checkbox', { name: child.title, exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Cancel relationship' }).click()
    await page.getByRole('button', { name: 'Close tools' }).click()
    expect(failures).toEqual([])
  })
  test(`${mode} saves a metadata year through the real API and refreshes its open live list without losing another draft`, async () => {
    const game = await manual(`Year parity ${mode}`)
    const list = await api<{ id: number }>({
      route: 'list.live',
      body: { name: `2006 parity ${mode}`, filter: { yearFrom: 2006, yearTo: 2006, search: game.title } },
    })
    await surface(mode)
    // The API emits the same library invalidation used by the production renderer.
    await expect(page.getByLabel('My lists').locator(`option[value="${list.id}"]`)).toHaveCount(1)
    await page.getByLabel('My lists').selectOption(String(list.id))
    await page.getByRole('button', { name: `View ${game.title}`, exact: true }).click()
    await page.getByRole('button', { name: 'Metadata', exact: true }).click()
    await page.getByLabel('Name', { exact: true }).fill('An unfinished title')
    await page.getByLabel('Release year', { exact: true }).fill('2017')
    await page.getByRole('button', { name: 'Save release year', exact: true }).click()
    await expect(page.getByText('Saved.', { exact: true })).toBeVisible()
    await expect(page.getByLabel('Name', { exact: true })).toHaveValue('An unfinished title')
    await page.getByRole('button', { name: 'Back to your library', exact: true }).click()
    await expect(page.getByText('No games match these filters.', { exact: true })).toBeVisible()
    await expect(page.getByLabel('My lists')).toHaveValue(String(list.id))
    const saved = await api<LibraryResponse>({ route: 'library.get' })
    expect(saved.games.find((item) => item.workId === game.workId)?.firstReleaseYear).toBe(2017)
    expect(saved.games.find((item) => item.workId === game.workId)?.title).toBe(game.title)
    expect(saved.lists.find((item) => item.id === list.id)?.releaseIds).toEqual([])
    await page.getByRole('button', { name: 'Close list', exact: true }).click()
    expect(failures).toEqual([])
  })
  test(`${mode} reviews grouped proposals, merges selected groups and retracts their exact acts through one Undo`, async () => {
    await surface(mode)
    await page.getByRole('button', { name: 'Manage library' }).click()
    await page.getByRole('button', { name: 'Identity review', exact: true }).click()
    const queue = page.locator('.merge-queue')
    await expect(queue.locator('.merge-card:not(.resolved)').nth(1)).toBeVisible()
    const rows = queue.locator('[data-merge-row]')
    await rows.first().focus()
    await rows.first().press('ArrowDown')
    await expect(rows.nth(1)).toBeFocused()
    const before = await api<MergeReview>({ route: 'identity.get' })
    const standing = new Set(before.history.filter((link) => !link.retractedAt).map((link) => link.actId))
    await queue
      .locator('.merge-card:not(.resolved)')
      .nth(0)
      .getByRole('checkbox', { name: /^Select .* group$/ })
      .check()
    await queue
      .locator('.merge-card:not(.resolved)')
      .nth(1)
      .getByRole('checkbox', { name: /^Select .* group$/ })
      .check()
    await queue.getByRole('button', { name: 'Merge 2 selected', exact: true }).click()
    await expect(queue.getByRole('button', { name: 'Undo review decisions', exact: true })).toBeEnabled()
    await expect(queue.getByText('2 rolled up · Nothing deleted', { exact: true })).toBeVisible()
    const linked = await api<MergeReview>({ route: 'identity.get' })
    const newActs = [
      ...new Set(
        linked.history
          .filter((link) => !link.retractedAt && !standing.has(link.actId))
          .map((link) => link.actId),
      ),
    ]
    expect(newActs).toHaveLength(2)
    await queue.getByRole('button', { name: 'Undo review decisions', exact: true }).click()
    await expect(
      queue.getByText('Decision undone. Your games and play history are kept.', { exact: true }),
    ).toBeVisible()
    const undone = await api<MergeReview>({ route: 'identity.get' })
    expect(
      undone.history.filter((link) => newActs.includes(link.actId)).every((link) => link.retractedAt),
    ).toBe(true)
    await expect(queue.locator('.merge-card:not(.resolved)').nth(1)).toBeVisible()
    const geometry = await queue
      .locator('.merge-card:not(.resolved)')
      .first()
      .evaluate((card) => {
        const bounds = card.getBoundingClientRect()
        return {
          left: bounds.left,
          right: bounds.right,
          width: document.documentElement.clientWidth,
          overflow: card.scrollWidth > card.clientWidth + 1,
        }
      })
    expect(geometry.left).toBeGreaterThanOrEqual(0)
    expect(geometry.right).toBeLessThanOrEqual(geometry.width)
    expect(geometry.overflow).toBe(false)
    await page.getByRole('button', { name: 'Close tools' }).click()
    expect(failures).toEqual([])
  })
}
