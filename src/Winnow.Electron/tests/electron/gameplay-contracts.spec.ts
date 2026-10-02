import { prebuiltFixture as fixture, prebuiltActivationHelper } from './prebuilt-backend'
import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Page,
  type Locator,
} from '@playwright/test'

import { mkdtemp, readFile } from 'node:fs/promises'
import { resolve, join } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import type { ApiRequest } from '../../src/shared/bridge'
import type { Mode } from '../../src/renderer/api/types'

const sourceNow = '2026-12-01T12:00:00.000Z'
let app: ElectronApplication, page: Page, directory: string, mode: Mode
let endpoint: { address: string; token: string }
const errors: string[] = []
const dashboard = () => page.locator('.gameplay-dashboard')
const chart = (name: string) => dashboard().getByRole('region', { name, exact: true })
const sections = () => dashboard().getByRole('navigation', { name: 'Library summary section', exact: true })
type Read = {
  id: number
  request: {
    ownerships: { ownershipId: number; resolvedWorkId: number }[]
    fromUtc: string
    untilUtc: string
    asOfUtc: string
    timeBins: { fromUtc: string; untilUtc: string }[]
    store: string | null
  }
  gateId?: string
  completed: boolean
  failed: boolean
  processId: number
  isDashboard: boolean
  cancellationRequested: boolean
}
const state = () => control<{ processId: number; calls: Read[] }>('state')
const reads = async () => (await state()).calls.filter((read) => read.isDashboard)
async function arm(
  behavior = 'hold',
  seconds?: number,
  options?: {
    repeat?: boolean
    match?: { store: string | null; ownerships: { ownershipId: number; resolvedWorkId: number }[] }
  },
) {
  return (await control<{ gateId: string }>('arm', { behavior, seconds, target: 'dashboard', ...options }))
    .gateId
}
async function entered(gateId: string) {
  await expect.poll(async () => (await reads()).some((read) => read.gateId === gateId)).toBe(true)
  const read = (await reads()).find((read) => read.gateId === gateId)!
  expect(Date.parse(read.request.asOfUtc)).toBe(Date.parse(sourceNow))
  expect(Date.parse(read.request.untilUtc)).not.toBe(Date.parse(read.request.asOfUtc))
  return read
}
async function release(gateId: string, seconds?: number) {
  await control('release', { gateId, seconds })
  await expect.poll(async () => (await reads()).find((read) => read.gateId === gateId)?.completed).toBe(true)
}
async function canceled(gateId: string) {
  await expect
    .poll(async () => (await reads()).find((read) => read.gateId === gateId)?.cancellationRequested)
    .toBe(true)
}
async function change(value: string) {
  await control('change', { change: value })
}
async function seed(kind: 'preview' | 'scope' | 'xbox', secondStore?: string) {
  await control('seed', { kind, secondStore })
  await navigate('Library')
  await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(kind === 'preview' ? 8 : 2)
}

