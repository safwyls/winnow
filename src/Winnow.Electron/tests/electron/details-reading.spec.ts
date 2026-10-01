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
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import type { ApiRequest } from '../../src/shared/bridge'
import type { GameDetails, Mode } from '../../src/renderer/api/types'

const fixture = resolve(
  '../..',
  '.tmp/task38115-fixture-artifacts/bin/Winnow.Electron.Fixtures/debug/Winnow.Electron.Fixtures.dll',
)
let app: ElectronApplication, page: Page, directory: string, mode: Mode, workId: number
let endpoint: { address: string; token: string }
const errors: string[] = []
const details = () => page.locator('.avalon-details')
const reading = () => details().locator('.avalon-details-reading')
const match = () => details().getByRole('region', { name: 'Game match', exact: true })
type FixtureState = {
  searches: string[]
  metadataRequests: number[][]
  osDispatchAttempts: string[]
  rows: Record<string, Record<string, any>[]>
}
async function api<T>(input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const result = await window.winnow.request(input)
    if (!result.ok) throw Error(`${input.route}: ${result.status} ${result.message}`)
    return result.data
  }, input) as Promise<T>
}
async function control<T>(path: string, body?: unknown): Promise<T> {
  const response = await fetch(new URL(`/__fixture/details-reading/${path}`, endpoint.address), {
    method: body === undefined ? 'GET' : 'POST',
    headers: { Authorization: `Bearer ${endpoint.token}`, 'Content-Type': 'application/json' },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    signal: AbortSignal.timeout(45000),
  })
  if (!response.ok) throw Error(`Details reading fixture ${path}: HTTP ${response.status}`)
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}
async function tap(button: number) {
  // Simulated standard Gamepad API frames; no physical-controller claim.
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
async function activate(control: Locator) {
  await expect(control).toBeEnabled()
  await control.scrollIntoViewIfNeeded()
  await control.focus()
  await expect(control).toBeFocused()
  if (mode === 'fullscreen') await tap(0)
  else await page.keyboard.press('Enter')
}
async function surface(width = mode === 'desktop' ? 1200 : 1920, height = mode === 'desktop' ? 640 : 1080) {
  await app.evaluate(
    ({ BrowserWindow }, value) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setFullScreen(false)
      // Source layout tests include 700px, below the product window's normal minimum.
      window.setMinimumSize(0, 0)
      window.setContentSize(value.width, value.height)
      window.isFullScreen = () => value.mode === 'fullscreen'
      window.webContents.send('winnow:fullscreen:changed', value.mode === 'fullscreen')
      window.focus()
    },
    { mode, width, height },
  )
  await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
  await expect.poll(() => page.evaluate(() => innerWidth)).toBe(width)
}
async function navigate(name: string) {
  await activate(
    page.getByRole('navigation', { name: 'Main navigation' }).getByRole('button', { name, exact: true }),
  )
}
async function seed(kind = 'sparse', state?: number) {
  workId = (await control<{ workId: number }>('seed', { kind, state })).workId
  await navigate('Library')
  await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(1)
}
async function openDetails(section = 'Overview') {
  await activate(page.locator(`.avalon-library [data-avalon-game="${workId}"]`))
  await expect(details().locator('h1')).toBeVisible()
  if (section !== 'Overview') await activate(details().getByRole('tab', { name: section, exact: true }))
}
async function shot(name: string, subject: Locator) {
  await subject.scrollIntoViewIfNeeded()
  await expect(subject).toBeInViewport()
  await page.screenshot({ path: test.info().outputPath(`${mode}-${name}.png`), animations: 'disabled' })
}
async function scrollEnd() {
  await reading().evaluate((node) => {
    node.scrollTop = node.scrollHeight
  })
  await expect.poll(() => reading().evaluate((node) => node.scrollTop)).toBeGreaterThan(0)
  return reading().evaluate((node) => node.scrollTop)
}
async function wrongGame() {
  await activate(details().getByRole('button', { name: 'More', exact: true }))
  const action = page.getByRole('button', { name: 'Wrong game?', exact: true })
  // The production host also exposes Refetch and store actions. Traverse those
  // real preceding actions; the frozen source fixture wired only the match action.
  for (let step = 0; step < 30 && !(await action.evaluate((node) => node === document.activeElement)); step++)
    await page.keyboard.press('ArrowDown')
  await expect(action).toBeFocused()
  if (mode === 'fullscreen') await tap(0)
  else await page.keyboard.press('Enter')
  await expect(match().getByRole('textbox', { name: 'Game title or IGDB ID', exact: true })).toBeFocused()
}
async function settings(section: string) {
  if (!(await page.locator('.settings-page').isVisible()))
    await activate(page.getByRole('button', { name: 'Settings', exact: true }))
  await activate(
    page
      .getByRole('navigation', { name: 'Settings section' })
      .getByRole('button', { name: section, exact: true }),
  )
}
async function assertProse(scope: Locator, skipPageHeader = false) {
  const prose = scope.locator(
    skipPageHeader ? ':scope > :not(header) .reading-prose:visible' : '.reading-prose:visible',
  )
  await expect.poll(() => prose.count()).toBeGreaterThan(0)
  const values = await prose.evaluateAll((nodes) =>
    nodes.map((node) => {
      const style = getComputedStyle(node),
        bounds = node.getBoundingClientRect()
      return {
        text: node.textContent,
        maxWidth: style.maxWidth,
        width: bounds.width,
        align: style.textAlign,
        wrap: style.whiteSpace,
        marginLeft: style.marginLeft,
      }
    }),
  )
  for (const value of values) {
    expect(value.maxWidth, value.text ?? '').toBe('410px')
    expect(value.width, value.text ?? '').toBeLessThanOrEqual(410.1)
    expect(value.width).toBeGreaterThan(0)
    expect(['left', 'start']).toContain(value.align)
    expect(['normal', 'pre-line']).toContain(value.wrap)
    expect(value.marginLeft).toBe('0px')
  }
  return values.length
}

