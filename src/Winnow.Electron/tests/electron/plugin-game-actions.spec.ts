import { prebuiltFixture as fixture, prebuiltActivationHelper } from './prebuilt-backend'
import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'

import { mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import type { ApiRequest } from '../../src/shared/bridge'
import type { LibraryResponse, Workspace } from '../../src/renderer/api/types'

const label = 'Played history — not proof of ownership'
let application: ElectronApplication | undefined, page: Page, directory: string
let endpoint: { address: string; token: string }
const errors: string[] = []
type Mode = 'desktop' | 'fullscreen'
interface PluginState {
  works: { id: number; name: string; nameIsProvisional: boolean }[]
  ownerships: { id: number; releaseId: number; store: string; installed: boolean; intentLive: boolean }[]
  externalIds: { releaseId: number; provider: string; providerId: string }[]
  actions: Record<string, { sourceLabel: string; canPlay: boolean; canOpenStore: boolean }>
  lastAction: string | null
  calls: string[]
  loaded: boolean
  shellAttempts: string[]
}

test.beforeEach(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-plugin-actions-'))
  errors.length = 0
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('tests/electron/plugin-game-actions-main.mjs'), '--data-dir', directory, '--no-sync'],
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
  expect((await fetch(new URL('/__fixture/plugin-actions/state', endpoint.address))).status).toBe(401)
})

test.afterEach(async ({}, info) => {
  try {
    if (info.status !== info.expectedStatus && page)
      await info.attach('plugin-action-failure', { body: await page.screenshot(), contentType: 'image/png' })
    if (application) {
      expect(await application.evaluate(() => (globalThis as any).__pluginGameActions.shellAttempts)).toEqual(
        [],
      )
      expect((await control<PluginState>('state')).shellAttempts).toEqual([])
    }
  } finally {
    await closeFixture(application, directory)
    application = undefined
  }
  expect(errors).toEqual([])
})

async function control<T = void>(path: string, body?: unknown): Promise<T> {
  const response = await fetch(new URL(`/__fixture/plugin-actions/${path}`, endpoint.address), {
    method: body === undefined ? 'GET' : 'POST',
    headers: { Authorization: `Bearer ${endpoint.token}`, 'Content-Type': 'application/json' },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    signal: AbortSignal.timeout(15000),
  })
  if (!response.ok) throw Error(`Plugin fixture ${path}: HTTP ${response.status}`)
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}
async function api<T>(input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const result = await window.winnow.request(input)
    if (!result.ok) throw Error(`API ${input.route}: ${result.status} ${result.message}`)
    return result.data
  }, input) as Promise<T>
}
async function surface(mode: Mode) {
  await application!.evaluate(({ BrowserWindow }, mode) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setContentSize(1920, 1080)
    window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
    window.focus()
  }, mode)
  await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
}
async function showLibrary() {
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
}
const tile = (workId: number) => page.locator(`.avalon-library [data-avalon-game="${workId}"]`)
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
async function openDetails(workId: number) {
  await tile(workId).click()
  await expect(
    page.locator('.avalon-details').getByRole('heading', { name: 'Xbox fixture', exact: true }),
  ).toBeVisible()
  await page.getByRole('tab', { name: 'Library', exact: true }).click()
  await expect(page.getByRole('tabpanel').getByText(label, { exact: true })).toBeVisible()
  await expect(page.getByRole('tabpanel').locator('.entry-actions strong')).toHaveText('Xbox')
}
async function execute(ownershipId: number, action: string) {
  return api<number>({
    route: 'actions.execute',
    params: { ownershipId },
    body: { operationId: crypto.randomUUID(), action },
  })
}