async function api<T>(input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const response = await window.winnow.request(input)
    if (!response.ok) throw Error(`${input.route}: ${response.status} ${response.message}`)
    return response.data
  }, input) as Promise<T>
}
async function control<T>(name: string, body?: unknown): Promise<T> {
  const response = await fetch(new URL(`/__fixture/gameplay-stats/${name}`, endpoint.address), {
    method: body === undefined ? 'GET' : 'POST',
    headers: { Authorization: `Bearer ${endpoint.token}`, 'Content-Type': 'application/json' },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    signal: AbortSignal.timeout(60000),
  })
  if (!response.ok) throw Error(`Gameplay fixture ${name}: HTTP${response.status}`)
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}
async function tap(button: number) {
  // Standard Gamepad API frames establish simulated input, not physical-device certification.
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
async function activate(element: Locator, key = 'Enter') {
  await element.scrollIntoViewIfNeeded()
  await element.focus()
  await expect(element).toBeFocused()
  if (mode === 'fullscreen') await tap(0)
  else await page.keyboard.press(key)
}
async function surface(width: number, height: number, scale = 1) {
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
  if (mode === 'fullscreen') {
    await api({
      route: 'preferences.presentation.put',
      params: { preference: 'FullscreenTextScale' },
      body: { value: String(scale) },
    })
    await expect
      .poll(() =>
        page.evaluate(() =>
          getComputedStyle(document.documentElement).getPropertyValue('--fullscreen-text-scale').trim(),
        ),
      )
      .toBe(String(scale))
    await tap(-1)
  }
}
async function navigate(name: string) {
  await activate(
    page.getByRole('navigation', { name: 'Main navigation' }).getByRole('button', { name, exact: true }),
  )
}
async function openGameplay() {
  if (mode === 'desktop') await navigate('STATS')
  else {
    await navigate('Activity')
    await activate(
      page
        .getByRole('navigation', { name: 'Activity pages', exact: true })
        .getByRole('button', { name: 'Library summary', exact: true }),
    )
  }
  await expect(dashboard()).toBeVisible()
  await expect(sections().getByRole('button', { name: 'Gameplay', exact: true })).toHaveAttribute(
    'aria-pressed',
    'true',
  )
}
async function capture(name: string, target?: Locator) {
  if (target) {
    await target.scrollIntoViewIfNeeded()
    await expect(target).toBeInViewport()
  }
  await page.screenshot({ path: test.info().outputPath(`${mode}-${name}.png`), animations: 'disabled' })
}
async function noHorizontalOverflow() {
  const widths = await dashboard().evaluate((node) => {
    const elements: HTMLElement[] = [node as HTMLElement]
    for (let parent = node.parentElement; parent; parent = parent.parentElement)
      if (/(auto|scroll)/.test(getComputedStyle(parent).overflowY)) elements.push(parent)
    return elements.map((element) => ({
      className: element.className,
      width: element.clientWidth,
      scrollWidth: element.scrollWidth,
    }))
  })
  for (const value of widths) expect(value.scrollWidth, value.className).toBeLessThanOrEqual(value.width + 1)
}

async function choosePeriod(value: '30' | '90' | 'custom') {
  if (mode === 'desktop')
    await dashboard().getByRole('combobox', { name: 'Gameplay period', exact: true }).selectOption(value)
  else
    await activate(
      dashboard()
        .getByRole('group', { name: 'Gameplay period', exact: true })
        .getByRole('button', { name: value === 'custom' ? 'Custom' : `${value} days`, exact: true }),
    )
}
async function chooseStore(key: string, label: string) {
  if (mode === 'desktop')
    await dashboard().getByRole('combobox', { name: 'Store', exact: true }).selectOption(key)
  else
    await activate(
      dashboard()
        .getByRole('group', { name: 'Store', exact: true })
        .getByRole('button', { name: label, exact: true }),
    )
}
async function expectStore(key: string, label: string) {
  if (mode === 'desktop')
    await expect(dashboard().getByRole('combobox', { name: 'Store', exact: true })).toHaveValue(key)
  else
    await expect(
      dashboard()
        .getByRole('group', { name: 'Store', exact: true })
        .getByRole('button', { name: label, exact: true }),
    ).toHaveAttribute('aria-pressed', 'true')
}
async function dates(from: string, until: string) {
  await dashboard().getByRole('textbox', { name: 'From', exact: true }).fill(from)
  await dashboard().getByRole('textbox', { name: 'Through', exact: true }).fill(until)
  await activate(dashboard().getByRole('button', { name: 'Apply dates', exact: true }))
}
const recorded = () =>
  dashboard()
    .locator('.stat')
    .filter({ has: page.getByText('Recorded hours', { exact: true }) })
    .locator('strong')
async function previewReady() {
  await expect(recorded()).toHaveText('31.5 h')
  for (const title of [
    'Recorded hours over time',
    'Games you spent time with',
    'Session lengths',
    'Your library today',
  ]) {
    await expect(chart(title)).toBeVisible()
    expect(await chart(title).locator('li').count()).toBeGreaterThan(0)
  }
  await expect(chart('Games you spent time with').locator('li')).toHaveCount(6)
  await expect(chart('Session lengths').locator('li strong')).toHaveText([
    '5 sessions',
    '9 sessions',
    '6 sessions',
    '3 sessions',
  ])
  await expect(dashboard().getByText('45 min', { exact: true })).toBeVisible()
}
async function section(value: 'Gameplay' | 'Spending') {
  if (mode === 'fullscreen') {
    await sections()
      .getByRole('button', { name: value === 'Spending' ? 'Gameplay' : 'Spending', exact: true })
      .focus()
    await tap(value === 'Spending' ? 7 : 6)
  } else
    await activate(
      sections().getByRole('button', { name: value, exact: true }),
      value === 'Spending' ? 'Space' : 'Enter',
    )
  await expect(sections().getByRole('button', { name: value, exact: true })).toHaveAttribute(
    'aria-pressed',
    'true',
  )
}
async function fullTypography(scale: number) {
  const styles = await sections()
    .getByRole('button', { name: 'Gameplay', exact: true })
    .evaluate((node) => {
      const style = getComputedStyle(node)
      const authored: { selector: string; width: string }[] = []
      const visit = (rules: CSSRuleList) => {
        for (const rule of rules) {
          if (rule instanceof CSSStyleRule && node.matches(rule.selectorText) && rule.style.borderBottomWidth)
            authored.push({ selector: rule.selectorText, width: rule.style.borderBottomWidth })
          if ('cssRules' in rule) visit((rule as CSSGroupingRule).cssRules)
        }
      }
      for (const sheet of document.styleSheets) visit(sheet.cssRules)
      let zoom = 1
      for (let ancestor: Element | null = node; ancestor; ancestor = ancestor.parentElement)
        zoom *= Number(getComputedStyle(ancestor).zoom) || 1
      return {
        font: parseFloat(style.fontSize),
        border: parseFloat(style.borderBottomWidth),
        authored,
        zoom,
        dpr: devicePixelRatio,
      }
    })
  expect(styles.font).toBeCloseTo(28 * scale)
  expect(styles.authored).toContainEqual({
    selector: '.gameplay-dashboard.mode-fullscreen .gameplay-toolbar .tabs button',
    width: '3px',
  })
  // Chromium quantizes the authored three CSS pixels after the existing interface zoom.
  const paintedPixels = Math.floor(3 * styles.zoom * styles.dpr)
  expect(styles.border).toBeCloseTo(paintedPixels / (styles.zoom * styles.dpr), 4)
  await test.info().attach('source-section-border-quantization', {
    body: JSON.stringify({ ...styles, authoredSourcePixels: 3, paintedPixels }),
    contentType: 'application/json',
  })
  expect(
    await dashboard()
      .getByText('Recorded hours', { exact: true })
      .evaluate((node) => parseFloat(getComputedStyle(node).fontSize)),
  ).toBeCloseTo(24 * scale)
}

async function selectedChoicePaint() {
  await page.evaluate(() => (document.activeElement as HTMLElement | null)?.blur())
  await page.mouse.move(0, 0)
  for (const name of ['Store', 'Gameplay period']) {
    const group = dashboard().getByRole('group', { name, exact: true })
    await expect(group.locator('button[aria-pressed="true"]')).toHaveCount(1)
    await expect
      .poll(async () =>
        group.evaluate((node) => {
          const selected = node.querySelector('button[aria-pressed="true"]')!
          const other = node.querySelector('button[aria-pressed="false"]')!
          const probe = document.createElement('span')
          probe.style.background = 'var(--raised)'
          probe.style.color = 'var(--accent-foreground, var(--accent))'
          node.append(probe)
          const expected = getComputedStyle(probe)
          const current = getComputedStyle(selected),
            unselected = getComputedStyle(other)
          const result = {
            unfocused: !selected.matches(':focus-visible') && document.activeElement !== selected,
            correctFill: current.backgroundColor === expected.backgroundColor,
            correctInk: current.color === expected.color && current.borderTopColor === expected.color,
            differs:
              [current.backgroundColor, current.color, current.borderTopColor].join('|') !==
              [unselected.backgroundColor, unselected.color, unselected.borderTopColor].join('|'),
          }
          probe.remove()
          return result
        }),
      )
      .toEqual({ unfocused: true, correctFill: true, correctInk: true, differs: true })
  }
}

test.beforeEach(async ({}, info) => {
  test.setTimeout(90000)
  mode = info.title.startsWith('fullscreen') ? 'fullscreen' : 'desktop'
  errors.length = 0
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-gameplay-stats-'))
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('tests/electron/gameplay-contracts-main.mjs'), '--data-dir', directory, '--no-sync'],
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
    timeout: 60000,
  })
  page = await app.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible({ timeout: 45000 })
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
  expect((await fetch(new URL('/__fixture/gameplay-stats/state', endpoint.address))).status).toBe(401)
  const cdp = await page.context().newCDPSession(page)
  await cdp.send('Emulation.setTimezoneOverride', {
    timezoneId: info.title.includes('DST') ? 'America/Los_Angeles' : 'UTC',
  })
  await page.clock.setFixedTime(new Date(sourceNow))
  await surface(mode === 'fullscreen' ? 1920 : 1200, 1080)
})
test.afterEach(async ({}, info) => {
  try {
    if (endpoint) {
      const ledger = await state()
      await info.attach('gameplay-fixture-calls', {
        body: JSON.stringify(ledger, null, 2),
        contentType: 'application/json',
      })
      for (const read of ledger.calls)
        expect(read.isDashboard).toBe(
          Date.parse(read.request.asOfUtc) === Date.parse(sourceNow) &&
            Date.parse(read.request.untilUtc) !== Date.parse(read.request.asOfUtc),
        )
    }
    if (info.status !== info.expectedStatus && page) {
      await info.attach('gameplay-before-teardown', {
        body: await page.screenshot(),
        contentType: 'image/png',
      })
      await info.attach('before-teardown-dom', {
        body: await page.locator('body').ariaSnapshot(),
        contentType: 'text/plain',
      })
    }
    expect(await app.evaluate(() => (globalThis as any).__identityProjections.dispatches)).toEqual([])
  } finally {
    if (endpoint) await control('release', {}).catch(() => {})
    await closeFixture(app, directory)
  }
  expect(errors).toEqual([])
})