test.beforeAll(async () => {
  test.setTimeout(120000)
  await promisify(execFile)(
    'dotnet',
    [
      'build',
      resolve('../..', 'tests/Winnow.Electron.Fixtures/Winnow.Electron.Fixtures.csproj'),
      '--artifacts-path',
      resolve('../..', '.tmp/task38115-fixture-artifacts'),
      '--nologo',
      '--verbosity',
      'quiet',
    ],
    { windowsHide: true, timeout: 115000 },
  )
})
test.beforeEach(async ({}, info) => {
  test.setTimeout(120000)
  mode = info.title.startsWith('fullscreen') ? 'fullscreen' : 'desktop'
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-details-reading-'))
  errors.length = 0
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('tests/electron/details-reading-main.mjs'), '--data-dir', directory, '--no-sync'],
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
  expect((await fetch(new URL('/__fixture/details-reading/state', endpoint.address))).status).toBe(401)
  await mkdir(join(directory, 'covers'), { recursive: true })
  for (let index = 1; index <= 5; index++) {
    const bytes = await app.evaluate(({ nativeImage }, index) => {
      const pixels = Buffer.alloc(1280 * 720 * 4)
      for (let pixel = 0; pixel < 1280 * 720; pixel++) {
        pixels[pixel * 4] = 25 * index
        pixels[pixel * 4 + 1] = 140
        pixels[pixel * 4 + 2] = 220 - 20 * index
        pixels[pixel * 4 + 3] = 255
      }
      return nativeImage.createFromBitmap(pixels, { width: 1280, height: 720 }).toJPEG(90).toString('base64')
    }, index)
    await writeFile(
      join(directory, 'covers', `igdb-shot_detailshot${index}.src.jpg`),
      Buffer.from(bytes, 'base64'),
    )
  }
  await surface()
})
test.afterEach(async ({}, info) => {
  try {
    if (info.status !== info.expectedStatus && page)
      await info.attach('details-reading-failure', {
        body: await page.screenshot(),
        contentType: 'image/png',
      })
    expect(await app.evaluate(() => (globalThis as any).__identityProjections.dispatches)).toEqual([])
  } finally {
    await closeFixture(app, directory)
  }
  expect(errors).toEqual([])
})

