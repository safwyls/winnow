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
import { mkdir, mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { resolve, join } from 'node:path'
import { build } from 'esbuild'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import type { ApiRequest } from '../../src/shared/bridge'
import type { GameDetails, Mode } from '../../src/renderer/api/types'

const artifacts = resolve('../..', '.tmp/task38116-fixture-artifacts')
const fixture = join(artifacts, 'bin/Winnow.Electron.Fixtures/debug/Winnow.Electron.Fixtures.dll')
const probeDirectory = resolve('../..', '.tmp/task38116-native-probe')
let app: ElectronApplication, page: Page, directory: string, mode: Mode, workId: number
let endpoint: { address: string; token: string }
const errors: string[] = []
const details = () => page.locator('.avalon-details')
const tracker = () => page.locator('.activity-tracker')
const plot = () => page.locator('.activity-timeline-plot')
const bars = () => plot().locator('.activity-timeline-bar')
const updates = () => plot().locator('.activity-timeline-update')

async function api<T>(input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const response = await window.winnow.request(input)
    if (!response.ok) throw Error(`${input.route}: ${response.status} ${response.message}`)
    return response.data
  }, input) as Promise<T>
}
async function control<T>(name: string, body?: unknown): Promise<T> {
  const response = await fetch(new URL(`/__fixture/activity-remaining/${name}`, endpoint.address), {
    method: body === undefined ? 'GET' : 'POST',
    headers: { Authorization: `Bearer ${endpoint.token}`, 'Content-Type': 'application/json' },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    signal: AbortSignal.timeout(120000),
  })
  if (!response.ok) throw Error(`Activity fixture ${name}: HTTP${response.status}`)
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}
async function tap(button: number) {
  // Standard Gamepad API frames, not a physical-device test.
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
async function activate(element: Locator) {
  await element.scrollIntoViewIfNeeded()
  await element.focus()
  await expect(element).toBeFocused()
  if (mode === 'fullscreen') await tap(0)
  else await page.keyboard.press('Enter')
}
async function surface(width = 1920, height = 1080) {
  await app.evaluate(
    ({ BrowserWindow }, value) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setFullScreen(false)
      window.setMinimumSize(0, 0)
      window.setContentSize(value.width, value.height)
      window.isFullScreen = () => value.mode === 'fullscreen'
      window.webContents.send('winnow:fullscreen:changed', value.mode === 'fullscreen')
      window.focus()
    },
    { mode, width, height },
  )
  await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
  if (mode === 'fullscreen') await tap(-1)
}
async function navigate(name: string) {
  await activate(
    page.getByRole('navigation', { name: 'Main navigation' }).getByRole('button', { name, exact: true }),
  )
}
async function seed(kind: string) {
  const result = await control<{ workId: number }>('seed', { kind })
  workId = result.workId
  await navigate('Library')
  await expect(page.locator(`.avalon-library [data-avalon-game="${workId}"]`)).toBeVisible()
}
async function openDetails() {
  await activate(page.locator(`.avalon-library [data-avalon-game="${workId}"]`))
  await expect(details().locator('h1')).toBeVisible()
}
async function openHistory() {
  if (mode === 'desktop') await activate(details().getByRole('tab', { name: 'Activity', exact: true }))
  else await activate(details().getByRole('button', { name: 'Play history →', exact: true }))
  await expect(tracker()).toBeVisible()
  if (mode === 'fullscreen')
    await expect(tracker().getByRole('button', { name: 'Lifetime', exact: true })).toBeFocused()
}
async function capture(name: string, element = tracker()) {
  await element.scrollIntoViewIfNeeded()
  await expect(element).toBeInViewport()
  await page.screenshot({ path: test.info().outputPath(`${mode}-${name}.png`), animations: 'disabled' })
}
async function assertTotalTypography() {
  const typography = await tracker()
    .locator('.activity-total')
    .evaluate((node) => {
      const style = getComputedStyle(node)
      const themeScale = Number(style.getPropertyValue('--theme-text-scale')) || 1
      const fullscreenScale = node.closest('.mode-fullscreen')
        ? Number(style.getPropertyValue('--fullscreen-text-scale')) || 1
        : 1
      return {
        size: parseFloat(style.fontSize),
        line: parseFloat(style.lineHeight),
        family: style.fontFamily,
        mono: style.getPropertyValue('--font-mono').trim(),
        scale: themeScale * fullscreenScale,
      }
    })
  expect(typography.size).toBeCloseTo(30 * typography.scale, 3)
  expect(typography.line).toBeCloseTo(34 * typography.scale, 3)
  expect(typography.family.replaceAll('"', '')).toBe(typography.mono.replaceAll('"', ''))
}
async function assertUpdateGeometry() {
  const geometry = await plot().evaluate((node) => {
    const dateTop = node.querySelector('.activity-timeline-dates')!.getBoundingClientRect().top
    return [...node.querySelectorAll('.activity-timeline-update')].map((mark) => ({
      width: parseFloat(getComputedStyle(mark).width),
      height: parseFloat(getComputedStyle(mark).height),
      paintedWidth: mark.getBoundingClientRect().width,
      paintedHeight: mark.getBoundingClientRect().height,
      radius: getComputedStyle(mark).borderRadius,
      zoom: Number(getComputedStyle(document.body).zoom) || 1,
      bottom: mark.getBoundingClientRect().bottom,
      dateTop,
    }))
  })
  await test
    .info()
    .attach('update-target-geometry', { body: JSON.stringify(geometry), contentType: 'application/json' })
  for (const mark of geometry) {
    // Chromium rounds used dimensions to physical subpixels under interface zoom.
    expect(mark.width).toBeCloseTo(24, 1)
    expect(mark.height).toBeCloseTo(24, 1)
    expect(mark.paintedWidth).toBeCloseTo(24 * mark.zoom, 1)
    expect(mark.paintedHeight).toBeCloseTo(24 * mark.zoom, 1)
    expect(mark.radius).toBe('50%')
    expect(mark.bottom).toBeLessThanOrEqual(mark.dateTop)
  }
}
async function probe(kind: string, width: number, height = 200) {
  await app.evaluate(
    async ({ BrowserWindow }, value) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setContentSize(value.width, value.height)
      await window.loadFile(value.path, {
        query: { kind: value.kind, mode: value.mode, width: String(value.width) },
      })
    },
    { path: join(probeDirectory, 'index.html'), kind, mode, width, height },
  )
  await expect(plot()).toBeVisible()
  await page.evaluate(() => document.fonts.ready)
  if (kind !== 'range')
    await expect.poll(() => plot().evaluate((node) => node.getBoundingClientRect().width)).toBe(width)
}