for (const [surfaceName, width, height, scale] of [
  ['desktop', 1200, 900, 1],
  ['desktop', 600, 900, 1],
  ['fullscreen', 1920, 1080, 1],
  ['fullscreen', 1280, 1080, 1.4],
] as const)
  test(`${surfaceName} source ${width}px four charts retain store and inclusive custom dates across Gameplay and Spending`, async () => {
    await surface(width, height, scale)
    await seed('preview')
    await openGameplay()
    await previewReady()
    if (mode === 'fullscreen') await selectedChoicePaint()
    await capture(`${width}-overview`, sections())
    await capture(`${width}-hours`, chart('Recorded hours over time'))
    await capture(`${width}-library`, chart('Your library today'))
    for (const title of [
      'Recorded hours over time',
      'Games you spent time with',
      'Session lengths',
      'Your library today',
    ]) {
      const mark = chart(title).locator(':scope > ol > li > button').first()
      const label = await mark.locator('.activity-bar-label').innerText()
      const value = await mark.locator('strong').innerText()
      await activate(mark)
      await expect(mark).toHaveAttribute('aria-pressed', 'true')
      await expect(mark).toBeFocused()
      await expect(chart(title).getByRole('status')).toContainText(`${label}: ${value}`)
    }
    const libraryCounts = await chart('Your library today').locator('li strong').allTextContents()
    await choosePeriod('90')
    await expect
      .poll(async () => Date.parse((await reads()).at(-1)?.request.fromUtc ?? ''))
      .toBe(Date.parse('2026-09-03T00:00:00Z'))
    await previewReady()
    await expect(chart('Your library today').locator('li strong')).toHaveText(libraryCounts)
    await chooseStore('gog', 'GOG')
    await expectStore('gog', 'GOG')
    await expect.poll(async () => (await reads()).at(-1)?.request.store).toBe('gog')
    await choosePeriod('custom')
    const from = dashboard().getByRole('textbox', { name: 'From', exact: true })
    if (mode === 'fullscreen') {
      await activate(from)
      await expect(page.getByRole('dialog', { name: 'Enter text', exact: true })).toBeVisible()
      await page.keyboard.press('Escape')
      await expect(from).toBeFocused()
    }
    await dates('2026-09-01', '2026-09-10')
    await expect
      .poll(async () => {
        const read = (await reads()).at(-1)?.request
        return read && [Date.parse(read.fromUtc), Date.parse(read.untilUtc), read.store]
      })
      .toEqual([Date.parse('2026-09-01T00:00:00Z'), Date.parse('2026-09-11T00:00:00Z'), 'gog'])
    // Chromium's en-GB month abbreviation is Sept; the source .NET culture uses Sep.
    await expect(dashboard().locator('.gameplay-period-label')).toHaveText(
      /^GOG · 1 Sept? 2026 – 10 Sept? 2026 · local dates$/,
    )
    await expect(dashboard().getByRole('alert')).toHaveCount(0)
    if (mode === 'fullscreen') await selectedChoicePaint()
    await capture(`${width}-custom`, sections())
    await section('Spending')
    await expect(dashboard()).toContainText('Source: Steam account pages.')
    await expect(dashboard().getByText('$20.00', { exact: true }).first()).toBeVisible()
    const accountCounts = dashboard().getByText('1 transactions · 0 licences.', { exact: true })
    await expect(accountCounts).toBeVisible()
    await capture(`${width}-spending-counts`, accountCounts)
    await section('Gameplay')
    await expectStore('gog', 'GOG')
    if (mode === 'desktop')
      await expect(dashboard().getByRole('combobox', { name: 'Gameplay period', exact: true })).toHaveValue(
        'custom',
      )
    else
      await expect(
        dashboard()
          .getByRole('group', { name: 'Gameplay period', exact: true })
          .getByRole('button', { name: 'Custom', exact: true }),
      ).toHaveAttribute('aria-pressed', 'true')
    await expect(from).toHaveValue('2026-09-01')
    await expect(dashboard().getByRole('textbox', { name: 'Through', exact: true })).toHaveValue('2026-09-10')
    await noHorizontalOverflow()
    if (surfaceName === 'desktop' && width === 1200) {
      mode = 'fullscreen'
      await surface(1920, 1080)
      await openGameplay()
      await expectStore('', 'All stores')
      await expect(
        dashboard()
          .getByRole('group', { name: 'Gameplay period', exact: true })
          .getByRole('button', { name: '30 days', exact: true }),
      ).toHaveAttribute('aria-pressed', 'true')
      mode = 'desktop'
      await surface(width, height)
      await openGameplay()
      await expectStore('gog', 'GOG')
      await expect(dashboard().getByRole('combobox', { name: 'Gameplay period', exact: true })).toHaveValue(
        'custom',
      )
      await expect(dashboard().getByRole('textbox', { name: 'From', exact: true })).toHaveValue('2026-09-01')
    }
  })

