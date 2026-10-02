import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import electronPath from 'electron'
import { build } from 'esbuild'
import { mkdir, mkdtemp, readFile, readdir, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { closeFixture } from './fixture-cleanup'
import type { LibraryGame, FeedSnapshot, Mode } from '../../src/renderer/api/types'

const output = resolve('../..', '.tmp/design-preview-native')
const surfaces = [
  'GameDetailsView',
  'GameTileView',
  'RowCoverView',
  'FeedCardView',
  'FeedView',
  'ActionBarView',
  'FilterPanelView',
  'StoresView',
  'AppearanceView',
  'MergeQueueView',
  'AccountStatsView',
  'StatsView',
  'GameplayStatsView',
  'LibrarySettingsView',
  'ApplicationSettingsView',
] as const
type Surface = 'Shell' | (typeof surfaces)[number]
type PreviewState = {
  mode: Mode
  surfaces: readonly string[]
  games: LibraryGame[]
  workspace: { ownerships: unknown[] }
  feed: FeedSnapshot
  details: unknown
  requests: string[]
  blocked: string[]
  writes: unknown[]
}
type NativePreview = { state(): PreviewState; show(surface: Surface): void }
let app: ElectronApplication | undefined, page: Page | undefined, directory: string
const errors: string[] = []
const measurements: Record<string, unknown> = {}

test.beforeAll(async () => {
  await mkdir(output, { recursive: true })
  await build({
    entryPoints: [resolve('tests/electron/preview-design-probe.tsx')],
    outfile: join(output, 'probe.js'),
    bundle: true,
    platform: 'browser',
    format: 'iife',
    jsx: 'automatic',
    define: { 'process.env.NODE_ENV': '"production"' },
    // Match Vite's emitted SVG URLs: UTF-8 data URLs are not safe inside unquoted CSS mask urls.
    loader: { '.ttf': 'file', '.woff': 'file', '.woff2': 'file', '.svg': 'file' },
    plugins: [
      {
        name: 'source-assets',
        setup(bundle) {
          bundle.onResolve({ filter: /\?raw$/ }, (args) => ({
            path: resolve(args.resolveDir, args.path.slice(0, -4)),
            namespace: 'raw',
          }))
          bundle.onLoad({ filter: /.*/, namespace: 'raw' }, async (args) => ({
            contents: await readFile(args.path, 'utf8'),
            loader: 'text',
          }))
        },
      },
    ],
    logLevel: 'silent',
  })
  await writeFile(
    join(output, 'index.html'),
    '<!doctype html><html><head><meta charset="utf-8"><link rel="stylesheet" href="probe.css"></head><body><div id="root"></div><script src="probe.js"></script></body></html>',
  )
})
test.beforeEach(() => {
  app = undefined
  page = undefined
  errors.length = 0
  for (const key of Object.keys(measurements)) delete measurements[key]
})
test.afterEach(async ({}, info) => {
  try {
    if (page && !page.isClosed()) {
      await page.screenshot({ path: info.outputPath('before-teardown.png') })
      measurements.fixture = await state()
    }
    if (app) {
      measurements.host = await app.evaluate(({ app, BrowserWindow }) => ({
        userData: app.getPath('userData'),
        ledger: (globalThis as unknown as { designPreviewHostLedger: unknown }).designPreviewHostLedger,
        childPids: (process as NodeJS.Process & { _getActiveHandles(): { pid?: number }[] })
          ._getActiveHandles()
          .flatMap((handle) => (handle.pid ? [handle.pid] : [])),
        preferences: (globalThis as unknown as { designPreviewHostPreferences: unknown })
          .designPreviewHostPreferences,
        registeredPreloads: BrowserWindow.getAllWindows().flatMap((window) =>
          window.webContents.session.getPreloadScripts(),
        ),
      }))
    }
  } finally {
    try {
      await closeFixture(app)
    } finally {
      await writeFile(info.outputPath('preview-evidence.json'), JSON.stringify(measurements, null, 2))
    }
  }
  expect(errors).toEqual([])
})
async function start(mode: Mode, surface: Surface, width: number, height: number) {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-design-preview-'))
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/preview-design-main.mjs'),
      '--data-dir',
      directory,
      join(output, 'index.html'),
      mode,
      surface,
      '--force-color-profile=srgb',
    ],
    env: Object.fromEntries(
      Object.entries(process.env).filter(
        ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
      ),
    ) as Record<string, string>,
    chromiumSandbox: true,
  })
  page = await app.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await app.evaluate(
    ({ BrowserWindow }, size) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setMinimumSize(0, 0)
      window.setContentSize(size.width, size.height)
      window.focus()
    },
    { width, height },
  )
  await expect(page.locator('#root')).toHaveAttribute('data-preview-surface', surface)
  await frames()
  measurements.viewport = await page.evaluate(() => ({
    width: innerWidth,
    height: innerHeight,
    devicePixelRatio,
  }))
}
async function frames() {
  await page!.evaluate(async () => {
    await document.fonts.ready
    await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
  })
}
async function state() {
  return page!.evaluate(() =>
    (window as unknown as { designPreviewNative: NativePreview }).designPreviewNative.state(),
  )
}
async function capture(name: string) {
  await frames()
  await page!.screenshot({ path: test.info().outputPath(`${name}.png`) })
}
async function dragonAsset() {
  const dragon = page!.locator('.avalon-brand .dragon-mark')
  await expect(dragon).toBeVisible()
  const asset = await dragon.evaluate(async (node) => {
    const style = getComputedStyle(node)
    const source = style.maskImage
    if (source === 'none') return { source, color: [], width: 0, height: 0 }
    const image = new Image()
    image.src = source.slice(4, -1).replace(/^"|"$/g, '')
    await image.decode()
    return {
      source,
      color: (style.backgroundColor.match(/[\d.]+/g) ?? []).slice(0, 3).map(Number),
      width: image.naturalWidth,
      height: image.naturalHeight,
    }
  })
  expect(asset.source).toMatch(/dragon[^/]*\.svg/)
  expect(asset.width).toBeGreaterThan(0)
  expect(asset.height).toBeGreaterThan(0)
  const bytes = await dragon.screenshot({ path: test.info().outputPath('source-dragon-mask.png') })
  const painted = await app!.evaluate(
    ({ nativeImage }, { bytes, color }) => {
      const image = nativeImage.createFromBuffer(Buffer.from(bytes))
      const bitmap = image.toBitmap()
      let accentPixels = 0
      for (let offset = 0; offset < bitmap.length; offset += 4) {
        if (
          Math.abs(bitmap[offset + 2]! - color[0]!) <= 3 &&
          Math.abs(bitmap[offset + 1]! - color[1]!) <= 3 &&
          Math.abs(bitmap[offset]! - color[2]!) <= 3
        )
          accentPixels++
      }
      return {
        size: image.getSize(),
        accentPixels,
        pixels: bitmap.length / 4,
        fraction: accentPixels / (bitmap.length / 4),
      }
    },
    { bytes: Array.from(bytes), color: asset.color },
  )
  expect(painted.fraction).toBeGreaterThan(0.1)
  expect(painted.fraction).toBeLessThan(0.9)
  measurements.dragonAsset = { ...asset, ...painted }
}
async function isolation() {
  const fixture = await state()
  expect(fixture.blocked).toEqual([])
  expect(fixture.writes).toEqual([])
  const observed = await app!.evaluate(({ app, BrowserWindow }) => ({
    root: app.getPath('userData'),
    ledger: (
      globalThis as unknown as {
        designPreviewHostLedger: { network: string[]; navigation: string[]; windows: string[] }
      }
    ).designPreviewHostLedger,
    children: (process as NodeJS.Process & { _getActiveHandles(): { pid?: number }[] })
      ._getActiveHandles()
      .filter((handle) => handle.pid).length,
    preferences: (globalThis as unknown as { designPreviewHostPreferences: unknown })
      .designPreviewHostPreferences,
    registeredPreloads: BrowserWindow.getAllWindows()[0]!.webContents.session.getPreloadScripts(),
  }))
  expect(observed.root).toBe(directory)
  expect(observed.ledger).toEqual({ network: [], navigation: [], windows: [] })
  expect(observed.children).toBe(0)
  expect(observed.preferences).toMatchObject({
    sandbox: true,
    contextIsolation: true,
    nodeIntegration: false,
  })
  expect(observed.preferences).not.toHaveProperty('preload')
  expect(observed.registeredPreloads).toEqual([])
  expect(
    await page!.evaluate(() => ({
      require: typeof (window as unknown as { require?: unknown }).require,
      process: typeof (window as unknown as { process?: unknown }).process,
    })),
  ).toEqual({ require: 'undefined', process: 'undefined' })
  const files = await readdir(directory)
  expect(files).not.toContain('backend')
  expect(files).not.toContain('winnow.db')
  measurements.isolation = { ...observed, files }
}
async function validateFixture() {
  const fixture = await state()
  expect(fixture.surfaces).toEqual(surfaces)
  expect(fixture.games).toHaveLength(8)
  expect(fixture.workspace.ownerships).toHaveLength(9)
  const witcher = fixture.games.find((game) => game.title === 'The Witcher 3: Wild Hunt')!
  expect(witcher.entries.map((entry) => entry.store).sort()).toEqual(['gog', 'steam'])
  expect(fixture.games.find((game) => game.title === 'Stardew Valley')!.bucket).toBe('stale_but_patched')
  expect(fixture.games.find((game) => game.title === 'Hollow Knight')!.bucket).toBe('bounced')
  expect(fixture.feed.shelves.map((shelf) => ({ title: shelf.title, count: shelf.items.length }))).toEqual([
    { title: 'Patched while you were away', count: 2 },
    { title: 'Never opened', count: 2 },
  ])
  for (const shelf of fixture.feed.shelves)
    for (const card of shelf.items)
      expect(
        fixture.games.some((game) => game.entries.some((entry) => entry.ownershipId === card.ownershipId)),
      ).toBe(true)
  measurements.sourceFixture = fixture
}
async function populatedDetails(mode: Mode, captureName: string) {
  const details = page!.locator('.avalon-details')
  await expect(details).toBeVisible()
  await expect(details.locator('h1')).toHaveText('Stardew Valley')
  await details.getByRole('tab', { name: /^Updates(?:,|$)/ }).click()
  await expect(details).toContainText('Patch 1.6.15')
  if (mode === 'fullscreen') {
    await details.getByRole('button', { name: 'Patch notes', exact: true }).click()
    await expect(page!.getByRole('region', { name: 'Patch notes', exact: true })).toBeVisible()
    await expect(page!.getByRole('group', { name: 'Patch notes controls' })).toContainText('Back')
  } else await details.locator('summary', { hasText: 'GOG patch notes' }).click()
  await expect(details.locator('.gog-patch-notes-text')).toContainText('Fixed several multiplayer desyncs')
  await expect(details.locator('.gog-patch-notes-text')).toContainText('meadowlands farm layout')
  await capture(captureName)
}