for (const surfaceName of ['desktop', 'fullscreen'] as const) {
  for (const [state, expected] of [
    [0, 'Not fetched'],
    [1, 'No achievements'],
    [2, '0 of 1 unlocked · 0%'],
    [3, 'Unavailable'],
    [4, '1 of 1 unlocked · 100% · last known'],
  ] as const)
    test(`${surfaceName} source account 12345 achievement state ${state} displays ${expected}`, async () => {
      await surface(1920, 1080)
      await seed('achievements', state)
      await openDetails('Library')
      await expect(details().getByRole('heading', { name: 'Achievements', exact: true })).toBeVisible()
      await expect(
        details().getByRole('group', { name: 'Achievements by release', exact: true }),
      ).toBeVisible()
      await expect(details().locator('.detail-achievements [data-release-id="1"]')).toHaveText(
        `Achievement fixture · Steam: ${expected}`,
      )
      const data = await api<GameDetails>({ route: 'game.details', params: { workId } })
      expect(data.achievements).toHaveLength(1)
      if (state === 0 || state === 3) expect(data.achievements[0].hasKnownProgress).toBe(false)
      if (state === 2 || state === 4)
        expect(data.achievements[0]).toMatchObject({
          total: 1,
          unlocked: state === 4 ? 1 : 0,
          hasKnownProgress: true,
          isStale: state === 4,
        })
      if (state === 4) await shot('last-known-achievement', details().locator('.detail-achievements'))
    })

  test(`${surfaceName} five-shot lightbox stays in its window above Details with resolved theme fills and complete copy`, async () => {
    await seed('gallery')
    await openDetails()
    const originalDetails = await details().elementHandle(),
      windows = app.windows().length
    const thumbnail = details().getByRole('button', { name: 'Open screenshot 2 of 5', exact: true })
    await expect(thumbnail).toHaveAttribute('title', 'View screenshot 2 of 5')
    await activate(thumbnail)
    const lightbox = page.getByRole('dialog', { name: 'Screenshot 2 of 5', exact: true })
    await expect(lightbox).toBeVisible()
    expect(app.windows()).toHaveLength(windows)
    expect(windows).toBe(1)
    await expect(lightbox.getByRole('button', { name: 'Close screenshots', exact: true })).toBeFocused()
    expect(await originalDetails!.evaluate((node) => node.isConnected)).toBe(true)
    await expect
      .poll(() => lightbox.locator('img').evaluate((node) => (node as HTMLImageElement).naturalWidth))
      .toBeGreaterThan(0)
    const structural = await lightbox.evaluate((node) => {
      const card = document.querySelector('.avalon-details')!,
        image = node.querySelector('.screenshot-frame > img') as HTMLImageElement,
        close = node.querySelector('.screenshot-close') as HTMLElement
      const bounds = close.getBoundingClientRect()
      return {
        sameDocument: node.ownerDocument === card.ownerDocument,
        after: !!(card.compareDocumentPosition(node) & Node.DOCUMENT_POSITION_FOLLOWING),
        topmost: close.contains(
          document.elementFromPoint(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2),
        ),
        objectFit: getComputedStyle(image).objectFit,
        width: image.width,
        height: image.height,
      }
    })
    expect(structural).toMatchObject({ sameDocument: true, after: true, topmost: true, objectFit: 'contain' })
    expect(structural.width).toBeLessThanOrEqual(1280)
    expect(structural.height).toBeLessThanOrEqual(720)
    const overlay = await page
      .locator('.dialog-overlay:visible')
      .last()
      .evaluate((node) => {
        const rect = node.getBoundingClientRect()
        return {
          x: rect.x,
          y: rect.y,
          width: rect.width,
          height: rect.height,
          viewportWidth: innerWidth,
          viewportHeight: innerHeight,
        }
      })
    expect(overlay.x).toBeCloseTo(0, 1)
    expect(overlay.y).toBeCloseTo(0, 1)
    expect(overlay.width).toBeCloseTo(overlay.viewportWidth, 1)
    expect(overlay.height).toBeCloseTo(overlay.viewportHeight, 1)
    for (const [name, title] of [
      ['Close screenshots', 'Close screenshots'],
      ['Previous screenshot', 'Previous screenshot'],
      ['Next screenshot', 'Next screenshot'],
    ]) {
      const button = lightbox.getByRole('button', { name, exact: true })
      await expect(button).toHaveAttribute('title', title)
      expect((await button.getAttribute('aria-label'))?.trim()).toBeTruthy()
    }
    const copy = await lightbox.evaluate((node) =>
      [
        node.textContent,
        ...[...node.querySelectorAll('[aria-label],[title]')].flatMap((child) => [
          child.getAttribute('aria-label'),
          child.getAttribute('title'),
        ]),
      ]
        .filter(Boolean)
        .join('\n'),
    )
    expect(copy).not.toMatch(/TODO|PLACEHOLDER/i)
    const next = lightbox.getByRole('button', { name: 'Next screenshot', exact: true })
    await page.mouse.move(0, 0)
    const tokenFill = await next.evaluate((node) => {
      const probe = document.createElement('span')
      probe.style.backgroundColor = 'var(--avalon-lightbox-fill)'
      node.appendChild(probe)
      const value = {
        actual: getComputedStyle(node).backgroundColor,
        token: getComputedStyle(probe).backgroundColor,
        transition: getComputedStyle(node).transition,
        focusVisible: node.matches(':focus-visible'),
      }
      probe.remove()
      return value
    })
    expect(tokenFill.actual).toBe(tokenFill.token)
    expect(tokenFill.actual).not.toBe('rgba(0, 0, 0, 0)')
    await page.keyboard.press('Tab')
    await page.keyboard.press('Tab')
    await expect(next).toBeFocused()
    expect(await next.evaluate((node) => node.matches(':focus-visible'))).toBe(true)
    const activeFill = await next.evaluate((node) => {
      const probe = document.createElement('span')
      probe.style.backgroundColor = 'var(--avalon-lightbox-active-fill)'
      node.appendChild(probe)
      const value = {
        actual: getComputedStyle(node).backgroundColor,
        token: getComputedStyle(probe).backgroundColor,
        transition: getComputedStyle(node).transition,
        focusVisible: node.matches(':focus-visible'),
      }
      probe.remove()
      return value
    })
    // Keep the authored transition running and measure its final active state.
    await expect
      .poll(() => next.evaluate((node) => getComputedStyle(node).backgroundColor))
      .toBe(activeFill.token)
    const settledFill = await next.evaluate((node) => getComputedStyle(node).backgroundColor)
    expect(settledFill).not.toBe(tokenFill.actual)
    await test.info().attach('lightbox-native-geometry-and-fills', {
      body: JSON.stringify({ overlay, structural, tokenFill, activeFill, settledFill }),
      contentType: 'application/json',
    })
    for (let i = 0; i < 6; i++) {
      await page.keyboard.press('Tab')
      expect(await lightbox.evaluate((node) => node.contains(document.activeElement))).toBe(true)
    }
    await shot('five-shot-overlay', lightbox)
    await page.keyboard.press('Escape')
    await expect(lightbox).toHaveCount(0)
    await expect(details()).toBeVisible()
    await expect(thumbnail).toBeFocused()
    await expect(thumbnail).toHaveAttribute('aria-pressed', 'true')
  })

  test(`${surfaceName} exact forty-paragraph Overview keeps its scroll position across history navigation`, async () => {
    await seed('long')
    await openDetails()
    const original = await details().elementHandle()
    await activate(
      details().getByRole('button', { name: mode === 'desktop' ? 'Read more' : 'Read more →', exact: true }),
    )
    await expect(details().locator('.game-summary')).toHaveText(
      Array(40)
        .fill('This paragraph explains the places to explore and the choices to make in the game.')
        .join('\n\n'),
    )
    const offset = await scrollEnd()
    if (mode === 'desktop') {
      await activate(details().getByRole('tab', { name: 'Activity', exact: true }))
      await activate(details().getByRole('tab', { name: 'Overview', exact: true }))
    } else {
      await activate(details().getByRole('button', { name: 'Back to Overview', exact: true }))
      await activate(details().getByRole('button', { name: 'Play history →', exact: true }))
      await expect(details().getByRole('region', { name: 'History', exact: true })).toBeVisible()
      await activate(details().getByRole('button', { name: 'Back to Overview', exact: true }))
      await activate(details().getByRole('button', { name: 'Read more →', exact: true }))
    }
    await expect.poll(() => reading().evaluate((node) => node.scrollTop)).toBe(offset)
    expect(await original!.evaluate((node) => node.isConnected)).toBe(true)
    await page.screenshot({
      path: test.info().outputPath(`${mode}-restored-long-reading.png`),
      animations: 'disabled',
    })
  })

  test(`${surfaceName} Library Wrong game returns with its exact Astral query and five cached candidates after Back and Escape`, async () => {
    await seed()
    const before = await control<FixtureState>('state')
    await openDetails('Library')
    const original = await details().elementHandle()
    await wrongGame()
    const query = match().getByRole('textbox', { name: 'Game title or IGDB ID', exact: true })
    await query.fill('Astral cartographers')
    await page.keyboard.press('Enter')
    await expect(match().locator('.igdb-candidate-row')).toHaveCount(5)
    const candidates = await match().locator('.igdb-candidate-row').allTextContents()
    for (let index = 1; index <= 5; index++) {
      expect(candidates[index - 1]).toContain(`The Astral Cartographers ${index}`)
      expect(candidates[index - 1]).toContain(String(2019 + index))
      expect(candidates[index - 1]).toContain('PC (Microsoft Windows), PlayStation 5')
    }
    await shot('five-match-candidates', match())
    await activate(details().getByRole('button', { name: 'Back to Library', exact: true }))
    await expect(details().getByRole('tab', { name: 'Library', exact: true })).toHaveAttribute(
      'aria-selected',
      'true',
    )
    await expect(details().getByRole('button', { name: 'More', exact: true })).toBeFocused()
    await wrongGame()
    await expect(query).toHaveValue('Astral cartographers')
    await expect(match().locator('.igdb-candidate-row')).toHaveCount(5)
    expect(await match().locator('.igdb-candidate-row').allTextContents()).toEqual(candidates)
    await page.keyboard.press('Escape')
    await expect(match()).toHaveCount(0)
    await expect(details().getByRole('tab', { name: 'Library', exact: true })).toHaveAttribute(
      'aria-selected',
      'true',
    )
    expect(await original!.evaluate((node) => node.isConnected)).toBe(true)
    expect(await control<FixtureState>('state')).toMatchObject({
      searches: ['Astral cartographers'],
      metadataRequests: [],
      osDispatchAttempts: [],
      rows: before.rows,
    })
  })
}