for (const scale of [1, 1.4])
  test(`fullscreen source full shell preserves section and chart typography at text ${scale}`, async () => {
    await surface(1920, 1080, scale)
    await seed('preview')
    await openGameplay()
    await previewReady()
    await fullTypography(scale)
    await selectedChoicePaint()
    await capture(`shell-${scale}-overview`, sections())
    await capture(`shell-${scale}-hours`, chart('Recorded hours over time'))
    await capture(`shell-${scale}-library`, chart('Your library today'))
    await choosePeriod('custom')
    await capture(`shell-${scale}-custom`, sections())
    await noHorizontalOverflow()
    const navigation = page.getByRole('navigation', { name: 'Main navigation' })
    for (const bumper of ['LB', 'RB']) {
      const hint = navigation.locator(`[data-root-bumper="${bumper}"]`)
      await expect(hint).toBeVisible()
      await expect(hint.locator('path')).toBeVisible()
      await expect(hint).toHaveAttribute('aria-hidden', 'true')
    }
    expect(
      await navigation.evaluate((node) => {
        const buttons = [...node.querySelectorAll('button')]
        return (
          node.querySelector('[data-root-bumper="LB"]')!.getBoundingClientRect().right <=
            buttons[0].getBoundingClientRect().left &&
          node.querySelector('[data-root-bumper="RB"]')!.getBoundingClientRect().left >=
            buttons.at(-1)!.getBoundingClientRect().right
        )
      }),
    ).toBe(true)
  })