for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} isolated preview shell loads exact fabricated feed and eight grouped games then opens populated Stardew details without native side effects`, async () => {
    await start(mode, 'Shell', mode === 'fullscreen' ? 1920 : 1280, mode === 'fullscreen' ? 1080 : 820)
    await expect(page!.locator(`.avalon-shell.${mode}`)).toBeVisible()
    await expect(page!.locator('.startup-presentation')).toHaveCount(0)
    await dragonAsset()
    await validateFixture()
    if (mode === 'fullscreen') {
      for (const { title, games } of [
        {
          title: 'Patched while you were away',
          games: [
            { id: 4, title: 'Stardew Valley' },
            { id: 1, title: 'Hollow Knight' },
          ],
        },
        {
          title: 'Never opened',
          games: [
            { id: 2, title: 'Disco Elysium' },
            { id: 7, title: 'Slay the Spire' },
          ],
        },
      ]) {
        await page!.getByRole('button', { name: `Show ${title}`, exact: true }).click()
        await expect(page!.locator('.avalon-home-shelf h2')).toHaveText(title)
        const active = page!.locator('.avalon-home-shelf .avalon-retained-row[data-row-active="true"]')
        await expect(active).toHaveAttribute('aria-hidden', 'false')
        await expect(active.locator('.avalon-cover')).toHaveCount(2)
        for (const game of games)
          await expect(active.locator(`[data-avalon-game="${game.id}"]`)).toHaveAccessibleName(
            new RegExp(game.title),
          )
      }
    } else {
      await expect(page!.locator('.avalon-discover .avalon-feed-card')).toHaveCount(4)
      await expect(
        page!.getByRole('heading', { name: 'Patched while you were away', exact: true }),
      ).toBeVisible()
      await expect(page!.getByRole('heading', { name: 'Never opened', exact: true })).toBeAttached()
    }
    await capture(`${mode}-source-feed`)
    await page!
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'Library', exact: true })
      .click()
    const library = page!.locator('.avalon-library')
    await expect(library.locator('[data-avalon-game]')).toHaveCount(8)
    const witcher = library.locator('[data-avalon-game="3"]')
    await expect(witcher.locator('.avalon-store-initials')).toHaveAttribute('title', /Steam.*GOG|GOG.*Steam/)
    const stardew = library.locator('[data-avalon-game="4"]')
    await expect(stardew.locator('.avalon-unread')).toBeVisible()
    await expect(library.locator('[data-avalon-game="1"]')).toHaveAccessibleName(/Hollow Knight/)
    await capture(`${mode}-source-library-eight`)
    if (mode === 'desktop') {
      await stardew.focus()
      await stardew.locator('..').getByRole('button', { name: 'Details', exact: true }).click()
    } else await stardew.click()
    await populatedDetails(mode, `${mode}-source-opened-details`)
    await isolation()
  })

  test(`${mode} all fifteen isolated source preview surfaces attach with production content and standalone Stardew details at original geometry`, async () => {
    await start(mode, 'GameDetailsView', 1280, 820)
    await validateFixture()
    const attached: unknown[] = []
    for (const surface of surfaces) {
      await page!.evaluate(
        (surface) =>
          (window as unknown as { designPreviewNative: NativePreview }).designPreviewNative.show(surface),
        surface,
      )
      await expect(page!.locator('#root')).toHaveAttribute('data-preview-surface', surface)
      // Portalled Details and the fixed filter panel own their painted bounds.
      const host = page!.locator(
        surface === 'GameDetailsView'
          ? '.avalon-details'
          : surface === 'FilterPanelView'
            ? '.avalon-filter-panel'
            : '[data-design-surface]',
      )
      await expect(host).toBeVisible()
      await frames()
      await expect
        .poll(() => host.locator('*').count(), {
          message: `${surface} must compose actual production descendants`,
        })
        .toBeGreaterThan(0)
      if (surface !== 'RowCoverView')
        await expect
          .poll(() => host.innerText(), { message: `${surface} must render production text` })
          .toMatch(/\S/)
      const geometry = await host.evaluate((node) => ({
        surface: node.getAttribute('data-design-surface'),
        bounds: node.getBoundingClientRect().toJSON(),
        descendants: node.querySelectorAll('*').length,
        text: node.textContent,
      }))
      expect(geometry.bounds.width).toBeGreaterThan(0)
      expect(geometry.bounds.height).toBeGreaterThan(0)
      attached.push({ ...geometry, surface })
      if (surface === 'FeedView' && mode === 'fullscreen') {
        const active = host.locator('.avalon-retained-row[data-row-active="true"]')
        await expect(active.locator('.avalon-cover')).toHaveCount(2)
        const cards = []
        for (const id of [4, 1]) {
          const card = active.locator(`[data-avalon-game="${id}"]`)
          await expect(card).toBeVisible()
          await expect(card).toBeInViewport({ ratio: 0.99 })
          const painted = await card.evaluate((node) => {
            const bounds = node.getBoundingClientRect()
            const point = document.elementFromPoint(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2)
            return {
              workId: node.getAttribute('data-avalon-game'),
              bounds: bounds.toJSON(),
              midpointHitsCard: point === node || (point !== null && node.contains(point)),
            }
          })
          expect(painted.bounds.width).toBeGreaterThanOrEqual(80)
          expect(painted.bounds.height).toBeGreaterThanOrEqual(120)
          expect(painted.bounds.width / painted.bounds.height).toBeCloseTo(2 / 3, 1)
          expect(painted.midpointHitsCard).toBe(true)
          cards.push(painted)
        }
        measurements.standaloneFeedCards = cards
      }
      await capture(`${mode}-surface-${surface}`)
      if (surface === 'GameDetailsView') await populatedDetails(mode, `${mode}-standalone-stardew-notes`)
      await isolation()
    }
    measurements.attached = attached
    expect(attached).toHaveLength(15)
  })
}
