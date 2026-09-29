import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import type { ApiRequest } from '../../src/shared/bridge'
import type { FeedSnapshot, FeedVerdict } from '../../src/renderer/api/types'

let application: ElectronApplication, page: Page, directory: string
const failures: string[] = []
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-feed-'))
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

for (const mode of ['desktop', 'fullscreen'] as const)
  test(`${mode} saves a dated receipt, holds Undo, and retains history after navigation`, async () => {
    await application.evaluate(({ BrowserWindow }, mode) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setFullScreen(false)
      window.setContentSize(1440, 900)
      window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
      window.focus()
    }, mode)
    await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'For you', exact: true })
      .click()
    const feed = await api<FeedSnapshot>({ route: 'feed.get' })
    const shelf = feed.shelves.find((shelf) => shelf.supportsFeedback && shelf.items.length > 0)!
    expect(shelf).toBeTruthy()
    if (mode === 'fullscreen') {
      await page.getByRole('button', { name: `Show ${shelf.title}`, exact: true }).click()
      await expect(page.locator('.avalon-home-shelf').getByRole('heading')).toHaveText(shelf.title)
    }
    const scope =
      mode === 'desktop'
        ? page
            .locator('.avalon-shelf')
            .filter({ has: page.getByRole('heading', { name: shelf.title, exact: true }) })
        : page.locator('.avalon-home-hero')
    const name =
      mode === 'desktop'
        ? await scope.locator('.avalon-cover').first().getAttribute('aria-label')
        : await page
            .locator('.avalon-home-row .avalon-cover[data-selected="true"]')
            .getAttribute('aria-label')
    const cover = page.getByRole('button', { name: name!, exact: true }).first()
    await expect(cover).toBeVisible()
    await scope.getByRole('button', { name: 'Not now', exact: true }).first().click()
    const undo = scope.getByRole('button', { name: 'Undo', exact: true }).first()
    await expect(undo).toBeFocused()
    await expect(scope.getByRole('status').first()).toContainText('Back on')
    const saved = await api<FeedVerdict[]>({ route: 'feedHistory' })
    const active = saved.find((row) => row.kind === 1 && row.status === 0)!
    expect(active.expiresAt).toBeTruthy()
    await expect(scope.locator('time').first()).toHaveAttribute('dateTime', active.expiresAt!)
    await page.getByRole('button', { name: /What you've told the feed/ }).click()
    const dialog = page.getByRole('dialog', { name: "What you've told the feed", exact: true })
    await expect(dialog.getByRole('button', { name: 'Undo', exact: true })).toBeVisible()
    const geometry = await dialog.boundingBox(),
      viewport = page.viewportSize()
    expect(geometry).not.toBeNull()
    expect(geometry!.y).toBeGreaterThanOrEqual(0)
    if (viewport) expect(geometry!.y + geometry!.height).toBeLessThanOrEqual(viewport.height + 1)
    await dialog.getByRole('button', { name: 'Undo', exact: true }).click()
    await expect(dialog.getByText(/Undone on/).first()).toBeVisible()
    await dialog.getByRole('button', { name: 'Back to the feed' }).click()
    await expect(cover).toBeVisible()
    await expect(scope.getByRole('button', { name: 'Not now', exact: true }).first()).toBeVisible()
    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'Library', exact: true })
      .click()
    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'For you', exact: true })
      .click()
    await page.getByRole('button', { name: /What you've told the feed/ }).click()
    await expect(
      page
        .getByRole('dialog')
        .getByText(/Undone on/)
        .first(),
    ).toBeVisible()
    await page.getByRole('button', { name: 'Back to the feed', exact: true }).click()
    expect(failures).toEqual([])
  })