for (const width of [1024, 600])
  test(`desktop source ${width}px Spending header keeps the dashboard above the fold`, async () => {
    await surface(width, 760)
    await seed('preview')
    await openGameplay()
    await section('Spending')
    const figures = dashboard().locator('.account-spending-figures')
    await expect(figures).toBeVisible()
    await expect(dashboard().getByText('$20.00', { exact: true }).first()).toBeVisible()
    const y = await figures.evaluate(
      (node) =>
        node.getBoundingClientRect().top - node.closest('.gameplay-dashboard')!.getBoundingClientRect().top,
    )
    expect(y).toBeGreaterThanOrEqual(0)
    expect(y).toBeLessThanOrEqual(width === 1024 ? 170 : 220)
    await test.info().attach('source-spending-header-bounds', {
      body: JSON.stringify({
        viewportWidth: width,
        viewportHeight: 760,
        figuresTopRelativeToStats: y,
        sourceMaximum: width === 1024 ? 170 : 220,
      }),
      contentType: 'application/json',
    })
    await expect(dashboard().getByText('Steam account', { exact: true })).toHaveCount(0)
    await expect(dashboard()).toContainText('Source: Steam account pages.')
    const refresh = dashboard().getByRole('button', { name: 'Refresh Steam spending', exact: true })
    await refresh.focus()
    await expect(refresh).toBeFocused()
    await noHorizontalOverflow()
    await capture(`${width}-spending-header`)
  })