test('desktop original Right Right Left End Home sequence selects each tab and retains two-pixel theme focus', async () => {
  await seed()
  await openDetails()
  await details().getByRole('tab', { name: 'Overview', exact: true }).focus()
  for (const [key, title] of [
    ['ArrowRight', 'Activity'],
    ['ArrowRight', 'Updates'],
    ['ArrowLeft', 'Activity'],
    ['End', 'Library'],
    ['Home', 'Overview'],
  ]) {
    await page.keyboard.press(key)
    const tab = details().getByRole('tab', { name: title, exact: true })
    await expect(tab).toBeFocused()
    await expect(tab).toHaveAttribute('aria-selected', 'true')
    const focus = await tab.evaluate((node) => {
      const probe = document.createElement('span')
      probe.style.color = 'var(--accent-foreground, var(--accent))'
      node.appendChild(probe)
      const value = {
        shadow: getComputedStyle(node).boxShadow,
        color: getComputedStyle(node).color,
        token: getComputedStyle(probe).color,
      }
      probe.remove()
      return value
    })
    expect(focus.shadow).toContain('0px 2px 0px')
    expect(focus.shadow).toContain(focus.color)
    expect(focus.color).toBe(focus.token)
    if (key === 'ArrowRight' && title === 'Activity') await shot('two-pixel-keyboard-tab', tab)
  }
})
test('fullscreen tab arrows retain manual activation and original Home End focus boundaries', async () => {
  await seed()
  await openDetails()
  await details().getByRole('tab', { name: 'Overview', exact: true }).focus()
  let previous = 'Overview'
  for (const [key, title] of [
    ['ArrowRight', 'Updates'],
    ['ArrowRight', 'Journal'],
    ['ArrowLeft', 'Updates'],
    ['End', 'Library'],
    ['Home', 'Overview'],
  ]) {
    await page.keyboard.press(key)
    const tab = details().getByRole('tab', { name: title, exact: true })
    await expect(tab).toBeFocused()
    await expect(details().getByRole('tab', { name: previous, exact: true })).toHaveAttribute(
      'aria-selected',
      'true',
    )
    await tap(0)
    await expect(tab).toHaveAttribute('aria-selected', 'true')
    previous = title
  }
})

