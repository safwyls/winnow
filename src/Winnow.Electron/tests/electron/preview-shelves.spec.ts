import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Page,
  type Locator,
} from '@playwright/test'
import { build } from 'esbuild'
import { mkdtemp, mkdir, readFile, writeFile } from 'node:fs/promises'
import { resolve, join } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import type { Mode } from '../../src/renderer/api/types'
import { profileDirectory } from '../../src/main/storage'
import { DEFAULT_PROFILE } from '../../src/shared/theme'

const artifacts = resolve('../..', '.tmp/task38120-fixture-artifacts')
const fixture = join(artifacts, 'bin/Winnow.Electron.Fixtures/debug/Winnow.Electron.Fixtures.dll')
const probeDirectory = resolve('../..', '.tmp/task38120-native-probe')
let app: ElectronApplication, page: Page, directory: string, mode: Mode
let endpoint: { address: string; token: string }
const errors: string[] = []
type Call = {
  callId: number
  operation: string
  gateId?: string | null
  workId?: number
  provider?: string
  id?: string
  completed: boolean
  canceled: boolean
  responseReady: boolean
  delivered: boolean
  json?: string | null
}
type FixtureState = { kind: string; calls: Call[] }
const bubble = () => page.locator('.avalon-hover-preview')
const state = () => control<FixtureState>('state')
const cards = () => page.locator('.avalon-discover .avalon-feed-card')
const libraryCover = () => page.locator('.avalon-library [data-avalon-game="1"]')
async function control<T = unknown>(route: string, body?: unknown): Promise<T> {
  const response = await fetch(new URL(`/__fixture/recommendation-preview/${route}`, endpoint.address), {
    method: body === undefined ? 'GET' : 'POST',
    headers: {
      Authorization: `Bearer ${endpoint.token}`,
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!response.ok) throw Error(`Fixture ${route}: ${response.status} ${await response.text()}`)
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}
async function pad() {
  await page.evaluate(() => {
    const state = { pressed: [] as number[] }
    Object.assign(window, { previewPad: state })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          id: 'Native preview fixture controller',
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
      ;(window as unknown as { previewPad: { pressed: number[] } }).previewPad.pressed = pressed
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
}
async function surface(width: number, height: number) {
  await app.evaluate(
    ({ BrowserWindow }, { width, height, mode }) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setFullScreen(false)
      window.setMinimumSize(0, 0)
      window.setContentSize(width, height)
      window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
      window.focus()
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
async function navigate(name: 'For you' | 'Library') {
  const target = page
    .getByRole('navigation', { name: 'Main navigation', exact: true })
    .getByRole('button', { name, exact: true })
  await target.focus()
  if (mode === 'fullscreen') await tap(0)
  else await page.keyboard.press('Enter')
}
async function seed(kind: string, feed = true) {
  await navigate('Library')
  await control('seed', { kind })
  await expect(libraryCover()).toBeAttached()
  if (feed) {
    await navigate('For you')
    await expect(page.locator(mode === 'desktop' ? '.avalon-discover' : '.avalon-home')).toBeVisible()
  }
  await page.mouse.move(0, 0)
}
async function hover(target: Locator) {
  await target.hover({ position: { x: 40, y: 20 } })
  await expect(bubble()).toBeVisible()
}
async function arm(operation: string, extra: Record<string, unknown> = {}) {
  return await control<{ gateId: string }>('arm', {
    operation,
    workId: 1,
    ignoreCancellation: true,
    ...extra,
  })
}
async function entered(gateId: string) {
  await expect
    .poll(async () => (await state()).calls.some((call) => call.gateId === gateId && call.responseReady))
    .toBe(true)
}
async function canceled(gateId: string) {
  await expect
    .poll(async () => (await state()).calls.some((call) => call.gateId === gateId && call.canceled))
    .toBe(true)
}
async function released(gateId: string) {
  await control('release', { gateId })
  await expect
    .poll(async () => (await state()).calls.some((call) => call.gateId === gateId && call.completed))
    .toBe(true)
}
async function capture(name: string) {
  await page.screenshot({ path: test.info().outputPath(`${mode}-${name}.png`) })
}
async function withinWindow() {
  const rect = await bubble().boundingBox()
  expect(rect).not.toBeNull()
  const viewport = await page.evaluate(() => ({ width: innerWidth, height: innerHeight }))
  expect(rect!.x).toBeGreaterThanOrEqual(0)
  expect(rect!.y).toBeGreaterThanOrEqual(0)
  expect(rect!.x + rect!.width).toBeLessThanOrEqual(viewport.width)
  expect(rect!.y + rect!.height).toBeLessThanOrEqual(viewport.height)
  return rect!
}
async function disposeOwner() {
  await app.evaluate(({ BrowserWindow }) => {
    const original = BrowserWindow.getAllWindows()[0]!
    // Keep Electron available for its normal fixture shutdown after destroying
    // the real renderer owner. The empty window has no bridge or backend access.
    new BrowserWindow({
      show: false,
      webPreferences: { sandbox: true, contextIsolation: true, nodeIntegration: false },
    })
    original.destroy()
  })
  await expect.poll(() => page.isClosed()).toBe(true)
}

test.beforeAll(async () => {
  await readFile(fixture) // The coordinator builds this exact backend before granting the native slot.
  await mkdir(probeDirectory, { recursive: true })
  await build({
    entryPoints: [resolve('tests/electron/preview-shelves-probe.tsx')],
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
  test.setTimeout(90000)
  mode = info.title.startsWith('fullscreen') ? 'fullscreen' : 'desktop'
  endpoint = undefined as unknown as typeof endpoint
  errors.length = 0
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-recommendation-preview-'))
  const profile = structuredClone(DEFAULT_PROFILE)
  profile.appearance.scale = 100
  profile.appearance.reducedMotion = true
  const preferences = profileDirectory(join(directory, 'electron-userdata'), directory)
  await mkdir(preferences, { recursive: true })
  await writeFile(join(preferences, 'preferences.json'), JSON.stringify(profile))
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('tests/electron/preview-shelves-main.mjs'), '--data-dir', directory, '--no-sync'],
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
  expect((await fetch(new URL('/__fixture/recommendation-preview/state', endpoint.address))).status).toBe(401)
  await surface(1600, 1000)
  await expect
    .poll(() => page.evaluate(() => getComputedStyle(document.body).zoom))
    .toBe(mode === 'desktop' ? '1' : '0.85')
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
})
test.afterEach(async ({}, info) => {
  try {
    if (endpoint)
      await info.attach('preview-fixture-ledger', {
        body: JSON.stringify(await state(), null, 2),
        contentType: 'application/json',
      })
    if (app) {
      const dispatches = await app.evaluate(() => (globalThis as any).__identityProjections.dispatches)
      await info.attach('intercepted-action-dispatches', {
        body: JSON.stringify(dispatches),
        contentType: 'application/json',
      })
      expect(dispatches).toEqual([])
    }
    if (info.status !== info.expectedStatus && page && !page.isClosed()) {
      await info.attach('preview-before-teardown', {
        body: await page.screenshot(),
        contentType: 'image/png',
      })
      await info.attach('preview-before-teardown-dom', {
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

async function pixelProbe(kind: string) {
  await app.evaluate(
    async ({ BrowserWindow }, { path, kind }) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setContentSize(200, 100)
      await window.loadFile(path, { query: { kind } })
    },
    { path: join(probeDirectory, 'index.html'), kind },
  )
  await expect(bubble()).toBeVisible()
  await expect(bubble().locator('.avalon-preview-shape path').last()).toHaveAttribute('d', /^M /)
  await page.waitForFunction(() =>
    [...document.images].every((image) => image.complete && image.naturalWidth > 0),
  )
}
async function pixels(name: string) {
  const png = await page.screenshot({ omitBackground: true, path: test.info().outputPath(`${name}.png`) })
  return await app.evaluate(
    ({ nativeImage }, data) => {
      const image = nativeImage.createFromBuffer(Buffer.from(data))
      return { size: image.getSize(), bitmap: Array.from(image.toBitmap()) }
    },
    [...png],
  )
}
function pixel(value: { size: { width: number }; bitmap: number[] }, x: number, y: number) {
  return value.bitmap.slice((y * value.size.width + x) * 4, (y * value.size.width + x) * 4 + 4)
}
test('desktop source 200 by 100 artwork continues through the pointer without a seam or rectangular spill', async () => {
  await pixelProbe('artwork')
  const value = await pixels('source-bubble-artwork-200x100')
  expect(value.size).toEqual({ width: 200, height: 100 })
  expect(pixel(value, 2, 10)[3]).toBe(0)
  expect(pixel(value, 30, 40)).toEqual(pixel(value, 10, 40))
  expect(pixel(value, 30, 40)).toEqual(pixel(value, 5, 40))
  expect(pixel(value, 30, 40)[2]).toBeGreaterThanOrEqual(44)
  expect(pixel(value, 30, 40)[2]).toBeLessThanOrEqual(48)
  expect(pixel(value, 30, 40)[3]).toBe(255)
})
test('desktop source flipped pointer moves artwork geometry and child gutter together', async () => {
  await pixelProbe('plain')
  const child = page.locator('[data-source-child]')
  const before = (await child.boundingBox())!
  expect(before.x).toBe(26)
  await page.evaluate(() =>
    (window as unknown as { previewPixelProbe: { flip(): void } }).previewPixelProbe.flip(),
  )
  await expect(bubble()).toHaveAttribute('data-arrow-side', 'right')
  const after = (await child.boundingBox())!
  expect(after.x).toBe(16)
  expect(after.width).toBe(before.width)
  const value = await pixels('source-bubble-flipped-200x100')
  expect(pixel(value, 197, 10)[3]).toBe(0)
  expect(pixel(value, 195, 40)[3]).toBe(255)
  expect(pixel(value, 2, 40)[3]).toBe(255)
})

const compactRatings = 'IGDB: 81 \u00b7 IGDB critics: 88 \u00b7 Steam: Very Positive'
async function sourceSlot(top = 20, library = false) {
  await surface(900, library ? 500 : 400)
  const host = library ? libraryCover().locator('..') : page.locator('.avalon-feed-card-host').first()
  // Equivalent to the original test's Canvas.SetLeft/SetTop and Width inputs.
  // The mounted production card, preview portal and authenticated bridge remain intact.
  await host.evaluate(
    (node, top) =>
      Object.assign((node as HTMLElement).style, {
        position: 'fixed',
        left: '20px',
        top: `${top}px`,
        width: '220px',
        zIndex: '80',
      }),
    top,
  )
  return library ? libraryCover() : host.locator('[data-avalon-game]')
}
async function previewTypography() {
  const value = await bubble().evaluate((node) => {
    const title = getComputedStyle(node.querySelector('h3')!)
    const meta = getComputedStyle(node.querySelector('.avalon-preview-meta')!)
    const ratings = node.querySelector('.avalon-preview-ratings')
    const summary = node.querySelector('.avalon-preview-summary')
    const style = getComputedStyle(node)
    return {
      titleSize: title.fontSize,
      titleWeight: title.fontWeight,
      titleFamily: title.fontFamily,
      displayFamily: style.getPropertyValue('--font-display').trim(),
      metaSize: meta.fontSize,
      metaColor: meta.color,
      muted: style.getPropertyValue('--muted').trim(),
      gap: getComputedStyle(node.querySelector('.avalon-preview-copy')!).gap,
      ratingsSize: ratings && getComputedStyle(ratings).fontSize,
      summarySize: summary && getComputedStyle(summary).fontSize,
      summaryLines: summary && getComputedStyle(summary).webkitLineClamp,
    }
  })
  expect(value.titleSize).toBe('22px')
  expect(value.titleWeight).toBe('700')
  expect(value.titleFamily).toBe(value.displayFamily)
  expect(value.metaSize).toBe('13px')
  expect(value.gap).toBe('10px')
  if (value.ratingsSize) expect(value.ratingsSize).toBe('12px')
  if (value.summarySize) {
    expect(value.summarySize).toBe('13px')
    expect(value.summaryLines).toBe('4')
  }
  await test
    .info()
    .attach('source-preview-typography', { body: JSON.stringify(value), contentType: 'application/json' })
}
test('desktop source hover loads compact populations lazily with accessible counts and no repeated reason', async () => {
  await seed('ratings')
  const target = await sourceSlot()
  const before = (await state()).calls.filter((call) => call.operation === 'details').length
  const held = await arm('details')
  await hover(target)
  await entered(held.gateId)
  expect((await state()).calls.filter((call) => call.operation === 'details')).toHaveLength(before + 1)
  await expect(bubble().locator('.avalon-preview-ratings')).toHaveCount(0)
  await released(held.gateId)
  const ratings = bubble().locator('.avalon-preview-ratings')
  await expect(ratings).toHaveText(compactRatings)
  expect(await ratings.getAttribute('title')).toBeNull()
  expect(await ratings.innerText()).not.toMatch(/1,500|1500|100|12|%/)
  await expect(ratings).toHaveAccessibleName(
    /81 out of 100, from 100 ratings.*88 out of 100, from 12 critic scores.*93% positive, from 1,500 reviews/,
  )
  await expect(bubble().locator('.avalon-preview-reason')).toHaveCount(0)
  await expect(bubble()).not.toContainText('Reason')
  await previewTypography()
  await withinWindow()
  await expect(bubble().locator('.artwork img')).toBeVisible()
  await capture('source-compact-ratings')
  await page.mouse.move(880, 380)
  await expect(bubble()).toHaveCount(0)
})
test('desktop source missing ratings reserve no preview space', async () => {
  await seed('missing')
  const target = await sourceSlot()
  const held = await arm('details')
  await hover(target)
  await entered(held.gateId)
  const before = await bubble().boundingBox()
  await expect(bubble().locator('.avalon-preview-ratings')).toHaveCount(0)
  await released(held.gateId)
  await expect(bubble().locator('.avalon-preview-ratings')).toHaveCount(0)
  const after = await bubble().boundingBox()
  expect(after?.width).toBe(before?.width)
  expect(after?.height).toBe(before?.height)
  await capture('source-missing-ratings')
})
for (const disposed of [false, true])
  test(`desktop source ${disposed ? 'disposed' : 'closed'} preview cancels ratings and ignores the late response`, async () => {
    await seed('ratings')
    const target = await sourceSlot()
    const held = await arm('details')
    await hover(target)
    await entered(held.gateId)
    if (disposed) await navigate('Library')
    else await page.mouse.move(880, 380)
    await expect(bubble()).toHaveCount(0)
    await canceled(held.gateId)
    await released(held.gateId)
    await expect(bubble()).toHaveCount(0)
    await expect(page.locator('.avalon-preview-ratings')).toHaveCount(0)
  })
test('desktop source ratings growth near the bottom reclamps the measured preview within the window', async () => {
  await seed('ratings')
  const target = await sourceSlot(340)
  const held = await arm('details')
  await hover(target)
  await entered(held.gateId)
  const before = (await bubble().boundingBox())!
  await released(held.gateId)
  await expect(bubble().locator('.avalon-preview-ratings')).toHaveText(compactRatings)
  await expect.poll(async () => (await bubble().boundingBox())!.height).toBeGreaterThan(before.height)
  const after = await withinWindow()
  expect(after.y).toBeLessThan(before.y)
  await capture('source-bottom-ratings-growth')
})
for (const recycle of [false, true])
  test(`desktop source library ${recycle ? 'same ID Replacement recycle' : 'pointer exit'} closes preview and cancels pending metadata`, async () => {
    await seed('library', false)
    const target = libraryCover()
    const held = await arm('details')
    await hover(target)
    await entered(held.gateId)
    await expect(bubble().getByRole('heading', { name: 'Hades', exact: true })).toBeVisible()
    if (recycle) {
      await control('change', { kind: 'replacement' })
      await expect(libraryCover()).toHaveAccessibleName(/Replacement/)
    } else await page.mouse.move(1580, 980)
    await expect(bubble()).toHaveCount(0)
    await canceled(held.gateId)
    await released(held.gateId)
    await expect(bubble()).toHaveCount(0)
    await expect(page.locator('.avalon-preview-ratings')).toHaveCount(0)
    if (recycle) await expect(libraryCover()).toHaveAttribute('data-avalon-game', '1')
  })

const titles = ['Disco Elysium', 'Hollow Knight', 'Outer Wilds', 'Hades', 'Subnautica']
const reasons = [
  '2 hours played, then left untouched for 8 months.',
  'An unfinished journey through Hallownest.',
  'You played 48 minutes last winter. There is still a whole solar system to unravel.',
  'One more escape attempt.',
  'Your last dive was a year ago.',
]
for (const width of [1600, 900])
  test(`desktop source two five-card shelves and Hades quick details at ${width} by 1000`, async () => {
    await surface(width, 1000)
    await seed('shelf')
    const shelves = page.locator('.avalon-discover .avalon-shelf')
    await expect(shelves).toHaveCount(2)
    await expect(cards()).toHaveCount(10)
    for (let shelf = 0; shelf < 2; shelf++) {
      const row = shelves.nth(shelf)
      await expect(row.locator('h2')).toHaveText(
        shelf ? 'Still waiting for their first session' : 'Worth another look',
      )
      await expect(row.locator('header > p')).toHaveText(
        shelf ? 'A fresh start, already in your library.' : 'Games you started, with a reason to come back.',
      )
      await expect(row.locator('.avalon-feed-card-caption strong')).toHaveText(titles)
      await expect(row.locator('.avalon-feed-card-caption > span')).toHaveText(reasons)
      const geometry = await row.locator('.avalon-desktop-covers').evaluate((node) => ({
        gap: getComputedStyle(node).gap,
        cards: [...node.querySelectorAll('.avalon-cover')].map((card) => ({
          width: parseFloat(getComputedStyle(card).width),
          height: parseFloat(getComputedStyle(card).height),
        })),
        available: node.getBoundingClientRect().width,
      }))
      expect(geometry.gap).toBe('18px')
      for (const card of geometry.cards) {
        expect(card.width).toBeGreaterThanOrEqual(179.9)
        expect(card.width).toBeLessThanOrEqual(240.1)
        expect(card.height / card.width).toBeCloseTo(1.5, 2)
      }
      await test.info().attach(`shelf-${shelf}-geometry`, {
        body: JSON.stringify(geometry),
        contentType: 'application/json',
      })
    }
    await capture(`source-shelves-${width}`)
    const target = shelves.first().locator('[data-avalon-game="4"]')
    await hover(target)
    await expect(bubble().getByRole('heading', { name: 'Hades', exact: true })).toBeVisible()
    await expect(bubble().locator('.avalon-preview-summary')).toHaveText(
      'Defy the god of the dead as you battle out of the Underworld.',
    )
    await expect(bubble()).not.toContainText(reasons[3])
    const anchor = (await target.boundingBox())!
    const measured = await withinWindow()
    const rightSpace = width - anchor.x - anchor.width
    const onLeft = rightSpace < measured.width + 8 && anchor.x > rightSpace
    await expect(bubble()).toHaveAttribute('data-arrow-side', onLeft ? 'right' : 'left')
    expect(onLeft ? measured.x + measured.width : measured.x).toBeCloseTo(
      onLeft ? anchor.x : anchor.x + anchor.width,
      0,
    )
    expect(measured.x).toBeGreaterThanOrEqual(8)
    expect(measured.x + measured.width).toBeLessThanOrEqual(width - 8)
    await test.info().attach('actual-host-preview-placement', {
      body: JSON.stringify({ width, anchor, measured, rightSpace, onLeft }),
      contentType: 'application/json',
    })
    await previewTypography()
    await capture(`source-quick-details-${width}`)
  })
for (const width of [1920, 1280])
  test(`fullscreen separate shelf presentation keeps the selected hero and controller navigation without pointer previews at ${width}`, async () => {
    await surface(width, 1080)
    await seed('shelf')
    const row = page.locator('.avalon-retained-row[data-row-active="true"]')
    const covers = row.locator('[data-avalon-game]')
    const capacity = width === 1280 ? 4 : 5
    await expect(covers).toHaveCount(capacity)
    await covers.first().focus()
    await expect(covers.first()).toBeFocused()
    for (let index = 0; index < 5; index++) {
      if (index) await tap(15)
      await expect(row.locator(`[data-avalon-game="${index + 1}"]`)).toBeFocused()
      await expect(page.locator('.avalon-home-hero h1')).toHaveText(titles[index])
      await expect(page.locator('.avalon-home-hero > p')).toHaveText(reasons[index])
    }
    if (width === 1280) await expect(covers).toHaveCount(1)
    await capture(`reachable-fifth-${width}`)
    await tap(14)
    await expect(row.locator('[data-avalon-game="4"]')).toBeFocused()
    const hero = page.locator('.avalon-home-hero')
    await expect(hero).toContainText('Hades')
    await expect(hero).toContainText(reasons[3])
    const before = (await state()).calls.filter((call) => call.operation === 'details').length
    await row.locator('[data-avalon-game="4"]').hover()
    await expect(bubble()).toHaveCount(0)
    expect((await state()).calls.filter((call) => call.operation === 'details')).toHaveLength(before)
    const navigation = page.getByRole('navigation', { name: 'Main navigation', exact: true })
    for (const bumper of ['LB', 'RB'])
      await expect(navigation.locator(`[data-root-bumper="${bumper}"]`)).toBeVisible()
    await capture(`selected-hero-${width}`)
    await tap(7)
    await expect(page.locator('.avalon-retained-row[data-row-active="true"] [data-avalon-game]')).toHaveCount(
      capacity,
    )
    await expect(
      page.locator('.avalon-retained-row[data-row-active="true"] .avalon-home-row'),
    ).toHaveAttribute('data-shelf-id', 'visual-1')
    await expect(bubble()).toHaveCount(0)
  })

test('desktop preview releases a pending real artwork request ignores its late bytes and reacquires on attachment', async () => {
  await seed('ratings')
  const target = await sourceSlot()
  const held = await arm('image', { workId: null, provider: 'steam-hero', id: '123' })
  await hover(target)
  await entered(held.gateId)
  await expect(bubble().locator('.artwork img')).toHaveCount(0)
  await page.mouse.move(880, 380)
  await expect(bubble()).toHaveCount(0)
  await canceled(held.gateId)
  await released(held.gateId)
  const previous = (await state()).calls.find((call) => call.gateId === held.gateId)!
  expect(previous.delivered).toBe(false)
  await expect(bubble()).toHaveCount(0)
  await hover(target)
  const image = bubble().locator('.artwork img')
  await expect(image).toBeVisible()
  await expect
    .poll(() => image.evaluate((node) => (node as HTMLImageElement).naturalWidth))
    .toBeGreaterThan(0)
  const current = (await state()).calls.filter(
    (call) =>
      call.operation === 'image' && call.provider === 'steam-hero' && call.id === '123' && call.delivered,
  )
  expect(current.length).toBeGreaterThanOrEqual(1)
  expect(current.every((call) => call.callId !== previous.callId)).toBe(true)
  await image.evaluate((node) => Object.assign(window, { retainedPreviewImage: node }))
  await capture('artwork-reacquired')
  await page.mouse.move(880, 380)
  await expect(bubble()).toHaveCount(0)
  expect(
    await page.evaluate(() => {
      const image = (window as unknown as { retainedPreviewImage: HTMLImageElement }).retainedPreviewImage
      return { connected: image.isConnected, source: image.getAttribute('src') }
    }),
  ).toEqual({ connected: false, source: null })
})
test('desktop detached metadata cannot replace the reattached preview artwork candidates', async () => {
  await seed('backdrop')
  const target = await sourceSlot()
  const old = await arm('artworkState', { slot: 'Hero' })
  await hover(target)
  await entered(old.gateId)
  await page.mouse.move(880, 380)
  await expect(bubble()).toHaveCount(0)
  await canceled(old.gateId)
  await control('change', { kind: 'currentart' })
  const current = await arm('artworkState', { slot: 'Hero' })
  await hover(target)
  await entered(current.gateId)
  await released(old.gateId)
  expect(
    (await state()).calls.filter((call) => call.operation === 'image' && call.id === 'staleart'),
  ).toEqual([])
  await expect(bubble().locator('.artwork img')).toHaveCount(0)
  await released(current.gateId)
  const image = bubble().locator('.artwork img')
  await expect(image).toBeVisible()
  await expect
    .poll(() => image.evaluate((node) => (node as HTMLImageElement).naturalWidth))
    .toBeGreaterThan(0)
  const calls = (await state()).calls
  expect(calls.find((call) => call.gateId === old.gateId)?.json).toContain('staleart')
  expect(calls.find((call) => call.gateId === current.gateId)?.json).toContain('currentart')
  expect(calls.filter((call) => call.operation === 'image' && call.id === 'staleart')).toEqual([])
  expect(
    calls.some(
      (call) =>
        call.operation === 'image' &&
        call.provider === 'igdb-backdrop' &&
        call.id === 'currentart' &&
        call.delivered,
    ),
  ).toBe(true)
  const color = await image.evaluate((node) => {
    const canvas = document.createElement('canvas')
    canvas.width = canvas.height = 1
    const context = canvas.getContext('2d')!
    context.drawImage(node as HTMLImageElement, 0, 0, 1, 1, 0, 0, 1, 1)
    return [...context.getImageData(0, 0, 1, 1).data]
  })
  expect(color).toEqual([0, 128, 0, 255])
  await capture('only-current-artwork')
})
