import { prebuiltFixture as fixture, prebuiltActivationHelper } from './prebuilt-backend'
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
import { libraryAction } from './library-controls'
import { profileDirectory } from '../../src/main/storage'
import { DEFAULT_PROFILE } from '../../src/shared/theme'
import type { Mode } from '../../src/renderer/api/types'
import type { ApiRequest } from '../../src/shared/bridge'

const probeDirectory = resolve('../..', '.tmp/task38121-native-probe')
let app: ElectronApplication, page: Page, shellPage: Page, directory: string, mode: Mode
let endpoint: { address: string; token: string }
const errors: string[] = []
type Call = {
  callId: number
  operation: string
  gateId?: string | null
  workId?: number
  provider?: string
  id?: string
  width?: number
  responseReady: boolean
  completed: boolean
  canceled: boolean
  delivered: boolean
  json?: string | null
}
type FixtureState = {
  kind: string
  calls: Call[]
  storedMode?: string | null
  modeWrites?: string[]
  floorRgba?: number[] | string
  floor?: { brightnessFloor: number; saturationFloor: number; hueDegrees: number }
  pluginKey?: { provider: string; id: string }
}
const state = () => control<FixtureState>('state')
const cover = (id = 1) => page.locator(`.avalon-library [data-avalon-game="${id}"]`)
const details = () => page.locator('.avalon-details')
const queue = () => page.locator('.merge-queue')
const coverIdentity = (key: { provider: string; id: string } | null | undefined) =>
  key ? { provider: key.provider, id: key.id } : null