for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} plugin resource title promotion preserves the work and imported source identity`, async () => {
    await surface(mode)
    await control('sync', { state: 'provisional' })
    const initial = await control<PluginState>('state')
    expect(initial.works).toHaveLength(1)
    expect(initial.works[0].nameIsProvisional).toBe(true)
    const workId = initial.works[0].id
    expect(initial.externalIds).toEqual([
      expect.objectContaining({ provider: 'plugin:xbox', providerId: 'stable-package' }),
    ])
    await showLibrary()
    await control('sync', { state: 'available' })
    const promoted = await control<PluginState>('state')
    expect(promoted.works).toEqual([{ id: workId, name: 'Xbox fixture', nameIsProvisional: false }])
    expect(promoted.ownerships.map(({ id, releaseId }) => ({ id, releaseId }))).toEqual(
      initial.ownerships.map(({ id, releaseId }) => ({ id, releaseId })),
    )
    expect(promoted.externalIds).toEqual(initial.externalIds)
    await expect(tile(workId)).toHaveAccessibleName('View Xbox fixture')
    await expect(page.locator('.avalon-results-count')).toHaveText('1 game')
    expect(promoted.calls).toEqual([])
  })

  test(`${mode} offline plugin tile actions use the active provider and retain exact launch attribution`, async ({}, info) => {
    await surface(mode)
    await control('sync', { state: 'available' })
    const imported = await control<PluginState>('state')
    expect(imported.ownerships).toHaveLength(1)
    const ownership = imported.ownerships[0],
      workId = imported.works[0].id
    expect(ownership.store).toBe('plugin:xbox')
    expect(imported.actions[String(ownership.id)]).toEqual({
      sourceLabel: label,
      canPlay: true,
      canOpenStore: true,
    })
    await control('sync', { state: 'unavailable' })
    await showLibrary()
    await expect(tile(workId)).toHaveAccessibleName('View Xbox fixture')
    expect(
      (await api<Workspace>({ route: 'library.workspace' })).pluginActions[String(ownership.id)],
    ).toEqual({
      sourceLabel: label,
      canPlay: true,
      canOpenStore: true,
      pluginId: 'xbox',
      sourceId: 'stable-package',
    })
    if (mode === 'desktop') {
      await tile(workId).hover()
      await tile(workId).locator('..').getByRole('button', { name: 'Play', exact: true }).click()
    } else {
      await tile(workId).focus()
      await tap(2)
    }
    await expect
      .poll(async () => (await control<PluginState>('state')).calls)
      .toEqual(['stable-package:Play'])
    const launched = await control<PluginState>('state')
    expect(launched.ownerships).toEqual([expect.objectContaining({ id: ownership.id, intentLive: true })])
    expect(launched.lastAction).toBe('stable-package:Play')
    expect(await execute(ownership.id, 'Play')).toBe(1)
    expect((await control<PluginState>('state')).calls).toEqual(['stable-package:Play'])
    await openDetails(workId)
    await page.getByRole('tabpanel').getByRole('button', { name: 'Open store', exact: true }).click()
    await expect
      .poll(async () => (await control<PluginState>('state')).calls)
      .toEqual(['stable-package:Play', 'stable-package:OpenStore'])
    expect((await control<PluginState>('state')).lastAction).toBe('stable-package:OpenStore')
    expect(await control('validate-captured', { afterRemoval: false })).toEqual({
      wrongOwnershipAccepted: false,
      forgedLinkAccepted: false,
      staleAccepted: null,
    })
    const requests = await application!.evaluate(() => (globalThis as any).__pluginGameActions.actions)
    expect(requests.map((request: any) => [request.path, request.body.action, request.result])).toEqual([
      [`/api/v1/entries/${ownership.id}/actions`, 'Play', 0],
      [`/api/v1/entries/${ownership.id}/actions`, 'Play', 1],
      [`/api/v1/entries/${ownership.id}/actions`, 'OpenStore', 0],
    ])
    await page.screenshot({
      path: info.outputPath(`${mode}-offline-plugin-actions.png`),
      animations: 'disabled',
    })
  })

  test(`${mode} plugin uninstall rejects stale Play and unload retains inclusion evidence without actions`, async ({}, info) => {
    await surface(mode)
    await control('sync', { state: 'available' })
    const before = await control<PluginState>('state')
    const ownership = before.ownerships[0],
      workId = before.works[0].id
    await showLibrary()
    await openDetails(workId)
    await expect(page.getByRole('tabpanel').getByRole('button', { name: 'Play', exact: true })).toBeVisible()
    await control('sync', { state: 'removed' })
    await expect(
      page.locator('.avalon-details').getByRole('button', { name: 'Play', exact: true }),
    ).toHaveCount(0)
    await expect(
      page.getByRole('tabpanel').getByRole('button', { name: 'Open store', exact: true }),
    ).toBeVisible()
    expect((await control<PluginState>('state')).actions[String(ownership.id)]).toEqual({
      sourceLabel: label,
      canPlay: false,
      canOpenStore: true,
    })
    expect(await control('validate-captured', { afterRemoval: true })).toEqual({
      wrongOwnershipAccepted: false,
      forgedLinkAccepted: false,
      staleAccepted: false,
    })
    expect(await execute(ownership.id, 'Play')).toBe(2)
    await control('unload', {})
    await expect(
      page.locator('.avalon-details').getByRole('button', { name: 'Open store', exact: true }),
    ).toHaveCount(0)
    await expect(page.getByRole('tabpanel').getByText(label, { exact: true })).toBeVisible()
    expect(await execute(ownership.id, 'OpenStore')).toBe(2)
    const unloaded = await control<PluginState>('state')
    expect(unloaded.loaded).toBe(false)
    expect(unloaded.actions[String(ownership.id)]).toEqual({
      sourceLabel: label,
      canPlay: false,
      canOpenStore: false,
    })
    expect(unloaded.calls).toEqual([])
    expect(unloaded.ownerships).toEqual([
      expect.objectContaining({ id: ownership.id, installed: false, intentLive: false }),
    ])
    expect((await api<LibraryResponse>({ route: 'library.get' })).games).toHaveLength(1)
    await page.screenshot({ path: info.outputPath(`${mode}-unloaded-plugin.png`), animations: 'disabled' })
  })
}

test('fullscreen plugin inclusion and copy facts remain legible at 1080p and 720p with larger text', async ({}, info) => {
  await surface('fullscreen')
  await control('sync', { state: 'available' })
  const state = await control<PluginState>('state')
  await showLibrary()
  await openDetails(state.works[0].id)
  const copy = page.getByRole('tabpanel').locator('.avalon-copy')
  const inclusion = copy.getByText(label, { exact: true })
  const facts = copy.locator('.entry-actions > div:first-child > span')
  for (const size of [
    { width: 1920, height: 1080, scale: 1 },
    { width: 1280, height: 720, scale: 1.4 },
  ]) {
    await application!.evaluate(({ BrowserWindow }, size) => {
      BrowserWindow.getAllWindows()[0]!.setContentSize(size.width, size.height)
    }, size)
    await api({
      route: 'preferences.presentation.put',
      params: { preference: 'FullscreenTextScale' },
      body: { value: String(size.scale) },
    })
    await expect(facts).toHaveCount(2)
    for (const fact of await facts.all())
      await expect
        .poll(() => fact.evaluate((element) => Number.parseFloat(getComputedStyle(element).fontSize)))
        .toBeCloseTo(24 * size.scale, 1)
    await inclusion.scrollIntoViewIfNeeded()
    const text = await inclusion.evaluate((element) => {
      const range = document.createRange()
      range.selectNodeContents(element)
      const box = element.getBoundingClientRect()
      return {
        lines: [...range.getClientRects()].map((line) => ({ left: line.left, right: line.right })),
        left: box.left,
        right: box.right,
        whiteSpace: getComputedStyle(element).whiteSpace,
      }
    })
    expect(text.whiteSpace).not.toBe('nowrap')
    for (const line of text.lines) {
      expect(line.left).toBeGreaterThanOrEqual(text.left - 1)
      expect(line.right).toBeLessThanOrEqual(text.right + 1)
    }
    for (const container of [page.locator('.avalon-details'), page.locator('.avalon-details-reading'), copy])
      expect(await container.evaluate((element) => element.scrollWidth <= element.clientWidth + 1)).toBe(true)
    const play = copy.getByRole('button', { name: 'Play', exact: true })
    await play.focus()
    await expect(play).toBeFocused()
    await expect(play).toBeInViewport({ ratio: 1 })
    expect(
      await play.evaluate((element) => {
        const box = element.getBoundingClientRect()
        const viewport = element.closest('.avalon-details-reading')!.getBoundingClientRect()
        return (
          box.top >= viewport.top - 1 &&
          box.bottom <= viewport.bottom + 1 &&
          box.left >= viewport.left - 1 &&
          box.right <= viewport.right + 1
        )
      }),
    ).toBe(true)
    await page.screenshot({
      path: info.outputPath(`fullscreen-plugin-copy-${size.width}-${size.scale}.png`),
      animations: 'disabled',
    })
  }
  expect((await control<PluginState>('state')).calls).toEqual([])
})
