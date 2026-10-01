import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Locator,
  type Page,
} from '@playwright/test'
import electronPath from 'electron'
import { mkdir, mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { prebuiltActivationHelper, prebuiltFixture } from './prebuilt-backend'
import { closeFixture } from './fixture-cleanup'
import { profileDirectory } from '../../src/main/storage'
import { DEFAULT_PROFILE } from '../../src/shared/theme'

let application: ElectronApplication, page: Page, directory: string
let endpoint: { address: string; token: string }
let mode: 'desktop' | 'fullscreen'
const errors: string[] = []
const tile = (id = 1) => page.locator(`.avalon-library [data-avalon-game="${id}"]`)
const details = () => page.locator('.avalon-details')
const selection = () => page.getByRole('group', { name: 'Selected games', exact: true })
const options = () => page.getByRole('dialog', { name: 'Library options', exact: true })
const mutations = async () =>
  (await nativeState()).requests.filter((call: any) => /\/(acknowledge|restore)-updates$/.test(call.path))
async function control(action: string, body?: unknown): Promise<any> {
  const response = await fetch(new URL(`/__fixture/update-acknowledgement/${action}`, endpoint.address), {
    method: body === undefined ? 'GET' : 'POST',
    headers: { Authorization: `Bearer ${endpoint.token}`, 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(15000),
  })
  if (!response.ok) throw Error(`Acknowledgement fixture ${action}: ${response.status}`)
  return response.status === 204 ? null : response.json()
}
async function api(route: string, params?: Record<string, number>): Promise<any> {
  return page.evaluate(
    async ({ route, params }) => {
      const response = await window.winnow.request({ route, params })
      if (!response.ok || !response.data) throw Error(`Failed actual API: ${route}`)
      return response.data
    },
    { route, params },
  )
}
const nativeState = () => application.evaluate(() => (globalThis as any).__updateAcknowledgement)
async function tap(button: number) {
  for (const pressed of [[], [button], []])
    await page.evaluate(async (pressed) => {
      ;(window as any).__ackPad.pressed = pressed
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
}
async function activate(target: Locator) {
  await expect(target).toBeEnabled()
  await target.scrollIntoViewIfNeeded()
  await target.focus()
  await expect(target).toBeFocused()
  if (mode === 'fullscreen') await tap(0)
  else await page.keyboard.press('Enter')
}
async function back() {
  if (mode === 'fullscreen') await tap(1)
  else await page.keyboard.press('Escape')
}
async function navigate(name: string) {
  await activate(
    page.getByRole('navigation', { name: 'Main navigation' }).getByRole('button', { name, exact: true }),
  )
}
async function feed(read: boolean) {
  await navigate('For you')
  const card = page.locator('.avalon-feed-body [data-avalon-game="1"]').first()
  await expect(card).toBeVisible()
  if (read) await expect(card).not.toHaveAccessibleName(/patched since you played/)
  else await expect(card).toHaveAccessibleName(/patched since you played: 2 updates/)
  const snapshot = await api('feed.get')
  expect(snapshot.candidateCount).toBe(2)
  const patched = snapshot.shelves.find((shelf: any) => shelf.id === 'patched_while_away')
  if (read) {
    expect(patched).toBeUndefined()
    expect(snapshot.shelves.find((shelf: any) => shelf.id === 'worth_another_look').items).toEqual([
      expect.objectContaining({
        ownershipId: 1,
        releaseId: 1,
        reason: '20 hours played, last opened in 2025.',
      }),
    ])
  } else
    expect(patched.items).toEqual([
      expect.objectContaining({ ownershipId: 1, releaseId: 1, reason: '2 updates since you last played.' }),
    ])
  await capture(read ? 'feed-after-reading-group' : 'feed-after-restoring-group', card)
}
async function closeOptions() {
  if (mode === 'fullscreen' && (await options().isVisible())) {
    await back()
    await expect(options()).toHaveCount(0)
  }
}
async function bucket(name: 'All games' | 'Patched') {
  await closeOptions()
  await activate(
    page
      .getByRole('group', { name: 'Library collections' })
      .getByRole('button', { name: new RegExp(`^${name}`) }),
  )
}
async function contextFor(id = 1) {
  if (mode === 'fullscreen') {
    await tile(id).focus()
    await expect(tile(id)).toBeFocused()
    await tap(3)
    await expect(options()).toBeVisible()
  } else await tile(id).click({ button: 'right' })
}
async function seed(kind: string) {
  await control('seed', { kind })
  await navigate('Library')
  await expect(tile()).toBeVisible()
  await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(kind === 'multiple' ? 4 : 2)
  await expect(tile()).toHaveAccessibleName(/patched since you played: 2 updates/)
  await expect(tile(2)).not.toHaveAccessibleName(/patched since/)
}
async function workUnread(id: number) {
  const workspace = await api('library.workspace')
  const rows = workspace.buckets.filter((row: any) => row.resolvedWorkId === id)
  expect(rows.length).toBeGreaterThan(0)
  return Math.max(...rows.map((row: any) => row.game.unreadUpdateCount))
}
async function acknowledgements() {
  return (await api('game.details', { workId: 1 })).acknowledgements as Record<string, string>
}
async function expectWatermarks() {
  const saved = await acknowledgements()
  expect(Object.keys(saved).map(Number).sort()).toEqual([1, 2])
  expect(Date.parse(saved[1])).toBe(Date.parse('2026-06-01T00:00:00Z'))
  expect(Date.parse(saved[2])).toBe(Date.parse('2026-08-01T00:00:00Z'))
}
async function openDetails() {
  await closeOptions()
  await activate(tile())
  await expect(details().locator('h1')).toHaveText('Grouped game')
}
async function closeDetails() {
  if (mode === 'fullscreen') await back()
  else await activate(details().getByRole('button', { name: 'Close game details', exact: true }))
  await expect(details()).toHaveCount(0)
  await expect(tile()).toBeFocused()
}
async function updates() {
  await activate(details().getByRole('tab', { name: /^Updates/ }))
  await expect(details().locator('.update-gap-caption')).toBeVisible()
}
async function history(read: boolean) {
  if (mode === 'desktop') await activate(details().getByRole('tab', { name: 'Activity', exact: true }))
  else {
    await activate(details().getByRole('tab', { name: 'Overview', exact: true }))
    await activate(details().getByRole('button', { name: 'Play history \u2192', exact: true }))
  }
  const tracker = page.locator('.activity-tracker')
  await expect(tracker).toBeVisible()
  for (const id of [1, 2]) {
    await tracker.getByRole('combobox', { name: 'Edition history', exact: true }).selectOption(String(id))
    await expect(tracker.locator('.activity-update-summary')).toHaveText(
      read ? 'No unread updates' : `${id === 1 ? 3 : 2} unread updates`,
    )
    if (read) await expect(tracker.locator('.activity-timeline-update[data-unread="true"]')).toHaveCount(0)
  }
  await capture(read ? 'timeline-read' : 'timeline-unread', tracker.locator('.activity-update-summary'))
  if (mode === 'fullscreen') await back()
}
async function capture(name: string, subject?: Locator) {
  if (subject) {
    await subject.scrollIntoViewIfNeeded()
    await expect(subject).toBeInViewport()
  }
  if (mode === 'fullscreen' && !(await details().count()) && !(await options().count())) {
    await expect(page.locator('[data-root-bumper="LB"] svg')).toBeVisible()
    await expect(page.locator('[data-root-bumper="RB"] svg')).toBeVisible()
  }
  await page.screenshot({ path: test.info().outputPath(`${mode}-${name}.png`), animations: 'disabled' })
}
test.beforeAll(async () => {
  await Promise.all([readFile(prebuiltFixture), readFile(prebuiltActivationHelper)])
})
test.beforeEach(async ({}, info) => {
  application = undefined!
  page = undefined!
  endpoint = undefined!
  errors.length = 0
  mode = info.title.startsWith('fullscreen') ? 'fullscreen' : 'desktop'
  directory = await mkdtemp(join(resolve('../..', '.tmp'), `winnow-electron-update-acknowledgement-${mode}-`))
  const profile = structuredClone(DEFAULT_PROFILE)
  profile.appearance.scale = 100
  profile.appearance.reducedMotion = true
  const profileRoot = profileDirectory(join(directory, 'electron-userdata'), directory)
  await mkdir(profileRoot, { recursive: true })
  await writeFile(join(profileRoot, 'preferences.json'), JSON.stringify(profile))
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/update-acknowledgement-main.mjs'),
      '--data-dir',
      directory,
      '--no-sync',
      '--force-color-profile=srgb',
    ],
    env: {
      ...(Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ) as Record<string, string>),
      WINNOW_BACKEND_PATH: prebuiltFixture,
      WINNOW_ACTIVATION_HELPER_PATH: prebuiltActivationHelper,
    },
    chromiumSandbox: true,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.locator('.avalon-shell')).toBeVisible({ timeout: 30000 })
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
  expect((await fetch(new URL('/__fixture/update-acknowledgement/state', endpoint.address))).status).toBe(401)
  await application.evaluate(({ BrowserWindow }, mode) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setMinimumSize(0, 0)
    window.setContentSize(1920, 1080)
    window.isFullScreen = () => mode === 'fullscreen'
    window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
    window.focus()
  }, mode)
  await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
  await page.evaluate(() => {
    const state = ((window as any).__ackPad = { pressed: [] as number[] })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          id: 'Acknowledgement standard simulated controller',
          connected: true,
          mapping: 'standard',
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, i) => ({
            pressed: state.pressed.includes(i),
            touched: state.pressed.includes(i),
            value: state.pressed.includes(i) ? 1 : 0,
          })),
        },
      ],
    })
    window.dispatchEvent(new Event('gamepadconnected'))
  })
})
test.afterEach(async () => {
  try {
    if (endpoint)
      await test.info().attach('acknowledgement-backend', {
        body: JSON.stringify(await control('state')),
        contentType: 'application/json',
      })
    if (application)
      await test.info().attach('acknowledgement-native', {
        body: JSON.stringify(await nativeState()),
        contentType: 'application/json',
      })
    if (test.info().status !== test.info().expectedStatus && page && !page.isClosed())
      await capture('before-teardown')
  } finally {
    await closeFixture(application, directory)
  }
  expect(errors).toEqual([])
})

