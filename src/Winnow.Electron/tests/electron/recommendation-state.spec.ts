import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Page,
  type Locator,
} from '@playwright/test'
import { execFile } from 'node:child_process'
import { promisify } from 'node:util'
import { mkdtemp, readFile } from 'node:fs/promises'
import { resolve, join } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import type { ApiRequest } from '../../src/shared/bridge'
import type { Mode } from '../../src/renderer/api/types'
import type { FeedSnapshot } from '../../src/renderer/api/types'

const artifacts = resolve('../..', '.tmp/task38119-fixture-artifacts')
const fixture = join(artifacts, 'bin/Winnow.Electron.Fixtures/debug/Winnow.Electron.Fixtures.dll')
let app: ElectronApplication, page: Page, directory: string, mode: Mode
let endpoint: { address: string; token: string }
const errors: string[] = []
const desktopShelves = () => page.locator('.avalon-discover .avalon-shelf')
const desktopCard = (workId: number) =>
  page.locator(`.avalon-feed-card-host:has([data-avalon-game="${workId}"])`)
const cover = (workId: number) =>
  page
    .locator(
      `${mode === 'fullscreen' ? '.avalon-retained-row[data-row-active="true"] ' : ''}[data-avalon-game="${workId}"]`,
    )
    .filter({ visible: true })
const hero = () => page.locator('.avalon-home-hero')
type FixtureState = {
  calls: {
    callId: number
    kind: string
    operation: string
    gateId?: string | null
    completed: boolean
    canceled: boolean
    releaseIds: number[]
  }[]
  surfacings: { releaseId: number; shelfId: string; surfacedOn: string }[]
  verdicts: { releaseId: number; kind: string; revokedAt?: string | null }[]
}
const state = () => control<FixtureState>('state')
const seen = async () => (await state()).surfacings
async function seed(kind: string, options: Record<string, unknown> = {}) {
  await navigate('Library')
  const result = await control<{ now: string }>('seed', { kind, ...options })
  await page.clock.setFixedTime(new Date(result.now))
  if (kind !== 'throwing')
    await expect(page.locator('.avalon-library [data-avalon-game]').first()).toBeAttached()
  return result
}
async function openFeed() {
  await navigate('For you')
  await expect(page.locator(mode === 'desktop' ? '.avalon-discover' : '.avalon-home')).toBeVisible()
}
async function selected(workId: number) {
  await cover(workId).focus()
  await expect(cover(workId)).toBeFocused()
  return mode === 'desktop' ? desktopCard(workId) : hero()
}
async function retain(name: string, target: Locator) {
  await target.evaluate((node, name) => {
    const host = window as unknown as { recommendationNodes?: Record<string, Element> }
    ;(host.recommendationNodes ??= {})[name] = node
  }, name)
}
async function retained(name: string, target: Locator) {
  return await target.evaluate(
    (node, name) =>
      (window as unknown as { recommendationNodes: Record<string, Element> }).recommendationNodes[name] ===
      node,
    name,
  )
}
async function noOsDispatch() {
  const actions = await app.evaluate(
    () => (globalThis as any).__identityProjections.dispatches as { kind: string; target: string }[],
  )
  expect(actions.filter((action) => action.kind !== 'entry')).toEqual([])
  return actions
}
async function lastViewportCard() {
  if (mode === 'desktop') {
    const target = desktopCard(11)
    await target.scrollIntoViewIfNeeded()
    await cover(11).focus()
    return target
  }
  await cover(1).focus()
  await tap(7)
  await expect(
    page.locator('.avalon-retained-row[data-row-active="true"] [data-avalon-game="7"]'),
  ).toBeVisible()
  for (let step = 0; step < 6; step++) await tap(15)
  await expect(cover(13)).toBeFocused()
  return hero()
}