for (const surfaceName of ['desktop', 'fullscreen']) {
  test(`${surfaceName} source Xbox post-import reload updates open store choices and real library counts`, async () => {
    await seed('xbox')
    await openGameplay()
    await expect(recorded()).toHaveText('0 h')
    const storeEntries = chart('Your library today').getByRole('group', {
      name: 'Store entries',
      exact: true,
    })
    await expect(storeEntries).not.toContainText('Xbox')
    await change('import-xbox')
    await expect(storeEntries).toContainText('Xbox')
    await expect(storeEntries.locator('li').filter({ hasText: 'Xbox' }).locator('strong')).toHaveText(/\b1\b/)
    await expect(storeEntries.locator('li').filter({ hasText: 'Steam' }).locator('strong')).toHaveText(
      /\b1\b/,
    )
    await chooseStore('plugin:xbox', 'Xbox')
    await expectStore('plugin:xbox', 'Xbox')
    await expect(dashboard()).toContainText('Xbox ·')
    await expect(storeEntries.locator('li')).toHaveCount(1)
    await expect(storeEntries).toContainText('Xbox')
    await expect(recorded()).toHaveText('0 h')
    await expect(dashboard()).toContainText('No completed sessions')
    const buckets = chart('Your library today').locator(':scope > ol > li strong')
    expect(
      (await buckets.allTextContents()).reduce((total, value) => total + Number(value.match(/\d+/)?.[0]), 0),
    ).toBe(1)
    await capture('xbox-open-store-counts', chart('Your library today'))
  })

  test(`${surfaceName} source hidden scope cancels the obsolete two-owner read and preserves the current one-hour result`, async () => {
    await seed('scope')
    const old = await arm()
    await openGameplay()
    expect((await entered(old)).request.ownerships).toHaveLength(2)
    await expect(dashboard()).toContainText('Reading gameplay statistics…')
    await expect(recorded()).toHaveCount(0)
    const current = await arm('value', 3600, {
      repeat: true,
      match: { store: null, ownerships: [{ ownershipId: 1, resolvedWorkId: 1 }] },
    })
    await change('hide2')
    const currentRead = await entered(current)
    await canceled(old)
    expect(currentRead.request.ownerships.map((owner) => owner.ownershipId)).toEqual([1])
    await expect(recorded()).toHaveText('1 h')
    await release(old, 999999)
    await expect(recorded()).toHaveText('1 h')
    await expect(dashboard().getByRole('alert')).toHaveCount(0)
    await capture('hidden-scope-rejects-old-result', recorded())
  })

  for (const [store, label] of [
    ['gog', 'GOG'],
    ['plugin:xbox', 'Xbox'],
  ] as const)
    test(`${surfaceName} source ${label} identity reload remaps both owners and resets a removed store`, async () => {
      await seed('scope', store)
      await openGameplay()
      await expect(recorded()).toHaveText('0 h')
      await chooseStore(store, label)
      await expectStore(store, label)
      await expect.poll(async () => (await reads()).at(-1)?.request.store).toBe(store)
      const linked = await arm('value', 0, {
        match: {
          store,
          ownerships: [
            { ownershipId: 1, resolvedWorkId: 1 },
            { ownershipId: 2, resolvedWorkId: 1 },
          ],
        },
      })
      await change('link1-2')
      const linkedRead = await entered(linked)
      expect(linkedRead.request.store).toBe(store)
      expect(linkedRead.request.ownerships).toHaveLength(2)
      expect(linkedRead.request.ownerships.map((owner) => owner.resolvedWorkId)).toEqual([1, 1])
      await expectStore(store, label)
      const removed = await arm('value', 0, {
        match: { store: null, ownerships: [{ ownershipId: 1, resolvedWorkId: 1 }] },
      })
      await change('remove2')
      const removedRead = await entered(removed)
      expect(removedRead.request.store).toBeNull()
      expect(removedRead.request.ownerships.map((owner) => owner.ownershipId)).toEqual([1])
      await expectStore('', 'All stores')
      await expect(recorded()).toHaveText('0 h')
    })

  test(`${surfaceName} source search leaves statistics unfiltered and deactivation rejects late ten-hour data`, async () => {
    await seed('scope')
    await page.keyboard.press('Control+k')
    const search = page.getByRole(mode === 'fullscreen' ? 'searchbox' : 'textbox', {
      name: 'Search games',
      exact: true,
    })
    await expect(search).toBeVisible()
    await search.fill('no matching game')
    if (mode === 'fullscreen') await expect(page.locator('.avalon-search-count')).toContainText('0 games')
    else await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(0)
    const pending = await arm()
    await openGameplay()
    const read = await entered(pending)
    expect(read.request.ownerships).toHaveLength(2)
    await expect(dashboard()).toContainText('Reading gameplay statistics…')
    await expect(recorded()).toHaveCount(0)
    await navigate('Library')
    await canceled(pending)
    await release(pending, 36000)
    await expect(dashboard()).toHaveCount(0)
    const returning = await arm()
    await openGameplay()
    await entered(returning)
    await expect(dashboard()).toContainText('Reading gameplay statistics…')
    await expect(recorded()).toHaveCount(0)
    await release(returning, 0)
    await expect(recorded()).toHaveText('0 h')
  })

  for (const [date, duration, from, until] of [
    ['2026-03-08', 23, '2026-03-08T08:00:00Z', '2026-03-09T07:00:00Z'],
    ['2026-11-01', 25, '2026-11-01T07:00:00Z', '2026-11-02T08:00:00Z'],
  ] as const)
    test(`${surfaceName} source DST ${date} inclusive local day sends its real ${duration}-hour UTC interval`, async () => {
      await seed('scope')
      await openGameplay()
      await expect(recorded()).toHaveText('0 h')
      await choosePeriod('custom')
      const exact = await arm('value', 0)
      await dates(date, date)
      const request = (await entered(exact)).request
      expect(Date.parse(request.fromUtc)).toBe(Date.parse(from))
      expect(Date.parse(request.untilUtc)).toBe(Date.parse(until))
      expect((Date.parse(request.untilUtc) - Date.parse(request.fromUtc)) / 3600000).toBe(duration)
      expect(request.timeBins).toHaveLength(1)
      expect(request.timeBins[0]).toEqual({ fromUtc: request.fromUtc, untilUtc: request.untilUtc })
      expect(request.fromUtc).toMatch(/Z$/)
      expect(request.untilUtc).toMatch(/Z$/)
      expect(Date.parse(request.asOfUtc)).toBe(Date.parse(sourceNow))
      await expect(recorded()).toHaveText('0 h')
      await expect(dashboard().getByRole('alert')).toHaveCount(0)
    })

  test(`${surfaceName} source five invalid custom date pairs send no query and reject obsolete results`, async () => {
    await seed('scope')
    const old = await arm()
    await openGameplay()
    await entered(old)
    await expect(dashboard()).toContainText('Reading gameplay statistics…')
    await expect(recorded()).toHaveCount(0)
    const draft = await arm()
    await choosePeriod('custom')
    await entered(draft)
    const count = (await reads()).length
    for (const [from, until, message] of [
      ['bad', '2026-01-01', 'Enter both dates as YYYY-MM-DD.'],
      ['2026-01-02', '2026-01-01', 'The start date must be on or before the end date.'],
      ['2027-01-01', '2027-01-01', 'Choose an end date no later than today.'],
      ['1899-12-31', '1900-01-01', 'Choose a period of up to ten years, starting in 1900 or later.'],
      ['2000-01-01', '2026-01-01', 'Choose a period of up to ten years, starting in 1900 or later.'],
    ]) {
      await dates(from!, until!)
      await expect(dashboard().getByRole('alert').locator('p')).toHaveText(message!)
      await expect(recorded()).toHaveCount(0)
      await expect(dashboard().getByText('Reading gameplay statistics…', { exact: true })).toHaveCount(0)
      expect(await reads()).toHaveLength(count)
    }
    await canceled(old)
    await canceled(draft)
    await section('Spending')
    await expect(dashboard()).toContainText('Source: Steam account pages.')
    await section('Gameplay')
    await expect(dashboard().getByRole('alert').locator('p')).toHaveText(
      'Choose a period of up to ten years, starting in 1900 or later.',
    )
    await expect(recorded()).toHaveCount(0)
    expect(await reads()).toHaveLength(count)
    await release(old, 999999)
    await release(draft, 999999)
    await expect(recorded()).toHaveCount(0)
    expect(await reads()).toHaveLength(count)
    await capture('invalid-dates-retain-no-obsolete-data', dashboard().getByRole('alert'))
  })
}