async function control<T = unknown>(route: string, body?: unknown): Promise<T> {
  const response = await fetch(new URL(`/__fixture/cover-behavior/${route}`, endpoint.address), {
    method: body === undefined ? 'GET' : 'POST',
    headers: {
      Authorization: `Bearer ${endpoint.token}`,
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!response.ok) throw Error(`Cover fixture ${route}: ${response.status} ${await response.text()}`)
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}
async function api<T = unknown>(input: ApiRequest): Promise<T> {
  const result = await page.evaluate((input) => window.winnow.request(input), input)
  if (!result.ok) throw Error(`${input.route}: ${result.status} ${result.message}`)
  return result.data as T
}
async function pad() {
  await page.evaluate(() => {
    const value = { pressed: [] as number[] }
    Object.assign(window, { coverPad: value })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          id: 'Native cover fixture controller',
          connected: true,
          mapping: 'standard',
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, index) => ({
            pressed: value.pressed.includes(index),
            touched: value.pressed.includes(index),
            value: value.pressed.includes(index) ? 1 : 0,
          })),
        },
      ],
    })
  })
}
async function tap(button: number) {
  for (const pressed of [[], [button], []])
    await page.evaluate(async (pressed) => {
      ;(window as unknown as { coverPad: { pressed: number[] } }).coverPad.pressed = pressed
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
}
async function activate(target: Locator) {
  await target.focus()
  await expect(target).toBeFocused()
  if (mode === 'fullscreen') await tap(0)
  else await page.keyboard.press('Enter')
}
async function navigate(name: 'For you' | 'Library' | 'Settings') {
  await activate(
    page
      .getByRole('navigation', { name: 'Main navigation', exact: true })
      .getByRole('button', { name, exact: true }),
  )
}
async function surface(
  width = mode === 'fullscreen' ? 1920 : 1600,
  height = mode === 'fullscreen' ? 1080 : 1000,
) {
  await app.evaluate(
    ({ BrowserWindow }, { width, height, mode }) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setFullScreen(false)
      window.setMinimumSize(0, 0)
      window.setContentSize(width, height)
      window.isFullScreen = () => mode === 'fullscreen'
      window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
      window.focus()
    },
    { width, height, mode },
  )
  await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
  await pad()
}
async function seed(kind: string, extra: Record<string, unknown> = {}) {
  await navigate('Library')
  const result = await control<FixtureState>('seed', { kind, ...extra })
  await expect(cover()).toBeVisible()
  await page.mouse.move(10, 10)
  return {
    ...result,
    // System.Text.Json transports byte[] as base64; preserve those exact reference bytes.
    floorRgba:
      typeof result.floorRgba === 'string' ? [...Buffer.from(result.floorRgba, 'base64')] : result.floorRgba,
  }
}
async function arm(id: string, provider: string, extra: Record<string, unknown> = {}) {
  return control<{ gateId: string }>('arm', {
    operation: 'image',
    provider,
    id,
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
async function release(gateId: string) {
  await control('release', { gateId })
  await expect
    .poll(async () => (await state()).calls.some((call) => call.gateId === gateId && call.completed))
    .toBe(true)
}
async function openQueue() {
  await navigate('Library')
  await activate(await libraryAction(page, 'Manage library'))
  await activate(page.getByRole('button', { name: 'Identity review', exact: true }))
  await expect(queue()).toBeVisible()
}
async function openMembers() {
  await activate(queue().getByRole('article').first().getByRole('button').first())
  await expect(page.getByRole('dialog').locator('[data-merge-member]')).toHaveCount(2)
}
async function openMergeDetails() {
  await openMembers()
  await activate(page.getByRole('dialog').locator('[data-merge-member="1"]'))
  await activate(page.getByRole('dialog').getByRole('button', { name: 'Open game', exact: true }))
  await expect(details()).toBeVisible()
}
async function openDetails() {
  await activate(cover())
  await expect(details()).toBeVisible()
}
async function closeDetails() {
  await page.keyboard.press('Escape')
  await expect(details()).toHaveCount(0)
}
async function capture(name: string) {
  await page.screenshot({ path: test.info().outputPath(`${mode}-${name}.png`) })
}
async function retainImages(target: Locator) {
  return await target.evaluateAll((nodes) => {
    const key = crypto.randomUUID()
    const host = window as unknown as { retainedCoverImages: Record<string, HTMLImageElement[]> }
    ;(host.retainedCoverImages ??= {})[key] = nodes as HTMLImageElement[]
    return key
  })
}
async function imagesReleased(key: string) {
  await expect
    .poll(() =>
      page.evaluate(
        (key) =>
          (
            window as unknown as { retainedCoverImages: Record<string, HTMLImageElement[]> }
          ).retainedCoverImages[key].map((image) => ({
            connected: image.isConnected,
            source: image.getAttribute('src'),
          })),
        key,
      ),
    )
    .toEqual(
      await page.evaluate(
        (key) =>
          (
            window as unknown as { retainedCoverImages: Record<string, HTMLImageElement[]> }
          ).retainedCoverImages[key].map(() => ({ connected: false, source: null })),
        key,
      ),
    )
}
test.beforeAll(async () => {
  await readFile(fixture)
  await mkdir(probeDirectory, { recursive: true })
  await build({
    entryPoints: [resolve('tests/electron/cover-contracts-probe.tsx')],
    outfile: join(probeDirectory, 'probe.js'),
    bundle: true,
    platform: 'browser',
    format: 'iife',
    jsx: 'automatic',
    define: { 'process.env.NODE_ENV': '"production"' },
    loader: { '.ttf': 'file', '.woff': 'file', '.woff2': 'file', '.svg': 'dataurl' },
    plugins: [
      {
        name: 'theme-source-text',
        setup(bundle) {
          bundle.onResolve({ filter: /\?raw$/ }, (args) => ({
            path: resolve(args.resolveDir, args.path.slice(0, -4)),
            namespace: 'theme-source-text',
          }))
          bundle.onLoad({ filter: /.*/, namespace: 'theme-source-text' }, async (args) => ({
            contents: await readFile(args.path, 'utf8'),
            loader: 'text',
          }))
        },
      },
    ],
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
  errors.length = 0
  endpoint = undefined as unknown as typeof endpoint
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-cover-behavior-'))
  const profile = structuredClone(DEFAULT_PROFILE)
  profile.appearance.scale = 100
  profile.appearance.reducedMotion = true
  const preferences = profileDirectory(join(directory, 'electron-userdata'), directory)
  await mkdir(preferences, { recursive: true })
  await writeFile(join(preferences, 'preferences.json'), JSON.stringify(profile))
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/cover-contracts-main.mjs'),
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
      WINNOW_BACKEND_PATH: fixture,
      WINNOW_ACTIVATION_HELPER_PATH: prebuiltActivationHelper,
    },
    chromiumSandbox: true,
    timeout: 60000,
  })
  page = shellPage = await app.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible({ timeout: 45000 })
  endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
  expect((await fetch(new URL('/__fixture/cover-behavior/state', endpoint.address))).status).toBe(401)
  await surface()
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
})
test.afterEach(async ({}, info) => {
  try {
    if (endpoint)
      await info.attach('cover-fixture-ledger', {
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
      await info.attach('cover-before-teardown', { body: await page.screenshot(), contentType: 'image/png' })
      await info.attach('cover-before-teardown-dom', {
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

async function probe(kind: 'library' | 'feed') {
  const opened = app.waitForEvent('window')
  await app.evaluate(
    async ({ BrowserWindow }, { path, kind, mode }) => {
      BrowserWindow.getAllWindows()[0]!.hide()
      const window = new BrowserWindow({
        width: 800,
        height: 650,
        useContentSize: true,
        webPreferences: { sandbox: true, contextIsolation: true, nodeIntegration: false },
      })
      await window.loadFile(path, { query: { kind, mode } })
      window.focus()
    },
    { path: join(probeDirectory, 'index.html'), kind, mode },
  )
  page = await opened
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.locator('.avalon-cover img')).toBeVisible()
  await page.evaluate(() => document.fonts.ready)
  await pad()
  await page.mouse.move(790, 640)
}
async function paintedPixel(target: Locator, name: string, point = { x: 0.25, y: 0.2 }) {
  const bounds = (await target.boundingBox())!
  const png = await page.screenshot({ path: test.info().outputPath(`${mode}-${name}.png`) })
  const result = await app.evaluate(
    ({ nativeImage }, { bytes, x, y }) => {
      const image = nativeImage.createFromBuffer(Buffer.from(bytes))
      const size = image.getSize()
      const data = image.toBitmap()
      const index = (Math.round(y) * size.width + Math.round(x)) * 4
      return [data[index + 2], data[index + 1], data[index], data[index + 3]]
    },
    { bytes: [...png], x: bounds.x + bounds.width * point.x, y: bounds.y + bounds.height * point.y },
  )
  await test.info().attach(`painted-pixel-${name}`, {
    body: JSON.stringify({ bounds, point, rgba: result }),
    contentType: 'application/json',
  })
  return result
}
function nearPixel(actual: number[], expected: number[], tolerance = 2) {
  expect(actual).toHaveLength(4)
  actual.forEach((value, index) => expect(Math.abs(value - expected[index])).toBeLessThanOrEqual(tolerance))
}
async function decodedPixel(target: Locator, point: { x: number; y: number }) {
  return target.evaluate((node, point) => {
    const image = node as HTMLImageElement
    const canvas = document.createElement('canvas')
    canvas.width = image.naturalWidth
    canvas.height = image.naturalHeight
    const context = canvas.getContext('2d')!
    context.drawImage(image, 0, 0)
    return [
      ...context.getImageData(Math.floor(canvas.width * point.x), Math.floor(canvas.height * point.y), 1, 1)
        .data,
    ]
  }, point)
}
async function paddingPixel(target: Locator, point: { x: number; y: number }) {
  return target.evaluate((node, point) => {
    const canvas = node as HTMLCanvasElement
    return [
      ...canvas
        .getContext('2d')!
        .getImageData(Math.floor(canvas.width * point.x), Math.floor(canvas.height * point.y), 1, 1).data,
    ]
  }, point)
}

for (const surfaceName of ['desktop', 'fullscreen']) {
  for (const art of ['plugin', 'unavailable', 'user', 'igdb', 'steam'])
    test(`${surfaceName} source ${art} selection is identical in the production library and merge composition`, async () => {
      const seeded = await seed('selection', { art })
      const selected = await api<{ current: { previewKey: { provider: string; id: string } } | null }>({
        route: 'artworkState',
        params: { workId: 1, slot: 'Cover' },
      })
      const expected =
        art === 'plugin'
          ? coverIdentity(seeded.pluginKey)
          : art === 'unavailable'
            ? null
            : { provider: art, id: art === 'user' ? 'fixtureuser' : art === 'igdb' ? 'co42' : '42' }
      expect(coverIdentity(selected.current?.previewKey)).toEqual(expected)
      if (expected) {
        await expect(cover().locator('img')).toBeVisible()
        await expect
          .poll(async () =>
            (await state()).calls.some(
              (call) =>
                call.operation === 'image' &&
                call.provider === expected.provider &&
                call.id === expected.id &&
                call.delivered,
            ),
          )
          .toBe(true)
      } else {
        await expect(cover().locator('img')).toHaveCount(0)
        expect((await state()).calls.filter((call) => call.operation === 'image')).toEqual([])
      }
      await expect(cover()).toHaveAccessibleName(/Game/)
      await capture(`source-selection-${art}-library`)
      await openQueue()
      const review = await api<{
        candidates: { leftReleaseId: number; rightReleaseId: number; score: number; status: string }[]
        workspace: {
          works: { id: number; name: string; coverUrl: string | null }[]
          releases: { id: number; workId: number }[]
        }
      }>({ route: 'identity.get' })
      expect(review.candidates).toMatchObject([
        { leftReleaseId: 1, rightReleaseId: 2, score: 0.95, status: 'pending' },
      ])
      expect(review.workspace.releases.map(({ id, workId }) => ({ id, workId }))).toEqual([
        { id: 1, workId: 1 },
        { id: 2, workId: 2 },
      ])
      const sourceReference =
        art === 'user'
          ? 'winnow://user-art/fixtureuser'
          : art === 'igdb' || art === 'steam'
            ? 'https://images.igdb.com/igdb/image/upload/t_cover_big/co42.jpg'
            : `winnow://plugin-art/fixture/${seeded.pluginKey!.id}`
      expect(review.workspace.works.find((work) => work.id === 1)).toMatchObject({
        name: 'Game',
        coverUrl: sourceReference,
      })
      expect(review.workspace.works.find((work) => work.id === 2)).toMatchObject({
        name: 'Game edition',
        coverUrl: null,
      })
      if (mode === 'fullscreen') {
        await expect(queue().locator('.artwork')).toHaveCount(0)
        await openMembers()
        await expect(page.getByRole('dialog').locator('[data-merge-member="1"]')).toHaveAccessibleName(
          /^Game/,
        )
        await expect(page.getByRole('dialog').locator('[data-merge-member="2"]')).toHaveAccessibleName(
          /^Game edition/,
        )
        const same = await api<{ current: { previewKey: { provider: string; id: string } } | null }>({
          route: 'artworkState',
          params: { workId: 1, slot: 'Cover' },
        })
        expect(coverIdentity(same.current?.previewKey)).toEqual(expected)
        await capture(`source-selection-${art}-merge-members`)
        return
      }
      const merge = queue().locator('.merge-cover[data-work-id="1"]')
      await expect(merge).toBeVisible()
      if (expected) {
        await expect(merge.locator('img')).toBeVisible()
        const color = await merge.locator('img').evaluate((node) => {
          const canvas = document.createElement('canvas')
          canvas.width = canvas.height = 1
          const context = canvas.getContext('2d')!
          context.drawImage(node as HTMLImageElement, 0, 0, 1, 1)
          return [...context.getImageData(0, 0, 1, 1).data]
        })
        expect(color).toEqual([106, 90, 205, 255])
      } else {
        await expect(merge.locator('img')).toHaveCount(0)
        expect((await state()).calls.filter((call) => call.operation === 'image')).toEqual([])
      }
      const same = await api<{ current: { previewKey: { provider: string; id: string } } | null }>({
        route: 'artworkState',
        params: { workId: 1, slot: 'Cover' },
      })
      expect(coverIdentity(same.current?.previewKey)).toEqual(expected)
      await expect(queue().locator('.merge-cover[data-work-id="2"]')).toBeVisible()
      await merge.scrollIntoViewIfNeeded()
      await capture(`source-selection-${art}-merge`)
    })
}

for (const kind of ['library', 'feed'] as const)
  for (const method of ['Tab', 'Directional'])
    test(`desktop source ${kind} ${method} focus restores vivid pixels and pointer exit preserves only keyboard focus`, async () => {
      const source = await seed('focus')
      expect(source.floorRgba).toHaveLength(4)
      await probe(kind)
      const target = page.locator('.avalon-cover')
      const image = target.locator('img')
      nearPixel(await paintedPixel(image, `${kind}-${method}-dormant`), source.floorRgba!)
      if (method === 'Tab') await page.keyboard.press('Tab')
      else {
        await page.locator('#elsewhere').focus()
        await tap(14)
      }
      await expect(target).toBeFocused()
      nearPixel(await paintedPixel(image, `${kind}-${method}-cover-focused`), [106, 90, 205, 255])
      const keyboardTarget = kind === 'library' ? page.locator('.avalon-tile-details') : target
      if (kind === 'library') {
        if (method === 'Tab') await page.keyboard.press('Tab')
        else await tap(15)
      }
      await expect(keyboardTarget).toBeFocused()
      nearPixel(await paintedPixel(image, `${kind}-${method}-focused`), [106, 90, 205, 255])
      await page.mouse.move(50, 50)
      await page.mouse.move(790, 640)
      await expect(keyboardTarget).toBeFocused()
      nearPixel(await paintedPixel(image, `${kind}-${method}-keyboard-pointer-exit`), [106, 90, 205, 255])
      await page.locator('#elsewhere').focus()
      nearPixel(await paintedPixel(image, `${kind}-${method}-blurred`), source.floorRgba!)
      await page.mouse.move(50, 50)
      await keyboardTarget.click()
      await page.mouse.move(50, 50)
      await expect(keyboardTarget).toBeFocused()
      nearPixel(await paintedPixel(image, `${kind}-${method}-pointer-focus`), [106, 90, 205, 255])
      await page.mouse.move(790, 640)
      await expect(keyboardTarget).toBeFocused()
      nearPixel(await paintedPixel(image, `${kind}-${method}-pointer-exit`), source.floorRgba!)
    })
test('desktop source library selection changes rendered dormancy without moving keyboard focus', async () => {
  const source = await seed('focus')
  await probe('library')
  const elsewhere = page.locator('#elsewhere'),
    image = page.locator('.avalon-cover img')
  await elsewhere.focus()
  nearPixel(await paintedPixel(image, 'selection-rest'), source.floorRgba!)
  await page.evaluate(() =>
    (window as unknown as { coverContractProbe: { select(value: boolean): void } }).coverContractProbe.select(
      true,
    ),
  )
  await expect(page.locator('.avalon-cover')).toHaveAttribute('data-selected', 'true')
  await expect(elsewhere).toBeFocused()
  nearPixel(await paintedPixel(image, 'selection-vivid'), [106, 90, 205, 255])
  await page.evaluate(() =>
    (window as unknown as { coverContractProbe: { select(value: boolean): void } }).coverContractProbe.select(
      false,
    ),
  )
  await expect(elsewhere).toBeFocused()
  nearPixel(await paintedPixel(image, 'selection-cleared'), source.floorRgba!)
})
test('fullscreen selection and controller focus restore vivid art with the shared dormant pixel endpoint', async () => {
  const source = await seed('focus')
  expect(source.floor).toEqual({ brightnessFloor: 0.68, saturationFloor: 0.22, hueDegrees: -6 })
  await probe('library')
  const target = page.locator('.avalon-cover'),
    image = target.locator('img')
  await page.locator('#elsewhere').focus()
  await expect(target.locator('.artwork')).toHaveCSS(
    'filter',
    `saturate(${source.floor!.saturationFloor}) hue-rotate(${source.floor!.hueDegrees}deg) brightness(${source.floor!.brightnessFloor})`,
  )
  nearPixel(await paintedPixel(image, 'fullscreen-rest'), source.floorRgba!)
  await tap(14)
  await expect(target).toBeFocused()
  nearPixel(await paintedPixel(image, 'fullscreen-controller-focus'), [106, 90, 205, 255])
  await page.locator('#elsewhere').focus()
  await page.evaluate(() =>
    (window as unknown as { coverContractProbe: { select(value: boolean): void } }).coverContractProbe.select(
      true,
    ),
  )
  await expect(page.locator('#elsewhere')).toBeFocused()
  nearPixel(await paintedPixel(image, 'fullscreen-selection'), [106, 90, 205, 255])
  await page.evaluate(() =>
    (window as unknown as { coverContractProbe: { select(value: boolean): void } }).coverContractProbe.select(
      false,
    ),
  )
  nearPixel(await paintedPixel(image, 'fullscreen-selection-cleared'), source.floorRgba!)
})

for (const surfaceName of ['desktop', 'fullscreen']) {
  test(`${surfaceName} closing Details cancels its pending ${surfaceName === 'desktop' ? 'portrait cover' : 'cinematic backdrop'} and releases its drawn image`, async () => {
    if (mode === 'desktop') {
      await navigate('Library')
      // Keep the wall's240px rendition distinct from the modal's160px rendition,
      // so closing the modal really removes the last consumer of its pending read.
      await page.getByRole('slider', { name: 'Density', exact: true }).fill('108')
    }
    await seed('lifetime')
    const held = await arm(
      '620',
      mode === 'desktop' ? 'steam' : 'steam-hero',
      mode === 'desktop' ? { width: 160 } : {},
    )
    await openDetails()
    await entered(held.gateId)
    const image = details().locator(
      mode === 'desktop' ? '.avalon-detail-cover img' : '.avalon-detail-backdrop .avalon-backdrop-art img',
    )
    await expect(image).toHaveCount(0)
    await closeDetails()
    await canceled(held.gateId)
    await release(held.gateId)
    expect((await state()).calls.find((call) => call.gateId === held.gateId)?.delivered).toBe(false)
    await openDetails()
    await expect(image).toBeVisible()
    const key = await retainImages(image)
    await capture('details-live-cover')
    await closeDetails()
    await imagesReleased(key)
  })
  test(`${surfaceName} closing Details releases every mounted screenshot consumer and ignores held late thumbnails`, async () => {
    await seed('lifetime', { shotCount: 3 })
    const ids = mode === 'desktop' ? ['aa11', 'bb22', 'cc33'] : ['aa11', 'bb22']
    const gates = []
    for (const id of ids) gates.push(await arm(id, 'igdb-shot', { width: mode === 'desktop' ? 400 : 1280 }))
    await openDetails()
    for (const gate of gates) await entered(gate.gateId)
    await expect(details().locator('.screenshot-strip img')).toHaveCount(0)
    await closeDetails()
    for (const gate of gates) {
      await canceled(gate.gateId)
      await release(gate.gateId)
    }
    await openDetails()
    const images = details().locator('.screenshot-strip img')
    await expect(images).toHaveCount(ids.length)
    for (const image of await images.all()) await expect(image).toBeVisible()
    const key = await retainImages(images)
    if (mode === 'fullscreen') {
      await activate(details().getByRole('button', { name: 'Open screenshot 2 of 3', exact: true }))
      const lightbox = page.getByRole('dialog', { name: 'Screenshot 2 of 3', exact: true })
      await activate(lightbox.getByRole('button', { name: 'Next screenshot', exact: true }))
      await expect(
        page.getByRole('dialog', { name: 'Screenshot 3 of 3', exact: true }).locator('img'),
      ).toBeVisible()
      await page.keyboard.press('Escape')
    }
    await capture('all-mounted-screenshots')
    await closeDetails()
    await imagesReleased(key)
  })
  test(`${surfaceName} lightbox keeps one active shot while navigating and clears its image on close`, async () => {
    await seed('lifetime', { shotCount: 2 })
    await openDetails()
    await expect(details().locator('.screenshot-strip img')).toHaveCount(2)
    await activate(details().getByRole('button', { name: 'Open screenshot 1 of 2', exact: true }))
    const lightbox = page.locator('.screenshot-dialog')
    await expect(lightbox.locator('img')).toHaveCount(1)
    await expect(lightbox.locator('img')).toBeVisible()
    const first = await retainImages(lightbox.locator('img'))
    await activate(lightbox.getByRole('button', { name: 'Next screenshot', exact: true }))
    await expect(lightbox).toHaveAccessibleName('Screenshot 2 of 2')
    await expect(lightbox.locator('img')).toHaveCount(1)
    await expect(lightbox.locator('img')).toBeVisible()
    await imagesReleased(first)
    const last = await retainImages(lightbox.locator('img'))
    const thumbs = await retainImages(details().locator('.screenshot-strip img'))
    await capture('one-active-lightbox-shot')
    await page.keyboard.press('Escape')
    await expect(lightbox).toHaveCount(0)
    await imagesReleased(last)
    expect(
      await page.evaluate(
        (key) =>
          (
            window as unknown as { retainedCoverImages: Record<string, HTMLImageElement[]> }
          ).retainedCoverImages[key].every((image) => image.isConnected && !!image.getAttribute('src')),
        thumbs,
      ),
    ).toBe(true)
    await closeDetails()
    await imagesReleased(thumbs)
  })
  test(
    surfaceName === 'desktop'
      ? 'desktop merge rows draw the shared dormant pixels and release every image when leaving review'
      : 'fullscreen merge members acquire artwork only through Details and release its backdrop on close',
    async () => {
      const source = await seed('lifetime')
      await openQueue()
      if (mode === 'fullscreen') {
        await expect(queue().locator('.artwork')).toHaveCount(0)
        const held = await arm('620', 'steam-hero')
        await openMergeDetails()
        await entered(held.gateId)
        await closeDetails()
        await canceled(held.gateId)
        await release(held.gateId)
        expect((await state()).calls.find((call) => call.gateId === held.gateId)?.delivered).toBe(false)
        await openMergeDetails()
        const backdrop = details().locator('.avalon-detail-backdrop .avalon-backdrop-art img')
        await expect(backdrop).toBeVisible()
        const key = await retainImages(backdrop)
        await capture('merge-member-details-backdrop')
        await closeDetails()
        await imagesReleased(key)
        await expect(queue().locator('.artwork')).toHaveCount(0)
        return
      }
      const images = queue().locator('.merge-cover img')
      await expect(images).toHaveCount(2)
      for (const image of await images.all()) await expect(image).toBeVisible()
      await page.mouse.move(10, 10)
      await page
        .getByRole('navigation', { name: 'Main navigation', exact: true })
        .getByRole('button', { name: 'Library', exact: true })
        .focus()
      const key = await retainImages(images)
      const image = queue().locator('.merge-cover[data-work-id="1"] img')
      // Sample beyond the decorative upper-left gloss, where the artwork is unobscured.
      nearPixel(await paintedPixel(image, 'merge-resting-cover', { x: 0.7, y: 0.7 }), source.floorRgba!)
      await activate(page.getByRole('button', { name: 'Close tools', exact: true }))
      await expect(queue()).toHaveCount(0)
      await imagesReleased(key)
      await expect(cover().locator('img')).toBeVisible()
    },
  )
}

async function preference(value: 'fit' | 'fill') {
  await api({
    route: 'preferences.presentation.put',
    params: { preference: 'CoverArtMode' },
    body: { value },
  })
  await expect
    .poll(() =>
      page.evaluate(() =>
        getComputedStyle(document.documentElement).getPropertyValue('--cover-art-fit').trim(),
      ),
    )
    .toBe(value === 'fit' ? 'contain' : 'cover')
}
for (const surfaceName of ['desktop', 'fullscreen'])
  test(`${surfaceName} shared Fit and Fill update live and new portraits preserve bounds and backgrounds and persist`, async () => {
    await seed('fit')
    expect((await state()).modeWrites).toEqual([])
    await expect(cover().locator('img')).toBeVisible()
    await cover().focus()
    const image = cover().locator('img')
    await expect(image).toHaveCSS('object-fit', 'contain')
    const firstNode = await image.elementHandle()
    const bounds = await image.boundingBox()
    const sourceRed = await decodedPixel(image, { x: 0.1, y: 0.5 })
    expect(sourceRed[0]).toBeGreaterThan(245)
    expect(sourceRed[1]).toBeLessThan(10)
    expect(sourceRed[2]).toBeLessThan(10)
    const sourceCenter = await decodedPixel(image, { x: 0.5, y: 0.5 })
    nearPixel(sourceCenter, [106, 90, 205, 255])
    const visiblePoint = { x: 0.1, y: 0.35 }
    const captionBounds = await cover().locator('.avalon-cover-caption').boundingBox()
    if (captionBounds) expect(bounds!.y + bounds!.height * visiblePoint.y).toBeLessThan(captionBounds.y)
    await test.info().attach('decoded-fit-source-regions', {
      body: JSON.stringify({ red: sourceRed, center: sourceCenter, visiblePoint, captionBounds }),
      contentType: 'application/json',
    })
    nearPixel(await paintedPixel(image, 'fit-entire-red-edge', visiblePoint), sourceRed)
    const paddingPoint = { x: 0.5, y: 0.1 }
    const edgeColor = [117, 77, 103, 255]
    const wallPadding = cover().locator('.cover-padding')
    await expect(wallPadding).toBeVisible()
    await expect.poll(() => paddingPixel(wallPadding, paddingPoint)).toEqual(edgeColor)
    nearPixel(await paintedPixel(image, 'fit-library-sampled-padding', paddingPoint), edgeColor)
    if (mode === 'desktop') {
      await activate(page.getByRole('button', { name: 'Display preferences', exact: true }))
      await page.getByRole('combobox', { name: 'Cover art', exact: true }).selectOption('fill')
      await page.keyboard.press('Escape')
    } else await preference('fill')
    await cover().focus()
    await expect(image).toHaveCSS('object-fit', 'cover')
    expect(await firstNode!.evaluate((node) => node.isConnected)).toBe(true)
    expect(await image.boundingBox()).toEqual(bounds)
    nearPixel(await paintedPixel(image, 'fill-cropped-to-center', visiblePoint), sourceCenter)
    await expect.poll(() => paddingPixel(wallPadding, paddingPoint)).toEqual([0, 0, 0, 0])
    nearPixel(await paintedPixel(image, 'fill-library-cleared-padding', paddingPoint), sourceCenter)
    await expect.poll(async () => (await state()).storedMode).toBe('fill')
    await navigate('For you')
    const fresh = page
      .locator(
        mode === 'desktop'
          ? '.avalon-feed-card .avalon-cover'
          : '.avalon-retained-row[data-row-active="true"] .avalon-cover',
      )
      .first()
    await expect(fresh.locator('img')).toBeVisible()
    await expect(fresh.locator('img')).toHaveCSS('object-fit', 'cover')
    let background: Locator
    if (mode === 'desktop') {
      await fresh.hover({ position: { x: 40, y: 20 } })
      background = page.locator('.avalon-hover-preview .artwork img')
    } else background = page.locator('.avalon-home-backdrop .avalon-backdrop-art img')
    await expect(background).toBeVisible()
    await expect(background).toHaveCSS('object-fit', 'cover')
    await capture('fill-new-portrait-and-independent-background')
    const feedNode = await fresh.locator('img').elementHandle()
    const feedBounds = await fresh.locator('img').boundingBox()
    await preference('fit')
    await expect(fresh.locator('img')).toHaveCSS('object-fit', 'contain')
    if (mode === 'desktop' && !(await background.count())) await fresh.hover({ position: { x: 41, y: 21 } })
    await expect(background).toHaveCSS('object-fit', 'cover')
    expect(await feedNode!.evaluate((node) => node.isConnected)).toBe(true)
    expect(await fresh.locator('img').boundingBox()).toEqual(feedBounds)
    const feedPadding = fresh.locator('.cover-padding')
    await expect.poll(() => paddingPixel(feedPadding, paddingPoint)).toEqual(edgeColor)
    await fresh.focus()
    nearPixel(await paintedPixel(fresh.locator('img'), 'fit-feed-sampled-padding', paddingPoint), edgeColor)
    await preference('fill')
    await expect(fresh.locator('img')).toHaveCSS('object-fit', 'cover')
    expect(await fresh.locator('img').boundingBox()).toEqual(feedBounds)
    await expect.poll(() => paddingPixel(feedPadding, paddingPoint)).toEqual([0, 0, 0, 0])
    await expect(background).toHaveCSS('object-fit', 'cover')
    await preference('fit')
    await expect(fresh.locator('img')).toHaveCSS('object-fit', 'contain')
    expect(await fresh.locator('img').boundingBox()).toEqual(feedBounds)
    await openQueue()
    if (mode === 'desktop') {
      const rowImage = queue().locator('.merge-cover[data-work-id="1"] img')
      await expect(rowImage).toBeVisible()
      await rowImage.scrollIntoViewIfNeeded()
      await expect(rowImage).toHaveCSS('object-fit', 'contain')
      const rowNode = await rowImage.elementHandle()
      const rowBounds = await rowImage.boundingBox()
      const rowPadding = queue().locator('.merge-cover[data-work-id="1"] .cover-padding')
      const rowPaddingPoint = { x: 0.7, y: 0.9 }
      await page.mouse.move(10, 10)
      await expect.poll(() => paddingPixel(rowPadding, rowPaddingPoint)).toEqual(edgeColor)
      // Shared CoverImaging.FloorMatrix(.22,.68,-6) maps the averaged RGB(117,77,103) to RGB(64,58,62).
      nearPixel(
        await paintedPixel(rowImage, 'fit-merge-sampled-dormant-padding', rowPaddingPoint),
        [64, 58, 62, 255],
      )
      await preference('fill')
      await expect(rowImage).toHaveCSS('object-fit', 'cover')
      expect(await rowNode!.evaluate((node) => node.isConnected)).toBe(true)
      expect(await rowImage.boundingBox()).toEqual(rowBounds)
      await expect.poll(() => paddingPixel(rowPadding, rowPaddingPoint)).toEqual([0, 0, 0, 0])
      await capture('fill-existing-merge-row')
      await preference('fit')
      await expect(rowImage).toHaveCSS('object-fit', 'contain')
      expect(await rowImage.boundingBox()).toEqual(rowBounds)
    } else {
      await expect(queue().locator('.artwork')).toHaveCount(0)
      await openMembers()
      await capture('text-only-merge-members')
      await page.keyboard.press('Escape')
      await expect(page.getByRole('dialog')).toHaveCount(0)
      await expect(queue().getByRole('article').first().getByRole('button').first()).toBeFocused()
    }
    if (mode === 'fullscreen') {
      await navigate('Settings')
      await activate(
        page
          .getByRole('navigation', { name: 'Settings section', exact: true })
          .getByRole('button', { name: 'Appearance', exact: true }),
      )
      const setting = page.getByRole('button', { name: 'Cover art', exact: true })
      const selectedValue = setting.locator('.fullscreen-setting-value [id]')
      await expect(selectedValue).toHaveText('Fit')
      await expect(setting).toBeEnabled()
      await setting.focus()
      await tap(15)
      await expect(selectedValue).toHaveText('Fill')
      await expect.poll(async () => (await state()).storedMode).toBe('fill')
      await expect(setting).toBeEnabled()
      await tap(14)
      await expect(selectedValue).toHaveText('Fit')
      await expect.poll(async () => (await state()).storedMode).toBe('fit')
      await expect(setting).toBeEnabled()
    }
    await navigate('Library')
    const closeTools = page.getByRole('button', { name: 'Close tools', exact: true })
    if (await closeTools.count()) await activate(closeTools)
    await expect(cover().locator('img')).toHaveCSS('object-fit', 'contain')
    await expect.poll(async () => (await state()).storedMode).toBe('fit')
    const saved = (await state()).modeWrites
    await page.reload()
    await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
    await surface()
    await navigate('Library')
    await expect(cover().locator('img')).toHaveCSS('object-fit', 'contain')
    expect((await state()).modeWrites).toEqual(saved)
    await capture('persisted-fit-after-reload')
  })