async function control<T = unknown>(route: string, body?: unknown): Promise<T> {
  const response = await fetch(new URL(`/__fixture/recommendation-state/${route}`, endpoint.address), {
    method: body === undefined ? 'GET' : 'POST',
    headers: {
      Authorization: `Bearer ${endpoint.token}`,
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!response.ok) throw new Error(`Fixture ${route} failed: ${response.status} ${await response.text()}`)
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}
async function api<T = unknown>(request: ApiRequest): Promise<T> {
  const response = await page.evaluate((request) => window.winnow.request(request), request)
  if (!response.ok) throw Error(`${request.route}: ${response.status} ${response.message}`)
  return response.data as T
}
async function pad() {
  await page.evaluate(() => {
    const state = { pressed: [] as number[] }
    Object.assign(window, { recommendationPad: state })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          id: 'Native recommendation fixture controller',
          connected: true,
          mapping: 'standard',
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, index) => ({
            pressed: state.pressed.includes(index),
            touched: state.pressed.includes(index),
            value: state.pressed.includes(index) ? 1 : 0,
          })),
        },
      ],
    })
  })
}
async function tap(button: number) {
  for (const pressed of [[], [button], []])
    await page.evaluate(async (pressed) => {
      ;(window as unknown as { recommendationPad: { pressed: number[] } }).recommendationPad.pressed = pressed
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
}
async function activate(target: Locator, key = 'Enter') {
  await expect(target).toBeVisible()
  await target.focus()
  await expect(target).toBeFocused()
  if (mode === 'fullscreen') await tap(0)
  else await page.keyboard.press(key)
}
async function navigate(name: 'For you' | 'Library') {
  const nav = page.getByRole('navigation', { name: 'Main navigation', exact: true })
  if (
    test.info().title.includes('unshown window') &&
    !(await app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0]!.isVisible()))
  ) {
    await nav
      .getByRole('button', { name, exact: true })
      .evaluate((node) => (node as HTMLButtonElement).click())
    return
  }
  await activate(nav.getByRole('button', { name, exact: true }))
}
async function surface(width: number, height: number) {
  await app.evaluate(
    ({ BrowserWindow }, { width, height, mode }) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setFullScreen(false)
      window.setMinimumSize(0, 0)
      window.setContentSize(width, height)
      window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
      if (window.isVisible()) window.focus()
    },
    { width, height, mode },
  )
  await page.waitForFunction(({ width, height }) => innerWidth === width && innerHeight === height, {
    width,
    height,
  })
  if (mode === 'fullscreen') await pad()
  await expect(page.locator('.avalon-shell')).toHaveClass(mode === 'fullscreen' ? /fullscreen/ : /desktop/)
}
async function capture(name: string, target?: Locator) {
  if (target) await target.scrollIntoViewIfNeeded()
  await page.screenshot({ path: test.info().outputPath(`${mode}-${name}.png`) })
}
async function showWindow() {
  await app.evaluate(({ BrowserWindow }) => {
    ;(globalThis as any).__recommendationState.allowShow = true
    const window = BrowserWindow.getAllWindows()[0]!
    window.show()
    window.focus()
  })
  await expect
    .poll(() => page.evaluate(() => document.hasFocus() && document.visibilityState === 'visible'))
    .toBe(true)
}

test.beforeAll(async () => {
  test.setTimeout(120000)
  await promisify(execFile)(
    'dotnet',
    [
      'build',
      resolve('../..', 'tests/Winnow.Electron.Fixtures/Winnow.Electron.Fixtures.csproj'),
      '--artifacts-path',
      artifacts,
      '--nologo',
      '--verbosity',
      'quiet',
    ],
    { windowsHide: true, timeout: 115000 },
  ).catch((error: Error & { stdout?: string; stderr?: string }) => {
    throw Error(`${error.message}\n${error.stdout ?? ''}\n${error.stderr ?? ''}`)
  })
})
test.beforeEach(async ({}, info) => {
  test.setTimeout(90000)
  mode = info.title.startsWith('fullscreen') ? 'fullscreen' : 'desktop'
  errors.length = 0
  endpoint = undefined as unknown as typeof endpoint
  directory = await mkdtemp(
    join(
      resolve('../..', '.tmp'),
      `winnow-electron-recommendation-state-${info.title.includes('no feedback store') ? 'nofeedback-' : ''}`,
    ),
  )
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/recommendation-state-main.mjs'),
      '--data-dir',
      directory,
      '--no-sync',
      ...(info.title.includes('unshown window') ? ['--recommendation-state-hidden', '--background'] : []),
    ],
    env: {
      ...(Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ) as Record<string, string>),
      WINNOW_BACKEND_PATH: fixture,
    },
    chromiumSandbox: true,
    timeout: 60000,
  })
  page = await app.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible({ timeout: 45000 })
  endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
  expect((await fetch(new URL('/__fixture/recommendation-state/state', endpoint.address))).status).toBe(401)
  const cdp = await page.context().newCDPSession(page)
  await cdp.send('Emulation.setTimezoneOverride', { timezoneId: 'UTC' })
  await surface(1920, 1080)
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
})
test.afterEach(async ({}, info) => {
  try {
    if (endpoint)
      await info.attach('recommendation-fixture-ledger', {
        body: JSON.stringify(await control('state'), null, 2),
        contentType: 'application/json',
      })
    await info.attach('intercepted-action-dispatches', {
      body: JSON.stringify(await app.evaluate(() => (globalThis as any).__identityProjections.dispatches)),
      contentType: 'application/json',
    })
    await noOsDispatch()
    if (info.status !== info.expectedStatus && page && !page.isClosed()) {
      await info.attach('recommendation-before-teardown', {
        body: await page.screenshot(),
        contentType: 'image/png',
      })
      await info.attach('before-teardown-dom', {
        body: await page.locator('body').ariaSnapshot(),
        contentType: 'text/plain',
      })
    }
  } finally {
    if (endpoint) await control('release', {}).catch(() => {})
    await closeFixture(app, directory)
  }
  expect(errors).toEqual([])
})