for (const surface of ['desktop', 'fullscreen'] as const) {
  test(`${surface} Patched-only actions acknowledge grouped and multiple selections leave unselected releases and admit later patches`, async () => {
    await seed('multiple')
    await contextFor()
    await expect(selection().getByRole('button', { name: 'Mark as read', exact: true })).toHaveCount(0)
    await closeOptions()
    await bucket('Patched')
    await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(3)
    const clear = selection().getByRole('button', { name: 'Clear selection', exact: true })
    if (await clear.isVisible()) await activate(clear)
    await tile().click({ modifiers: ['Control'] })
    await tile(4).click({ modifiers: ['Control'] })
    await contextFor()
    await expect(selection()).toContainText('2 selected')
    const mark = selection().getByRole('button', { name: 'Mark as read', exact: true })
    await capture('patched-selection-actions', mark)
    await activate(mark)
    await expect.poll(() => workUnread(1)).toBe(0)
    await expect.poll(() => workUnread(4)).toBe(0)
    await expect.poll(() => workUnread(5)).toBe(1)
    expect((await mutations()).map((call: any) => Number(call.path.split('/')[4])).sort()).toEqual([1, 2, 4])
    await expectWatermarks()
    await closeOptions()
    await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(1)
    await expect(tile(5)).toHaveAccessibleName(/patched since you played: 1 update/)
    await control('change', { stage: 'later-push', publish: true })
    await expect(tile()).toHaveAccessibleName(/patched since you played: 1 update/)
    await expect(tile(4)).toHaveCount(0)
    await expect(tile(5)).toBeVisible()
    await capture('only-later-and-unselected-remain')
  })

  test(`${surface} selected library snapshot excludes a push inserted before acknowledgement without losing the newer unread patch`, async () => {
    await seed('grouped')
    await bucket('Patched')
    await expect(tile()).toHaveAccessibleName(/patched since you played: 2 updates/)
    await contextFor()
    await control('change', { stage: 'later-push' })
    // The fullscreen options modal correctly removes the underlying grid from the accessibility tree.
    await expect(tile()).toHaveAttribute('aria-label', /patched since you played: 2 updates/)
    await activate(selection().getByRole('button', { name: 'Mark as read', exact: true }))
    await expect.poll(() => workUnread(1)).toBe(1)
    await expectWatermarks()
    const calls = await mutations()
    expect(calls).toHaveLength(2)
    for (const call of calls) expect(call.body.observedEventIds).not.toContain(13)
    await closeOptions()
    await expect(tile()).toHaveAccessibleName(/patched since you played: 1 update/)
    await capture('new-push-remains-unread', tile())
  })

  test(`${surface} Details marks and restores displayed releases across timeline library and actual feed refresh`, async () => {
    await seed('grouped')
    await feed(false)
    await expect
      .poll(
        async () =>
          (await nativeState()).requests.filter((call: any) => call.path === '/api/v1/feed' && call.completed)
            .length,
      )
      .toBeGreaterThan(0)
    await navigate('Library')
    await openDetails()
    await history(false)
    await updates()
    await expect(details().locator('.update-row[data-unread="true"]')).toHaveCount(6)
    await expect(details().locator('.update-gap-caption')).toHaveText('2 updates landed while you were away.')
    await activate(details().getByRole('button', { name: 'Mark as read', exact: true }))
    await expect(details().getByRole('button', { name: 'Show it again', exact: true })).toBeEnabled()
    await expect(details().locator('.update-row[data-unread="true"]')).toHaveCount(0)
    await expectWatermarks()
    await expect.poll(() => workUnread(1)).toBe(0)
    await expect
      .poll(async () =>
        (await nativeState()).requests
          .filter((call: any) => call.path === '/api/v1/feed' && call.completed)
          .at(-1)
          ?.result.shelves.some((shelf: any) => shelf.id === 'patched_while_away'),
      )
      .toBe(false)
    await capture(
      'details-read-restorable',
      details().getByRole('button', { name: 'Show it again', exact: true }),
    )
    await history(true)
    await updates()
    await activate(details().getByRole('button', { name: 'Show it again', exact: true }))
    await expect(details().locator('.update-row[data-unread="true"]')).toHaveCount(6)
    await expect.poll(() => workUnread(1)).toBe(2)
    expect(await acknowledgements()).toEqual({})
    await closeDetails()
    await expect(tile()).toHaveAccessibleName(/patched since you played: 2 updates/)
    await feed(false)
    expect((await mutations()).map((call: any) => call.path.endsWith('restore-updates'))).toEqual([
      false,
      false,
      true,
      true,
    ])
    // Reenter from the actual feed to verify its mounted cards after a real write.
    await activate(page.locator('.avalon-feed-body [data-avalon-game="1"]').first())
    await updates()
    await activate(details().getByRole('button', { name: 'Mark as read', exact: true }))
    await expect(details().getByRole('button', { name: 'Show it again', exact: true })).toBeEnabled()
    if (mode === 'fullscreen') await back()
    else await activate(details().getByRole('button', { name: 'Close game details', exact: true }))
    await expect(details()).toHaveCount(0)
    await feed(true)
  })

  test(`${surface} Details opening watermark leaves a later push unread and reopening reads only its release`, async () => {
    await seed('grouped')
    await openDetails()
    await updates()
    await expect(details().locator('.update-row[data-unread="true"]')).toHaveCount(6)
    await control('change', { stage: 'later-push' })
    await activate(details().getByRole('button', { name: 'Mark as read', exact: true }))
    await expect.poll(() => workUnread(1)).toBe(1)
    await expectWatermarks()
    for (const call of await mutations()) {
      expect(call.body.observedEventIds).not.toContain(13)
      expect(call.body.observedEventIds).not.toContain(14)
    }
    await expect(details().getByRole('button', { name: 'Mark as read', exact: true })).toBeEnabled()
    await closeDetails()
    await openDetails()
    await updates()
    await expect(details().getByRole('tab', { name: 'Updates, 1 unread update', exact: true })).toBeVisible()
    await expect(details().locator('.update-row[data-unread="true"]')).toHaveCount(2)
    const snapshot = await api('game.details', { workId: 1 })
    expect(snapshot.events.find((event: any) => event.id === 13)).toMatchObject({
      releaseId: 1,
      kind: 'build_push',
    })
    await capture(
      'reopened-later-release',
      details().getByRole('button', { name: 'Mark as read', exact: true }),
    )
    await activate(details().getByRole('button', { name: 'Mark as read', exact: true }))
    await expect(details().getByRole('button', { name: 'Show it again', exact: true })).toBeEnabled()
    const calls = await mutations()
    expect(calls).toHaveLength(3)
    expect(calls[2].path).toBe('/api/v1/releases/1/acknowledge-updates')
    expect(calls[2].body.observedEventIds).toContain(13)
    await expect.poll(() => workUnread(1)).toBe(0)
  })

  test(`${surface} refused selection preserves unread games and partial retry writes only the remaining release`, async () => {
    await seed('failure')
    await bucket('Patched')
    await contextFor()
    const mark = selection().getByRole('button', { name: 'Mark as read', exact: true })
    await activate(mark)
    await expect(page.getByRole('alert')).toHaveText("Couldn't mark every patch read. Try again.")
    if (mode === 'fullscreen') {
      const metrics = await page.getByRole('alert').evaluate((element) => {
        const style = getComputedStyle(element)
        const bounds = element.getBoundingClientRect()
        return {
          fontSize: Number.parseFloat(style.fontSize),
          themeTextScale: Number.parseFloat(style.getPropertyValue('--theme-text-scale')) || 1,
          fullscreenTextScale: Number.parseFloat(style.getPropertyValue('--fullscreen-text-scale')) || 1,
          bounds: { x: bounds.x, y: bounds.y, width: bounds.width, height: bounds.height },
          viewport: { width: innerWidth, height: innerHeight },
        }
      })
      await test.info().attach('fullscreen-refusal-typography', {
        body: JSON.stringify(metrics),
        contentType: 'application/json',
      })
      expect(metrics.fontSize).toBeGreaterThanOrEqual(
        28 * metrics.themeTextScale * metrics.fullscreenTextScale,
      )
    }
    await expect(mark).toBeEnabled()
    expect(await acknowledgements()).toEqual({})
    expect(await workUnread(1)).toBe(2)
    await capture('refused-selection-still-unread', page.getByRole('alert'))
    await control('change', { stage: 'refuse-release2' })
    const before = (await mutations()).length
    await activate(mark)
    await expect(page.getByRole('alert')).toHaveText("Couldn't mark every patch read. Try again.")
    await expect(mark).toBeEnabled()
    expect(Object.keys(await acknowledgements())).toEqual(['1'])
    expect(await workUnread(1)).toBe(2)
    const partial = (await mutations()).slice(before)
    expect(partial.map((call: any) => call.path).sort()).toEqual([
      '/api/v1/releases/1/acknowledge-updates',
      '/api/v1/releases/2/acknowledge-updates',
    ])
    await capture('partial-selection-retry', mark)
    await control('change', { stage: 'clear-refusal' })
    const retryStart = (await mutations()).length
    await activate(mark)
    await expect.poll(() => workUnread(1)).toBe(0)
    await expectWatermarks()
    expect((await mutations()).slice(retryStart).map((call: any) => call.path)).toEqual([
      '/api/v1/releases/2/acknowledge-updates',
    ])
    await expect(page.getByRole('alert')).toHaveCount(0)
    await closeOptions()
    await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(0)
    await capture('retry-removes-read-group')
  })
}