for (const width of [700, 1400])
  test(`desktop ${width}px preserves all six reading-surface prose measures and wrapping Steam consent`, async () => {
    await surface(width, 900)
    await seed()
    const counts: Record<string, number> = {}
    for (const section of ['Platforms', 'Spending', 'Application', 'Library', 'Appearance']) {
      await settings(section)
      counts[section] = await assertProse(page.locator('.settings-page'), true)
      await shot(
        `${width}-${section.toLowerCase()}-prose`,
        page.locator('.settings-page > :not(header) .reading-prose:visible').first(),
      )
    }
    await activate(page.getByRole('button', { name: 'Theme Studio', exact: true }))
    const studio = page.locator('.theme-studio')
    await expect(studio.getByRole('heading', { name: 'Your Winnow', exact: true })).toBeVisible()
    await expect(studio.locator('.studio-theme-option .reading-prose')).toHaveCount(4)
    const typography = studio.getByRole('region', { name: 'Theme typography', exact: true })
    await expect(typography.locator('.reading-prose')).toHaveText(
      'Fonts and text size follow this Avalon palette. Unavailable fonts use the bundled font for that role.',
    )
    counts.Themes = await assertProse(studio)
    await shot(
      `${width}-theme-descriptions-prose`,
      studio.locator('.studio-theme-option .reading-prose').first(),
    )
    await shot(`${width}-theme-typography-prose`, typography.locator('.reading-prose'))
    await navigate('Merges')
    counts.Merges = await assertProse(page.locator('main'))
    await shot(`${width}-merge-prose`, page.locator('main .reading-prose:visible').first())
    await settings('Platforms')
    await activate(page.getByRole('button', { name: 'Sign in to Steam', exact: true }))
    const consent = page.getByRole('dialog', { name: 'Before you sign in', exact: true })
    await assertProse(consent)
    const costs = consent.locator('.reading-prose').filter({ hasText: 'Steam sessions last about a day.' })
    const bounds = (await costs.boundingBox())!
    expect(bounds.height).toBeGreaterThan(18)
    expect(bounds.width).toBeGreaterThan(0)
    expect(bounds.width).toBeLessThanOrEqual(410.1)
    await shot(`${width}-consent-prose`, costs)
    await test
      .info()
      .attach('measured-prose-counts', { body: JSON.stringify(counts), contentType: 'application/json' })
    await page.keyboard.press('Escape')
  })