for (const surfaceName of ['desktop', 'fullscreen']) {
  for (const installedSibling of [false, true])
    test(`${surfaceName} source cold composition keeps canonical title and ${installedSibling ? 'installed Epic' : 'Steam'} action impression and verdict release`, async () => {
      await seed('composition', { installedSibling })
      await openFeed()
      const scope = await selected(1)
      const expectedRelease = installedSibling ? 2 : 1
      const shelfId = installedSibling ? 'ready_to_play' : 'waiting_to_be_opened'
      const reason = await cover(1).getAttribute('aria-description')
      expect(reason?.trim()).toBeTruthy()
      expect(reason).not.toMatch(/bought|paid for/i)
      await expect(cover(1)).toHaveAttribute('aria-label', /Kept title/)
      const text = await page.locator(mode === 'desktop' ? '.avalon-discover' : '.avalon-home').innerText()
      expect(text).not.toMatch(/bought|paid for/i)
      await expect
        .poll(async () =>
          (await seen()).some((row) => row.releaseId === expectedRelease && row.shelfId === shelfId),
        )
        .toBe(true)
      expect((await seen()).every((row) => row.releaseId === expectedRelease)).toBe(true)
      const feed = await api<FeedSnapshot>({ route: 'feed.get' })
      expect(feed.shelves).toHaveLength(1)
      expect(feed.shelves[0].id).toBe(shelfId)
      expect(feed.shelves[0].items).toHaveLength(1)
      expect(feed.shelves[0].items[0]).toMatchObject({
        releaseId: expectedRelease,
        ownershipId: expectedRelease,
      })
      // The exact source has no launch identifiers. It proves chosen-entry provenance, not OS launchability.
      await expect(scope.getByRole('button', { name: /^(Play|Install)( Kept title)?$/ })).toHaveCount(0)
      expect(await noOsDispatch()).toEqual([])
      await activate(scope.getByRole('button', { name: 'Not interested', exact: true }))
      await expect(scope.getByRole('button', { name: 'Undo', exact: true })).toBeVisible()
      await expect
        .poll(async () =>
          (await state()).verdicts.filter((row) => !row.revokedAt).map((row) => row.releaseId),
        )
        .toEqual([expectedRelease])
      await activate(scope.getByRole('button', { name: 'Undo', exact: true }))
      await expect.poll(async () => (await state()).verdicts.filter((row) => !row.revokedAt)).toEqual([])
      await expect(scope.getByRole('button', { name: 'Not interested', exact: true })).toBeVisible()
      await capture(`composition-${installedSibling ? 'epic' : 'steam'}`)
    })

  test(`${surfaceName} source primary reason remains the exact one-sentence explanation`, async () => {
    await seed('primary')
    await openFeed()
    const reason = 'An update arrived after your last session.'
    await expect(cover(1)).toHaveAccessibleDescription(reason)
    if (mode === 'fullscreen') await expect(hero().locator(':scope > p')).toHaveText(reason)
    else await expect(desktopCard(1).locator('.avalon-feed-card-caption > span')).toHaveText(reason)
    await capture('primary-reason')
  })

  test(`${surfaceName} source recently played keeps Open game but has no verdict or impression`, async () => {
    await seed('recent')
    await openFeed()
    if (mode === 'desktop') {
      const recent = desktopShelves().filter({
        has: page.getByRole('heading', { name: 'Recently played', exact: true }),
      })
      const recommended = desktopShelves().filter({
        has: page.getByRole('heading', { name: 'Recommended', exact: true }),
      })
      const recentCover = recent.locator('[data-avalon-game="1"]')
      await recentCover.focus()
      await expect(recent.getByRole('button', { name: 'Not now', exact: true })).toHaveCount(0)
      await expect(recent.getByRole('button', { name: 'Not interested', exact: true })).toHaveCount(0)
      await recentCover.hover()
      const preview = page.getByRole('tooltip')
      await expect(preview).toBeVisible()
      await expect(preview.getByRole('button')).toHaveCount(0)
      await capture('recent-readonly-preview', preview)
      await page.keyboard.press('Escape')
      await expect(preview).toHaveCount(0)
      await recommended.locator('[data-avalon-game="1"]').focus()
      await expect(recommended.getByRole('button', { name: 'Not interested', exact: true })).toBeVisible()
    } else {
      await expect(page.locator('.avalon-home-shelf').getByRole('heading')).toHaveText('Recently played')
      await cover(1).focus()
      expect(await seen()).toEqual([])
      await tap(3)
      const actions = page.getByRole('dialog', { name: 'More game actions', exact: true })
      await expect(actions).toBeVisible()
      await expect(actions.getByRole('button', { name: 'Open game', exact: true })).toBeVisible()
      await expect(actions.getByRole('button', { name: 'Not now', exact: true })).toHaveCount(0)
      await expect(actions.getByRole('button', { name: 'Not interested', exact: true })).toHaveCount(0)
      await capture('recent-readonly-more', actions)
      await tap(1)
      await expect(actions).toHaveCount(0)
      await expect(cover(1)).toBeFocused()
      await tap(13)
      await expect(page.locator('.avalon-home-shelf').getByRole('heading')).toHaveText('Recommended')
      await tap(3)
      await expect(actions.getByRole('button', { name: 'Not interested', exact: true })).toBeVisible()
      await tap(1)
    }
    await expect
      .poll(async () => (await seen()).some((row) => row.releaseId === 1 && row.shelfId === 'recommended'))
      .toBe(true)
    expect((await seen()).some((row) => row.shelfId === 'recently_played')).toBe(false)
    expect((await state()).verdicts).toEqual([])
  })

  test(`${surfaceName} source optional shelf arrival preserves card identity focus and one impression`, async () => {
    await seed('optional')
    await openFeed()
    await expect(cover(1)).toHaveAccessibleDescription('Baseline reason')
    await cover(1).focus()
    await expect(cover(1)).toBeFocused()
    await retain('initial', cover(1))
    const name = await cover(1).getAttribute('aria-label')
    await expect.poll(async () => (await seen()).filter((row) => row.releaseId === 1).length).toBe(1)
    const held = (await state()).calls.find(
      (call) => call.operation === 'supplement' && call.gateId && !call.completed,
    )!
    expect(held).toBeTruthy()
    await control('release', { gateId: held.gateId })
    if (mode === 'desktop') {
      await expect(desktopShelves()).toHaveCount(2)
      await expect(desktopShelves().nth(1).getByRole('heading')).toHaveText('Optional')
    } else await expect(page.getByRole('button', { name: 'Show Optional', exact: true })).toBeVisible()
    expect(await retained('initial', cover(1))).toBe(true)
    await expect(cover(1)).toBeFocused()
    await expect(cover(1)).toHaveAttribute('aria-label', name!)
    expect((await seen()).filter((row) => row.releaseId === 1)).toHaveLength(1)
    await capture('optional-preserves-focused-card', cover(1))
  })

  test(`${surfaceName} source history takes the entire feed body and restores its card and undo state`, async () => {
    await seed('history')
    await openFeed()
    const scope = await selected(1)
    await retain('history-card', cover(1))
    await activate(scope.getByRole('button', { name: 'Not now', exact: true }))
    await expect(scope.getByRole('status')).toContainText('Back on')
    await activate(page.getByRole('button', { name: /What you've told the feed/ }))
    const history = page.locator('.avalon-feed-history')
    await expect(history).toBeVisible()
    await expect(history).toContainText('Deep Rock Galactic 1')
    await expect(history).toContainText('NOT NOW')
    await expect(page.locator('[data-avalon-game]:visible')).toHaveCount(0)
    await expect(page.getByText('997 games scored', { exact: true })).not.toBeVisible()
    await expect(page.locator('.avalon-empty:visible')).toHaveCount(0)
    await activate(history.getByRole('button', { name: 'Undo', exact: true }))
    await expect(history).toContainText('Undone on')
    const styles = await history.evaluate((node) => {
      const style = getComputedStyle(node)
      const metric = (selector: string) => {
        const value = getComputedStyle(node.querySelector(selector)!)
        return {
          size: parseFloat(value.fontSize),
          weight: value.fontWeight,
          family: value.fontFamily,
          minHeight: parseFloat(value.minHeight),
        }
      }
      const probe = document.createElement('span')
      probe.style.cssText =
        'position:absolute;width:calc(1000 * var(--feed-info-unit));font-size:calc(1px * var(--feed-info-text));font-family:var(--font-body)'
      node.append(probe)
      const computed = getComputedStyle(probe)
      const values = {
        unit: parseFloat(computed.width) / 1000,
        text: parseFloat(computed.fontSize),
        family: computed.fontFamily,
      }
      probe.remove()
      return {
        ...values,
        padding: [style.paddingTop, style.paddingRight, style.paddingBottom, style.paddingLeft].map(
          parseFloat,
        ),
        maxWidth: parseFloat(style.maxWidth),
        bodySize: parseFloat(style.fontSize),
        title: metric('h1'),
        game: metric('h3'),
        metadata: metric('li p'),
        label: metric('.avalon-label'),
        button: metric('button'),
      }
    })
    if (mode === 'desktop') expect(styles.padding).toEqual([24, 24, 32, 24])
    else {
      expect(styles.padding[0]).toBeCloseTo(20 * styles.unit, 1)
      expect(styles.maxWidth).toBeCloseTo(1320 * styles.unit, 1)
      expect(styles.bodySize).toBeCloseTo(24 * styles.unit * styles.text, 1)
      for (const heading of [styles.title, styles.game]) {
        expect(heading.size).toBeCloseTo(32 * styles.unit * styles.text, 1)
        expect(heading.weight).toBe('700')
        expect(heading.family).toBe(styles.family)
      }
      expect(styles.metadata.size).toBeCloseTo(22 * styles.unit * styles.text, 1)
      expect(styles.label.size).toBeCloseTo(18 * styles.unit * styles.text, 1)
      expect(styles.button.size).toBeCloseTo(28 * styles.unit * styles.text, 1)
      expect(styles.button.minHeight).toBeCloseTo(64 * styles.unit, 1)
      await expect(history.locator('.avalon-feed-history-hints')).toBeVisible()
      await expect(history.locator('.avalon-feed-history-hints')).toContainText(
        'A \u00b7 Select \u00b7 B \u00b7 Back to the feed',
      )
    }
    await capture('history-owns-body', history)
    await activate(history.getByRole('button', { name: 'Back to the feed', exact: true }))
    await expect(history).not.toBeVisible()
    expect(await retained('history-card', cover(1))).toBe(true)
    await selected(1)
    await expect(scope.getByRole('button', { name: 'Not now', exact: true })).toBeVisible()
    await expect(scope.getByRole('status')).toHaveCount(0)
    expect((await state()).verdicts.filter((row) => !row.revokedAt)).toEqual([])
    if (mode === 'desktop') await expect(page.getByText('997 games scored', { exact: true })).toBeVisible()
  })

  test(`${surfaceName} source no feedback store removes verdict controls while keeping real cards and actions`, async () => {
    await seed('history')
    await openFeed()
    const scope = await selected(1)
    await expect(cover(1)).toBeVisible()
    await expect(scope.getByRole('button', { name: 'Not now', exact: true })).toHaveCount(0)
    await expect(scope.getByRole('button', { name: 'Not interested', exact: true })).toHaveCount(0)
    await expect(scope.getByRole('button', { name: /Add to list/ })).toBeVisible()
    expect((await state()).verdicts).toEqual([])
    await capture('unwired-feedback-controls-absent')
  })

  test(`${surfaceName} source reserve fixture preserves survivors and keeps full-surface promotion semantics`, async () => {
    await seed('reserve')
    await openFeed()
    const scope = await selected(1)
    await retain('outgoing', cover(1))
    if (mode === 'desktop') {
      await expect(desktopShelves().first().locator('[data-avalon-game]')).toHaveCount(5)
      for (const id of [2, 3, 4, 5]) await retain(`survivor-${id}`, cover(id))
      await control('next', { kind: 'reserve-next' })
      await activate(scope.getByRole('button', { name: 'Not interested', exact: true }))
      await expect(scope.getByRole('button', { name: 'Undo', exact: true })).toBeFocused()
      await expect(scope.getByRole('progressbar')).toHaveAttribute('value', '0')
      await page.mouse.move(0, 0)
      await page.getByRole('heading', { name: 'For you', exact: true }).evaluate((node) => {
        node.setAttribute('tabindex', '-1')
        ;(node as HTMLElement).focus()
      })
      await expect(cover(101)).toBeVisible({ timeout: 10000 })
      await expect(cover(1)).toHaveCount(0)
      expect(await page.evaluate(() => (window as any).recommendationNodes.outgoing.isConnected)).toBe(false)
      for (const id of [2, 3, 4, 5]) expect(await retained(`survivor-${id}`, cover(id))).toBe(true)
      await expect(desktopShelves().first().locator('[data-avalon-game]')).toHaveCount(5)
      await expect
        .poll(async () =>
          (await seen()).some((row) => row.releaseId === 101 && row.shelfId === 'ready_to_play'),
        )
        .toBe(true)
      const second = await selected(2)
      await activate(second.getByRole('button', { name: 'Not interested', exact: true }))
      await page.mouse.move(0, 0)
      await page
        .getByRole('heading', { name: 'For you', exact: true })
        .evaluate((node) => (node as HTMLElement).focus())
      await expect(cover(2)).toHaveCount(0, { timeout: 10000 })
      await expect(desktopShelves().first().locator('[data-avalon-game]')).toHaveCount(5)
      for (const id of [3, 4, 5]) expect(await retained(`survivor-${id}`, cover(id))).toBe(true)
    } else {
      // Fullscreen exposes the scored reserve as part of its shelf, so it has no hidden101 replacement.
      await expect(
        page.locator('.avalon-retained-row[data-row-active="true"] [data-avalon-game]'),
      ).toHaveCount(6)
      await activate(scope.getByRole('button', { name: 'Not now', exact: true }))
      await expect(scope.getByRole('button', { name: 'Undo', exact: true })).toBeFocused()
      await expect(scope.getByRole('progressbar')).toHaveCount(0)
      expect(await retained('outgoing', cover(1))).toBe(true)
      await activate(scope.getByRole('button', { name: 'Undo', exact: true }))
      await expect(scope.getByRole('button', { name: 'Not now', exact: true })).toBeVisible()
    }
    await capture('reserve-preserves-slots')
  })

  test(`${surfaceName} source scrolled-card list picker fits the window and Escape returns to its action`, async () => {
    await surface(mode === 'desktop' ? 1024 : 1280, mode === 'desktop' ? 480 : 1080)
    await seed('viewport13')
    await openFeed()
    const scope = await lastViewportCard()
    const add = scope.getByRole('button', { name: /Add to list/ })
    if (mode === 'desktop') {
      const geometry = await scope.locator('.avalon-feed-card-actions').evaluate((node) => {
        const bounds = node.getBoundingClientRect()
        const buttons = [...node.querySelectorAll('button')].map((button) => button.getBoundingClientRect())
        return {
          within: buttons.every(
            (button) => button.left >= bounds.left - 1 && button.right <= bounds.right + 1,
          ),
          overlap: buttons.some((first, index) =>
            buttons.some(
              (second, other) =>
                other !== index &&
                first.left < second.right &&
                first.right > second.left &&
                first.top < second.bottom &&
                first.bottom > second.top,
            ),
          ),
        }
      })
      expect(geometry).toEqual({ within: true, overlap: false })
    }
    await retain('list-origin', add)
    await activate(add, 'Space')
    const dialog = page.getByRole('dialog', { name: /Add.*list/i })
    const input = dialog.getByRole('textbox', { name: 'New list name', exact: true })
    await expect(input).toBeFocused()
    const box = await input.boundingBox()
    expect(box).not.toBeNull()
    expect(box!.y).toBeGreaterThanOrEqual(0)
    expect(box!.y + box!.height).toBeLessThanOrEqual(await page.evaluate(() => innerHeight))
    if (mode === 'fullscreen') {
      const hints = dialog.locator('.list-prompt-controller-hints')
      await expect(hints).toBeVisible()
      for (const label of ['A \u00b7 Select', 'Y \u00b7 Keyboard', 'B \u00b7 Back'])
        await expect(hints).toContainText(label)
      await tap(3)
      const keyboard = page.getByRole('dialog', { name: 'Enter text', exact: true })
      await expect(keyboard).toBeVisible()
      await expect(keyboard.locator('.keyboard-hints')).toBeVisible()
      for (const glyph of ['A', 'B', 'X', 'RT'])
        await expect(keyboard.locator(`[data-keyboard-glyph="${glyph}"] svg`)).toBeVisible()
      await capture('scrolled-list-picker-keyboard', keyboard)
      await tap(1)
      await expect(keyboard).toHaveCount(0)
      await expect(input).toBeFocused()
      await expect(hints).toContainText('Y \u00b7 Keyboard')
    }
    await capture('scrolled-list-picker', input)
    await page.keyboard.press('Escape')
    await expect(dialog).toHaveCount(0)
    await expect(add).toBeFocused()
    expect(await retained('list-origin', add)).toBe(true)
  })

  test(`${surfaceName} source offscreen reserve waits for the real vertical and horizontal viewport`, async () => {
    await surface(mode === 'desktop' ? 800 : 1280, mode === 'desktop' ? 480 : 1080)
    await seed('viewport13')
    await openFeed()
    await expect.poll(async () => (await seen()).length).toBeGreaterThan(0)
    expect((await seen()).some((row) => [11, 12, 13].includes(row.releaseId))).toBe(false)
    if (mode === 'desktop') {
      // The source invokes an offscreen card command directly; activating its mounted handler must not scroll it into view.
      await desktopCard(11)
        .locator('button[aria-label="Not interested"]')
        .evaluate((node) => (node as HTMLButtonElement).click())
      await expect(desktopCard(13)).toBeAttached({ timeout: 10000 })
      await expect(desktopCard(11)).toHaveCount(0)
      expect((await seen()).some((row) => [12, 13].includes(row.releaseId))).toBe(false)
      await desktopShelves().nth(1).getByRole('heading').scrollIntoViewIfNeeded()
      await expect(cover(7)).toBeVisible()
      expect((await seen()).some((row) => [12, 13].includes(row.releaseId))).toBe(false)
      await desktopCard(13).scrollIntoViewIfNeeded()
    } else {
      // Fullscreen keeps13 in its complete scored row; entering a different shelf alone does not expose overflow.
      await cover(1).focus()
      await tap(7)
      await expect(cover(7)).toBeFocused()
      expect((await seen()).some((row) => row.releaseId === 13)).toBe(false)
      for (let step = 0; step < 6; step++) await tap(15)
      await expect(cover(13)).toBeFocused()
    }
    await expect
      .poll(async () => (await seen()).some((row) => row.releaseId === 13 && row.shelfId === 'shelf-1'))
      .toBe(true)
    expect((await seen()).filter((row) => row.releaseId === 13)).toHaveLength(1)
    await capture('viewport-final-card', cover(13))
  })

  test(`${surfaceName} source unshown window starts observation when the actual window is shown`, async () => {
    await surface(mode === 'desktop' ? 800 : 1280, mode === 'desktop' ? 480 : 1080)
    await seed('viewport13')
    await openFeed()
    expect(await app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0]!.isVisible())).toBe(
      false,
    )
    expect(await seen()).toEqual([])
    await showWindow()
    await expect.poll(async () => (await seen()).length).toBeGreaterThan(0)
    await capture('unshown-window-observation-started')
  })

  test(`${surfaceName} source service failure is a recoverable sentence without feed cards`, async () => {
    await seed('throwing')
    await navigate('For you')
    await expect(page.locator('.avalon-empty')).toContainText('Recommendations could not be loaded.')
    await expect(page.locator('[data-avalon-game]:visible')).toHaveCount(0)
    const retry = page.getByRole('button', { name: 'Try again', exact: true })
    await expect(retry).toBeEnabled()
    await capture('service-failure-retry', retry)
  })

  test(`${surfaceName} source five shelves keep engine order and their own exact pitches`, async () => {
    await seed('five')
    await openFeed()
    await expect(cover(1)).toBeVisible()
    const snapshot = await api<FeedSnapshot>({ route: 'feed.get' })
    expect(snapshot.failed).toBe(false)
    expect(snapshot.shelves.map((shelf) => shelf.id)).toEqual([
      'patched_while_away',
      'worth_another_look',
      'ready_to_play',
      'barely_touched',
      'on_your_taste',
    ])
    expect(snapshot.shelves.map((shelf) => shelf.blurb)).toEqual([
      'Major updates landed after you stopped playing.',
      'You committed real hours past the refund line, then drifted off mid-story.',
      'Already on your disk with nothing sunk.',
      'Under 2 hours in — you opened the door and never walked through.',
      'Sitting sealed in your library, and it matches where your hours actually go.',
    ])
    if (mode === 'desktop') {
      await expect(desktopShelves().locator('h2')).toHaveText(snapshot.shelves.map((shelf) => shelf.title))
      await expect(desktopShelves().locator(':scope > header > p')).toHaveText(
        snapshot.shelves.map((shelf) => shelf.blurb),
      )
    } else {
      for (const [index, shelf] of snapshot.shelves.entries()) {
        await expect(page.locator('.avalon-home-shelf h2')).toHaveText(shelf.title)
        await expect(hero().locator(':scope > p')).toHaveText(shelf.items[0].reason)
        if (index < snapshot.shelves.length - 1) await tap(7)
      }
    }
    await expect(page.locator('.avalon-empty:visible')).toHaveCount(0)
    await capture('five-engine-shelves')
  })
}

for (const supplemental of [false, true])
  test(`desktop source ${supplemental ? 'supplemental' : 'built-in'} excess item6 leads reserve7 and8 without an unseen impression`, async () => {
    await surface(800, 480)
    await seed('excess8', { supplemental })
    await openFeed()
    await expect(desktopShelves()).toHaveCount(1)
    const cards = desktopShelves().first().locator('[data-avalon-game]')
    await expect(cards).toHaveCount(5)
    expect(
      await cards.evaluateAll((nodes) => nodes.map((node) => Number(node.getAttribute('data-avalon-game')))),
    ).toEqual([1, 2, 3, 4, 5])
    for (const id of [1, 2, 3, 4, 5]) {
      await cover(id).scrollIntoViewIfNeeded()
      await expect.poll(async () => (await seen()).some((row) => row.releaseId === id)).toBe(true)
    }
    expect((await seen()).map((row) => row.releaseId).sort((a, b) => a - b)).toEqual([1, 2, 3, 4, 5])
    await desktopCard(1)
      .locator('button[aria-label="Not interested"]')
      .evaluate((node) => (node as HTMLButtonElement).click())
    await expect(desktopCard(6)).toBeAttached({ timeout: 10000 })
    expect((await seen()).some((row) => row.releaseId >= 6)).toBe(false)
    await expect(cards).toHaveCount(5)
    expect(
      await cards.evaluateAll((nodes) => nodes.map((node) => Number(node.getAttribute('data-avalon-game')))),
    ).toEqual([6, 2, 3, 4, 5])
    await cover(6).scrollIntoViewIfNeeded()
    await expect.poll(async () => (await seen()).some((row) => row.releaseId === 6)).toBe(true)
    expect((await seen()).some((row) => [7, 8].includes(row.releaseId))).toBe(false)
    await capture(`excess-${supplemental ? 'optional' : 'builtin'}-sixth-promotion`, cover(6))
  })

for (const surfaceName of ['desktop', 'fullscreen']) {
  for (const invalidations of [1, 3])
    test(`${surfaceName} source held initial read replays ${invalidations} invalidation${invalidations === 1 ? '' : 's'} as one final read`, async () => {
      await seed(invalidations === 1 ? 'reserve-invalidation' : 'invalidation', { holdBuiltin: true })
      await navigate('For you')
      const reads = async () =>
        (await state()).calls.filter((call) => call.operation === 'builtin' && call.kind !== 'startup')
      await expect
        .poll(async () => (await reads()).filter((call) => call.gateId && !call.completed).length)
        .toBe(1)
      const held = (await reads()).find((call) => call.gateId && !call.completed)!
      expect(await reads()).toHaveLength(1)
      // The shell has already settled its startup-empty snapshot. This first scenario read preserves that truth;
      // cold-query loading is covered separately, while the source replay contract is the two calls and final cards.
      if (invalidations === 3) await control('next', { kind: 'final' })
      await control('publish', { count: invalidations })
      expect(await reads()).toHaveLength(1)
      await control('release', { gateId: held.gateId })
      await expect.poll(async () => (await reads()).filter((call) => call.completed).length).toBe(2)
      if (invalidations === 1) {
        await expect(cover(1)).toHaveAttribute('aria-label', /Shown 1/)
        await expect(cover(2)).toHaveAttribute('aria-label', /Shown 2/)
        await expect(page.locator('[data-avalon-game]:visible')).toHaveCount(2)
      } else {
        await expect(cover(99)).toHaveAttribute('aria-label', /Deep Rock Galactic 99/)
        await expect(cover(99)).toHaveAccessibleDescription('The final library state.')
        await expect(page.locator('[data-avalon-game]:visible')).toHaveCount(1)
        await expect(page.getByRole('heading', { name: 'Installed and waiting', exact: true })).toBeVisible()
      }
      expect(await reads()).toHaveLength(2)
      await capture(`initial-read-${invalidations}-invalidations`)
    })

  for (const change of ['reload', 'verdict', 'dispose'])
    test(`${surfaceName} source pending optional shelf cannot publish after ${change}`, async () => {
      await seed('optional-stale')
      await openFeed()
      const scope = await selected(1)
      await expect(cover(1)).toHaveAccessibleDescription('Baseline')
      const calls = async () => (await state()).calls
      await expect
        .poll(
          async () =>
            (await calls()).filter(
              (call) => call.operation === 'supplement' && call.gateId && !call.completed,
            ).length,
        )
        .toBe(1)
      const held = (await calls()).find(
        (call) => call.operation === 'supplement' && call.gateId && !call.completed,
      )!
      expect(held.releaseIds).toEqual([2])
      const builtins = (await calls()).filter((call) => call.operation === 'builtin').length
      await control('next', { kind: 'builtin' })
      if (change === 'reload') {
        await control('publish', { count: 1 })
        await expect
          .poll(
            async () =>
              (await calls()).filter((call) => call.operation === 'builtin' && call.completed).length,
          )
          .toBeGreaterThan(builtins)
      } else if (change === 'verdict') {
        await activate(scope.getByRole('button', { name: 'Not interested', exact: true }))
        await expect(scope.getByRole('button', { name: 'Undo', exact: true })).toBeVisible()
        await expect
          .poll(async () =>
            (await state()).verdicts.filter((row) => !row.revokedAt).map((row) => row.releaseId),
          )
          .toEqual([1])
      } else {
        await capture('optional-before-owner-disposal')
        await app.evaluate(({ BrowserWindow }) => {
          const primary = BrowserWindow.getAllWindows()[0]!
          // Keep the backend alive after the tested renderer owner is destroyed so its real cancellation can be observed.
          new BrowserWindow({
            show: false,
            webPreferences: { sandbox: true, contextIsolation: true, nodeIntegration: false },
          })
          primary.destroy()
        })
        await expect.poll(() => page.isClosed()).toBe(true)
        await expect
          .poll(async () => (await calls()).find((call) => call.callId === held.callId)?.canceled)
          .toBe(true)
      }
      await control('release', { gateId: held.gateId })
      await expect
        .poll(async () => (await calls()).find((call) => call.callId === held.callId)?.completed)
        .toBe(true)
      if (change === 'dispose') {
        expect((await seen()).some((row) => row.releaseId === 2)).toBe(false)
        return
      }
      await expect(page.getByRole('heading', { name: 'Extra', exact: true })).toHaveCount(0)
      await expect(page.getByRole('button', { name: 'Show Extra', exact: true })).toHaveCount(0)
      await expect(cover(2)).toHaveCount(0)
      await expect(cover(1)).toBeVisible()
      expect((await seen()).some((row) => row.releaseId === 2)).toBe(false)
      await capture(`retired-optional-${change}`)
    })
}