type BackendMeasurement = {
  name: string
  elapsedMs: number
  repositoryLeases: number
  exceeded: boolean
  leaseProcessIds: number[]
}
async function measure(
  name: string,
  action: () => Promise<unknown>,
  ready: () => Promise<unknown>,
  repositoryExpected = true,
) {
  const processes = await app.evaluate(({ BrowserWindow }) => ({
    main: process.pid,
    renderer: BrowserWindow.getAllWindows()[0]!.webContents.getOSProcessId(),
  }))
  const state = await control<{ processId: number }>('state')
  await control('measure-start', { name })
  await app.evaluate(({}, name) => {
    const state = (globalThis as any).__activityNative
    state.phase = name
    state.requests = []
  }, name)
  await page.evaluate(() => {
    const start = performance.now(),
      inputs: { type: string; dispatchToMicrotaskMs: number }[] = []
    let last = start,
      maximumGap = 0,
      ticks = 0
    const timer = setInterval(() => {
      const now = performance.now()
      maximumGap = Math.max(maximumGap, now - last)
      last = now
      ticks++
    }, 1)
    const input = (event: Event) => {
      const at = performance.now()
      queueMicrotask(() => inputs.push({ type: event.type, dispatchToMicrotaskMs: performance.now() - at }))
    }
    document.addEventListener('keydown', input, true)
    document.addEventListener('click', input, true)
    ;(window as any).__activityMeasurement = () => {
      clearInterval(timer)
      document.removeEventListener('keydown', input, true)
      document.removeEventListener('click', input, true)
      return {
        actionToObservedReadyMs: performance.now() - start,
        maxRendererTimerGapMs: maximumGap,
        rendererTimerTicks: ticks,
        inputEvents: inputs,
      }
    }
  })
  let renderer: unknown, backend: BackendMeasurement, requests: unknown
  try {
    await action()
    await ready()
    await page.evaluate(
      () => new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done()))),
    )
  } finally {
    renderer = await page.evaluate(() => (window as any).__activityMeasurement())
    backend = await control<BackendMeasurement>('measure-end', {})
    requests = await app.evaluate(({}, name) => {
      const state = (globalThis as any).__activityNative
      state.phase = ''
      return state.requests.filter((request: { phase: string }) => request.phase === name)
    }, name)
    await test.info().attach(`large-history-${name}`, {
      body: JSON.stringify({
        name,
        processes: { ...processes, backend: state.processId },
        renderer,
        backend,
        requests,
        interpretation:
          'UI action-to-observed-ready includes native input, IPC, API, projection, layout, two paint frames and automation observation. Details additionally opens play history and waits for the bound tracker. Activity waits for both the first event page and statistics; desktop uses its 30-day default, fullscreen its weekly default. Input-dispatch microtask time is not the original synchronous command-invoke metric. Header latency excludes body read. Source10s guard is checked at backend repository-lease boundaries; UI/frame timings are informational.',
      }),
      contentType: 'application/json',
    })
  }
  expect(backend!.exceeded).toBe(false)
  if (repositoryExpected) expect(backend!.repositoryLeases).toBeGreaterThan(0)
  expect(backend!.leaseProcessIds).toEqual(backend!.repositoryLeases > 0 ? [state.processId] : [])
  expect(state.processId).not.toBe(processes.main)
  expect(state.processId).not.toBe(processes.renderer)
  return requests as { path: string; query?: { pageSize?: number } }[]
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
    throw new Error(`${error.message}\n${error.stdout ?? ''}\n${error.stderr ?? ''}`)
  })
  await mkdir(probeDirectory, { recursive: true })
  await build({
    entryPoints: [resolve('tests/electron/activity-remaining-probe.tsx')],
    outfile: join(probeDirectory, 'probe.js'),
    bundle: true,
    platform: 'browser',
    format: 'iife',
    jsx: 'automatic',
    define: { 'process.env.NODE_ENV': '"production"' },
    loader: { '.ttf': 'file', '.woff': 'file', '.woff2': 'file' },
    logLevel: 'silent',
  })
  await writeFile(
    join(probeDirectory, 'index.html'),
    '<!doctype html><html><head><meta charset="utf-8"><link rel="stylesheet" href="probe.css"></head><body><div id="root"></div><script src="probe.js"></script></body></html>',
  )
})
test.beforeEach(async ({}, info) => {
  test.setTimeout(120000)
  mode = info.title.startsWith('fullscreen') ? 'fullscreen' : 'desktop'
  errors.length = 0
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-activity-remaining-'))
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('tests/electron/activity-remaining-main.mjs'), '--data-dir', directory, '--no-sync'],
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
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
  expect((await fetch(new URL('/__fixture/activity-remaining/state', endpoint.address))).status).toBe(401)
  await surface()
})
test.afterEach(async ({}, info) => {
  try {
    if (info.status !== info.expectedStatus && page)
      await info.attach('activity-failure', { body: await page.screenshot(), contentType: 'image/png' })
    expect(await app.evaluate(() => (globalThis as any).__identityProjections.dispatches)).toEqual([])
  } finally {
    await closeFixture(app, directory)
  }
  expect(errors).toEqual([])
})

