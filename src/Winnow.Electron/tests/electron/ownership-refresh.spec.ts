import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import { prebuiltActivationHelper, prebuiltFixture } from './prebuilt-backend'
import type { LibraryResponse } from '../../src/renderer/api/types'

const fixture = prebuiltFixture
let application: ElectronApplication, page: Page, directory: string
let endpoint: { address: string; token: string }
const errors: string[] = []
interface RefreshState {
  acquisitions: number
  metadataEntered: boolean
  metadataCommitted: boolean
  optionalFailed: boolean
  finished: boolean
  coordinatorFailed: boolean
  schedulerRunning: boolean
  publications: string[]
}

test.beforeAll(async () => {
  await Promise.all([readFile(fixture), readFile(prebuiltActivationHelper)])
})
test.beforeEach(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-ownership-'))
  errors.length = 0
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
  await expect(page.getByRole('heading', { name: 'Your library starts here.' })).toBeVisible()
  endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
})
test.afterEach(async () => {
  await closeFixture(application, directory)
  expect(errors).toEqual([])
})

async function control<T = void>(path: string, body?: unknown): Promise<T> {
  const response = await fetch(new URL(`/__fixture/ownership/${path}`, endpoint.address), {
    method: body === undefined ? 'GET' : 'POST',
    headers: { Authorization: `Bearer ${endpoint.token}`, 'Content-Type': 'application/json' },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    signal: AbortSignal.timeout(15000),
  })
  if (!response.ok) throw Error(`Ownership fixture ${path}: HTTP ${response.status}`)
  return response.status === 202 ? (undefined as T) : ((await response.json()) as T)
}
async function surface(mode: 'desktop' | 'fullscreen') {
  await application.evaluate(({ BrowserWindow }, mode) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setContentSize(1920, 1080)
    window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
    window.focus()
  }, mode)
  await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
}
async function libraryTitle() {
  return page.evaluate(async () => {
    const result = await window.winnow.request<LibraryResponse>({ route: 'library.get' })
    if (!result.ok) throw Error('Library could not be read')
    return result.data!.games.map((game) => game.title)
  })
}
async function visibleFeedTitle(mode: 'desktop' | 'fullscreen', title: string) {
  const feed = page.locator(mode === 'desktop' ? '.avalon-discover' : '.avalon-home')
  await expect(feed).toBeVisible()
  await expect(feed.getByText(title, { exact: true }).first()).toBeVisible()
}

for (const mode of ['desktop', 'fullscreen'] as const)
  for (const fail of [false, true])
    test(`${mode} scheduled acquisition and gated metadata update the visible feed with partial failure ${fail}`, async ({}, info) => {
      await surface(mode)
      await page.evaluate(() => {
        const events: string[] = []
        Object.assign(window, { ownershipRefreshEvents: events })
        window.winnow.onEvent((event) => events.push(event.kind))
      })
      // The fixture extends the authenticated backend, never the renderer bridge.
      expect((await fetch(new URL('/__fixture/ownership/state', endpoint.address))).status).toBe(401)
      await control('start', { fail })
      await expect.poll(async () => (await control<RefreshState>('state')).metadataEntered).toBe(true)
      expect(await control<RefreshState>('state')).toMatchObject({
        acquisitions: 1,
        metadataCommitted: false,
        finished: false,
        publications: ['New acquisition'],
      })
      await expect.poll(libraryTitle).toEqual(['New acquisition'])
      await visibleFeedTitle(mode, 'New acquisition')
      expect(
        await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0]!.isVisible()),
      ).toBe(true)
      await control('release-metadata', {})
      await expect.poll(async () => (await control<RefreshState>('state')).finished).toBe(true)
      await expect.poll(libraryTitle).toEqual(['Enriched acquisition'])
      await visibleFeedTitle(mode, 'Enriched acquisition')
      const state = await control<RefreshState>('state')
      expect(state).toEqual({
        acquisitions: 1,
        metadataEntered: true,
        metadataCommitted: true,
        optionalFailed: fail,
        finished: true,
        coordinatorFailed: fail,
        schedulerRunning: true,
        publications: ['New acquisition', 'Enriched acquisition'],
      })
      await expect
        .poll(() =>
          page.evaluate(
            () =>
              (window as unknown as { ownershipRefreshEvents: string[] }).ownershipRefreshEvents.filter(
                (kind) => kind === 'library.changed',
              ).length,
          ),
        )
        .toBeGreaterThanOrEqual(2)
      await page.screenshot({
        path: info.outputPath(`${mode}-scheduled-ownership-${fail ? 'partial' : 'success'}.png`),
      })
      if (mode === 'desktop') {
        // Electron's inactive presentation enters from the already refreshed shared query cache.
        await surface('fullscreen')
        await visibleFeedTitle('fullscreen', 'Enriched acquisition')
        await page.screenshot({
          path: info.outputPath(`fullscreen-late-entry-${fail ? 'partial' : 'success'}.png`),
        })
      }
      await info.attach('scheduled-refresh-phases', {
        body: JSON.stringify(state, null, 2),
        contentType: 'application/json',
      })
    })