test('desktop source failed read retries and malformed custom dates remain actionable', async () => {
  await surface(800, 800)
  await seed('preview')
  const failed = await arm('fail')
  await openGameplay()
  await entered(failed)
  await expect(dashboard().getByRole('alert')).toContainText("Couldn't read gameplay statistics. Try again.")
  await activate(dashboard().getByRole('button', { name: 'Try again', exact: true }))
  await previewReady()
  const refreshFailure = await arm('fail')
  await activate(dashboard().getByRole('button', { name: 'Refresh', exact: true }))
  await entered(refreshFailure)
  await expect(dashboard().getByRole('alert')).toContainText("Couldn't read gameplay statistics. Try again.")
  await expect(recorded()).toHaveCount(0)
  await activate(dashboard().getByRole('button', { name: 'Try again', exact: true }))
  await previewReady()
  await choosePeriod('custom')
  await dates('not a date', '2026-12-01')
  await expect(dashboard().getByRole('alert')).toContainText('YYYY-MM-DD')
  await expect(recorded()).toHaveCount(0)
  await expect(dashboard().getByRole('button', { name: 'Apply dates', exact: true })).toBeEnabled()
  await capture('retry-custom-validation', dashboard().getByRole('alert'))
})

test('fullscreen source controller retry cancel and resume rejects the canceled empty completion', async () => {
  await surface(1920, 1080)
  await seed('preview')
  const failed = await arm('fail')
  await openGameplay()
  await entered(failed)
  await expect(dashboard().getByRole('alert')).toContainText("Couldn't read gameplay statistics. Try again.")
  const held = await arm()
  await activate(dashboard().getByRole('button', { name: 'Try again', exact: true }))
  await entered(held)
  await expect(dashboard()).toContainText('Reading gameplay statistics…')
  await activate(dashboard().getByRole('button', { name: 'Cancel', exact: true }))
  await canceled(held)
  await expect(dashboard().getByRole('alert')).toContainText('Reading stopped')
  await expect(dashboard().getByText('Reading gameplay statistics…', { exact: true })).toHaveCount(0)
  await activate(dashboard().getByRole('button', { name: 'Try again', exact: true }))
  await previewReady()
  await release(held, 0)
  await previewReady()
  await expect(dashboard().getByRole('alert')).toHaveCount(0)
  const refreshFailure = await arm('fail')
  await activate(dashboard().getByRole('button', { name: 'Refresh gameplay', exact: true }))
  await entered(refreshFailure)
  await expect(dashboard().getByRole('alert')).toContainText("Couldn't read gameplay statistics. Try again.")
  await expect(recorded()).toHaveCount(0)
  await activate(dashboard().getByRole('button', { name: 'Try again', exact: true }))
  await previewReady()
  await capture('controller-retry-cancel-resume', sections())
})