test('fullscreen 1200px exact television paragraph retains 28px typography and the available reading width', async () => {
  await surface(1200, 900)
  await seed('prose')
  await openDetails()
  await activate(details().getByRole('button', { name: 'Read more →', exact: true }))
  const paragraph = details().locator('.game-summary.reading-prose')
  await expect(paragraph).toHaveText(
    Array(8).fill('Readable television paragraphs use the available layout width.').join(' '),
  )
  const metrics = await paragraph.evaluate((node) => {
    const style = getComputedStyle(node),
      root = getComputedStyle(document.documentElement)
    return {
      font: parseFloat(style.fontSize),
      maxWidth: style.maxWidth,
      width: node.getBoundingClientRect().width,
      whiteSpace: style.whiteSpace,
      themeScale: Number(root.getPropertyValue('--theme-text-scale').trim()) || 1,
      textScale: Number(root.getPropertyValue('--fullscreen-text-scale').trim()) || 1,
      zoom: Number(getComputedStyle(document.body).zoom),
    }
  })
  expect(metrics.font).toBeCloseTo(28 * metrics.themeScale * metrics.textScale, 3)
  expect(metrics.maxWidth).toBe('none')
  expect(metrics.width).toBeGreaterThan(410)
  expect(['normal', 'pre-line']).toContain(metrics.whiteSpace)
  await test
    .info()
    .attach('fullscreen-prose-geometry', { body: JSON.stringify(metrics), contentType: 'application/json' })
  await shot('1200-tv-prose', paragraph)
})