for (const surfaceName of ['desktop', 'fullscreen']) {
  for (const width of [620, 360])
    test(`${surfaceName} source ${width}px tracker switches units idempotently and keeps all monthly and session records reachable`, async () => {
      await probe('range', width, 600)
      await expect(tracker().getByText(/Hours per month/)).toBeVisible()
      await expect(bars()).toHaveCount(3)
      await assertTotalTypography()
      const tracked = tracker().getByRole('button', { name: 'Tracked sessions', exact: true }),
        lifetime = tracker().getByRole('button', { name: 'Lifetime', exact: true })
      await tracked.click()
      await expect(tracked).toHaveAttribute('aria-pressed', 'true')
      await tracked.click()
      await expect(tracked).toHaveAttribute('aria-pressed', 'true')
      await expect(tracker().getByText(/Hours per session/)).toBeVisible()
      await expect(bars()).toHaveCount(2)
      const geometry = await tracker().evaluate((node) => ({
        plotHeight: node.querySelector('.activity-timeline-plot')!.getBoundingClientRect().height,
        widths: [...node.querySelectorAll('*')]
          .filter((child) => (child as HTMLElement).offsetWidth > 0)
          .map((child) => ({ tag: child.tagName, width: child.getBoundingClientRect().width })),
      }))
      expect(geometry.plotHeight).toBeGreaterThanOrEqual(140)
      expect(geometry.plotHeight).toBeLessThanOrEqual(155)
      for (const child of geometry.widths) expect(child.width, child.tag).toBeLessThanOrEqual(width + 1)
      await lifetime.focus()
      await page.keyboard.press('Space')
      await expect(lifetime).toBeFocused()
      await expect(lifetime).toHaveAttribute('aria-pressed', 'true')
      await lifetime.click()
      await expect(lifetime).toHaveAttribute('aria-pressed', 'true')
      await tracker().locator('summary').click()
      await expect(tracker().locator('details')).toHaveAttribute('open', '')
      await expect(tracker().locator('.activity-records button')).toHaveCount(3)
      await tracker().locator('.activity-records button').last().focus()
      await page.keyboard.press('Enter')
      await expect(tracker().getByRole('status')).toContainText('Winnow sessions')
      await capture(`source-range-${width}`, tracker().locator('.activity-records'))
    })

  test(`${surfaceName} real API acknowledgement refreshes unread marks while preserving tracked range and the same Details`, async () => {
    await seed('ack-correlated')
    await openDetails()
    await openHistory()
    await expect(tracker().locator('.activity-update-summary')).toHaveText('1 unread update')
    await expect(updates()).toHaveCount(1)
    await expect(updates()).toHaveAttribute('data-unread', 'true')
    await assertUpdateGeometry()
    await updates().focus()
    await page.keyboard.press('Tab')
    await page.keyboard.press('Shift+Tab')
    await expect(updates()).toBeFocused()
    const focusRing = await updates().evaluate((node) => ({
      style: getComputedStyle(node).outlineStyle,
      width: parseFloat(getComputedStyle(node).outlineWidth),
    }))
    expect(focusRing.style).not.toBe('none')
    expect(focusRing.width).toBeGreaterThan(0)
    await capture('api-unread-update')
    const original = await details().elementHandle()
    const tracked = tracker().getByRole('button', { name: 'Tracked sessions', exact: true })
    await activate(tracked)
    const before = await api<GameDetails>({ route: 'game.details', params: { workId } })
    const response = await api<{ result: string }>({
      route: 'updates.acknowledge',
      params: { releaseId: 1 },
      body: { observedEventIds: before.events.map((event) => event.id) },
    })
    expect(response.result).toBe('Stored')
    await expect(tracker().locator('.activity-update-summary')).toHaveText('No unread updates')
    await expect(tracked).toHaveAttribute('aria-pressed', 'true')
    expect(await original!.evaluate((node) => node.isConnected)).toBe(true)
    await activate(tracker().getByRole('button', { name: 'Lifetime', exact: true }))
    await expect(updates()).toHaveAttribute('data-unread', 'false')
    await assertUpdateGeometry()
    await capture('api-read-update')
  })

  test(`${surfaceName} real API sparse Steam copy retains ten hours without inventing a plot or records`, async () => {
    await seed('sparse-linked')
    await openDetails()
    await openHistory()
    await expect(tracker().getByText('10h played · Steam copy', { exact: true })).toBeVisible()
    await expect(tracker().getByText('Last-played date unavailable', { exact: true })).toBeVisible()
    await expect(plot()).toHaveCount(0)
    await expect(tracker().locator('summary')).toHaveCount(0)
    await activate(tracker().getByRole('button', { name: 'Tracked sessions', exact: true }))
    await expect(tracker().getByRole('status')).toHaveText('No completed sessions recorded yet')
    await expect(plot()).toHaveCount(0)
    await capture('api-sparse-history')
  })

  test(`${surfaceName} real API source snapshots and sessions remain selectable in Details play history`, async () => {
    await seed('range')
    await openDetails()
    await openHistory()
    await expect(bars()).toHaveCount(3)
    await expect(tracker().getByText('16h played · Steam copy', { exact: true })).toBeVisible()
    await assertTotalTypography()
    await activate(tracker().getByRole('button', { name: 'Tracked sessions', exact: true }))
    await expect(bars()).toHaveCount(2)
    const barGeometry = await bars().evaluateAll((nodes) =>
      nodes.map((node) => ({
        width: parseFloat(getComputedStyle(node).width),
        paintedWidth: node.getBoundingClientRect().width,
        zoom: Number(getComputedStyle(document.body).zoom) || 1,
      })),
    )
    for (const bar of barGeometry) {
      expect(bar.width).toBe(10)
      expect(bar.paintedWidth).toBeCloseTo(10 * bar.zoom, 2)
    }
    await activate(bars().last())
    await expect(tracker().getByRole('status')).toContainText('2h · Winnow session')
    await expect(bars().last()).toBeFocused()
    await activate(tracker().getByRole('button', { name: 'Lifetime', exact: true }))
    await activate(tracker().locator('summary'))
    await expect(tracker().locator('.activity-records button')).toHaveCount(3)
    await activate(tracker().locator('.activity-records button').last())
    await expect(tracker().getByRole('status')).toContainText('3h · Winnow sessions')
    await capture('api-range-records', tracker().locator('.activity-records'))
  })

  test(`${surfaceName} exact large history records Details activity and account-summary measurements without imposing a machine-speed budget`, async () => {
    await seed('large')
    const state = await control<{ counts: Record<string, number> }>('state')
    expect(state.counts).toEqual({
      works: 2000,
      releases: 2000,
      ownerships: 2000,
      sessions: 125000,
      session_notes: 125000,
      playtime_snapshots: 14600,
      update_events: 2000,
      account_transactions: 40000,
    })
    const tile = page.locator(`.avalon-library [data-avalon-game="${workId}"]`)
    const detailsRequests = await measure(
      'details-to-history-ready',
      async () => {
        await tile.focus()
        await page.keyboard.press('Enter')
        await expect(details().locator('h1')).toHaveText('Game 1')
        await openHistory()
      },
      async () => {
        await expect(tracker().locator('.activity-total')).toBeVisible()
        await expect.poll(() => bars().count()).toBeGreaterThan(0)
        await expect(tracker().locator('.activity-records')).toHaveCount(0)
      },
    )
    expect(detailsRequests.filter((request) => request.path.endsWith('/games/1/details'))).toHaveLength(1)
    const data = await api<GameDetails>({ route: 'game.details', params: { workId } })
    expect(data.sessions['1']).toHaveLength(5060)
    expect((data.history as Record<string, unknown[]>)['1']).toHaveLength(14600)
    const observations = new Set(
      data.sessions['1'].map(
        (session) => `${session.startedAt}:${session.endedAt}:${session.durationSeconds}`,
      ),
    ).size
    await tracker().getByRole('button', { name: 'Tracked sessions', exact: true }).focus()
    await measure(
      'tracked-range',
      () => page.keyboard.press('Enter'),
      async () => {
        await expect(tracker().getByRole('status')).toContainText(`${observations} observed sessions`)
        await expect(tracker().locator('.activity-records')).toHaveCount(0)
      },
      false,
    )
    await expect.poll(() => bars().count()).toBeGreaterThan(0)
    const firstBarSelection = await bars().first().getAttribute('aria-label')
    expect(firstBarSelection).toBeTruthy()
    await measure(
      'tracked-selection',
      () => bars().first().focus(),
      async () => {
        await expect(bars().first()).toBeFocused()
        await expect(tracker().getByRole('status')).toHaveText(firstBarSelection!)
      },
      false,
    )
    await capture('large-selected-tracked-history')
    await activate(
      page.getByRole('button', {
        name: mode === 'desktop' ? 'Close game details' : 'B · Back to Library',
        exact: true,
      }),
    )
    const activity = page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'Activity', exact: true })
    const requests = await measure(
      mode === 'fullscreen' ? 'weekly-activity-ready' : '30-day-activity-ready',
      async () => {
        await activity.focus()
        await page.keyboard.press('Enter')
      },
      async () => {
        await expect(page.getByRole('heading', { name: 'A little history.', exact: true })).toBeVisible()
        await expect(page.getByRole('region', { name: 'Activity events', exact: true })).toBeVisible()
        await expect(page.getByText('Loading activity…', { exact: true })).toHaveCount(0)
        await expect(page.locator('.journal-page .stat-strip > div')).toHaveCount(4)
      },
    )
    const events = page
      .getByRole('region', { name: 'Activity events', exact: true })
      .locator('article.timeline-entry')
    expect(await events.count()).toBeGreaterThan(0)
    expect(await events.count()).toBeLessThanOrEqual(50)
    expect(requests.filter((request) => request.path.endsWith('/activity/query'))).toHaveLength(1)
    await events.last().focus()
    await expect(events.last()).toBeFocused()
    await expect(events.last()).toHaveAttribute('aria-current', 'true')
    await capture(mode === 'fullscreen' ? 'large-weekly-activity' : 'large-30-day-activity', events.last())
    await activate(page.getByRole('button', { name: 'Settings', exact: true }))
    if (mode === 'fullscreen')
      await activate(
        page
          .getByRole('navigation', { name: 'Settings section' })
          .getByRole('button', { name: 'Library', exact: true }),
      )
    const spending =
      mode === 'desktop'
        ? page
            .getByRole('navigation', { name: 'Settings section' })
            .getByRole('button', { name: 'Spending', exact: true })
        : page.locator('.fullscreen-settings-content').getByRole('button', { name: 'Spending', exact: true })
    await measure(
      'account-summary',
      async () => {
        await spending.focus()
        await page.keyboard.press('Enter')
      },
      async () => {
        await expect(page.getByRole('region', { name: 'Spending in $', exact: true })).toBeVisible()
        await expect(
          page
            .getByRole('region', { name: 'Spending in $', exact: true })
            .getByText('$400,000.00', { exact: true })
            .first(),
        ).toBeVisible()
        await expect(page.getByRole('button', { name: 'Refresh Steam spending', exact: true })).toBeEnabled()
      },
    )
    await expect(
      page
        .getByRole('region', { name: 'Spending in $', exact: true })
        .getByText('40000 transactions', { exact: true })
        .first(),
    ).toBeVisible()
    await capture('large-account-summary', page.getByRole('region', { name: 'Spending in $', exact: true }))
  })

  test(`${surfaceName} source February and July months have equal native widths and pointer and Enter selection`, async () => {
    await probe('months', 700)
    await expect(bars()).toHaveCount(2)
    const widths = await bars().evaluateAll((nodes) =>
      nodes.map((node) => node.getBoundingClientRect().width),
    )
    expect(widths[0]).toBeCloseTo(widths[1], 3)
    expect(widths[0]).toBeGreaterThan(10)
    await bars().nth(0).click()
    await expect(page.locator('#probe-selection')).toHaveText('February · 2h · monthly history')
    await page.keyboard.press('Tab')
    await expect(bars().nth(1)).toBeFocused()
    await page.keyboard.press('Enter')
    await expect(page.locator('#probe-selection')).toHaveText('July · 4h · Winnow sessions')
    await bars().nth(1).hover()
    await expect(bars().nth(1)).toHaveAttribute('title', 'July · 4h · Winnow sessions')
    await capture('source-months-700', plot())
  })
  test(`${surfaceName} source three sessions produce two ten-pixel bars retaining the collision observations`, async () => {
    await probe('sessions', 700)
    await expect(bars()).toHaveCount(2)
    for (const width of await bars().evaluateAll((nodes) =>
      nodes.map((node) => node.getBoundingClientRect().width),
    ))
      expect(width).toBe(10)
    await expect(bars().nth(0)).toHaveAccessibleName(/2 observed sessions.*3h/)
    await expect(bars().nth(1)).toHaveAccessibleName('Third session')
    await bars().nth(0).click()
    await expect(page.locator('#probe-selection')).toContainText('2 observed sessions')
    await capture('source-session-collisions-700', plot())
  })
  test(`${surfaceName} source thirty Patch observations cluster without truncation and request tracked range before refreshing read fills`, async () => {
    await probe('updates', 700)
    await expect(updates()).toHaveCount(1)
    const update = updates().first()
    await assertUpdateGeometry()
    await expect(update).toHaveAccessibleName(/30 updates · 30 unread/)
    expect(await update.evaluate((node) => getComputedStyle(node).backgroundColor)).toBe('rgb(255, 105, 180)')
    await update.click()
    await expect(page.locator('#probe-requests')).toHaveText('1')
    await expect(page.locator('#probe-selection')).toContainText('Patch')
    const sourceDate = await page.evaluate(() =>
      new Date(Date.UTC(2026, 7, 1)).toLocaleDateString(undefined, {
        day: 'numeric',
        month: 'short',
        year: 'numeric',
        timeZone: 'UTC',
      }),
    )
    await expect(page.locator('#probe-selection')).toContainText(sourceDate)
    expect((await update.getAttribute('aria-label'))!.split('Patch')).toHaveLength(31)
    await capture('source-thirty-unread-700', plot())
    await page.evaluate(() => dispatchEvent(new Event('activity-probe-acknowledge')))
    await expect(updates()).toHaveCount(1)
    await expect(update).toHaveAccessibleName(/30 updates · 0 unread/)
    await expect
      .poll(() => update.evaluate((node) => getComputedStyle(node).backgroundColor))
      .toBe('rgb(211, 211, 211)')
    await capture('source-thirty-read-700', plot())
  })
  test(`${surfaceName} source 180px plot keeps Today and every date and update target inside150px without overlapping labels`, async () => {
    await probe('narrow', 180)
    const sourceStartDate = await page.evaluate(() =>
      new Date(Date.UTC(2026, 0, 1)).toLocaleDateString(undefined, {
        day: 'numeric',
        month: 'short',
        year: 'numeric',
        timeZone: 'UTC',
      }),
    )
    await expect(plot().locator('.activity-timeline-dates > span').first()).toHaveText(sourceStartDate)
    await expect(plot().getByText('Today', { exact: true })).toBeVisible()
    await expect(updates()).toHaveCount(2)
    await assertUpdateGeometry()
    const geometry = await plot().evaluate((node) => {
      const origin = node.getBoundingClientRect()
      const bounds = (child: Element) => {
        const rect = child.getBoundingClientRect()
        return {
          left: rect.left - origin.left,
          right: rect.right - origin.left,
          bottom: rect.bottom - origin.top,
          text: child.textContent,
        }
      }
      return {
        width: origin.width,
        height: origin.height,
        labels: [...node.querySelectorAll('.activity-timeline-dates > span')].map(bounds),
        children: [...node.querySelectorAll('*')].map(bounds),
      }
    })
    for (let index = 1; index < geometry.labels.length; index++)
      expect(geometry.labels[index - 1].right).toBeLessThan(geometry.labels[index].left)
    for (const child of geometry.children) {
      expect(child.left, child.text ?? '').toBeGreaterThanOrEqual(-0.01)
      expect(child.right, child.text ?? '').toBeLessThanOrEqual(180.01)
      expect(child.bottom, child.text ?? '').toBeLessThanOrEqual(150)
    }
    await test
      .info()
      .attach('source-180px-geometry', { body: JSON.stringify(geometry), contentType: 'application/json' })
    await capture('source-narrow-180', plot())
  })
}
