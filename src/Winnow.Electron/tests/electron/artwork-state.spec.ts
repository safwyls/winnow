import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Page,
  type Locator,
} from '@playwright/test'
import { mkdtemp, mkdir, readFile, writeFile } from 'node:fs/promises'
import { resolve, join } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import { profileDirectory } from '../../src/main/storage'
import { DEFAULT_PROFILE } from '../../src/shared/theme'
import type { ApiRequest } from '../../src/shared/bridge'
import type { ArtworkState, Mode } from '../../src/renderer/api/types'

const fixture = resolve(
  '../..',
  '.tmp/task38122-fixture-artifacts/bin/Winnow.Electron.Fixtures/debug/Winnow.Electron.Fixtures.dll',
)
let app: ElectronApplication, page: Page, directory: string, mode: Mode
let detailsOrigin: Locator | undefined
let endpoint: { address: string; token: string }
const errors: string[] = []
type Call = {
  callId: number
  operation: string
  provider?: string
  id?: string
  workId?: number
  gateId?: string | null
  responseReady: boolean
  completed: boolean
  canceled: boolean
  delivered: boolean
  json?: string | null
}
type State = {
  kind: string
  calls: Call[]
  workId: number
  sourceOrder?: string[]
  storedSourceOrder?: string | null
  choices: Record<string, unknown>[]
  retained?: { workId: number; assetId: string; exists: boolean }[]
}
const state = () => control<State>('state')
const details = () => page.locator('.avalon-details')
const browser = () => page.locator('.artwork-browser-dialog')
const identity = (key: { provider: string; id: string } | null | undefined) =>
  key ? { provider: key.provider, id: key.id } : null
async function control<T = unknown>(route: string, body?: unknown): Promise<T> {
  const response = await fetch(new URL(`/__fixture/artwork-state/${route}`, endpoint.address), {
    method: body === undefined ? 'GET' : 'POST',
    headers: {
      Authorization: `Bearer ${endpoint.token}`,
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!response.ok) throw Error(`Artwork fixture ${route}: ${response.status} ${await response.text()}`)
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
    Object.assign(window, { artworkStatePad: value })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          id: 'Native artwork fixture controller',
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
      ;(window as unknown as { artworkStatePad: { pressed: number[] } }).artworkStatePad.pressed = pressed
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
}
async function activate(target: Locator) {
  await target.focus()
  await expect(target).toBeFocused()
  if (mode === 'fullscreen') await tap(0)
  else await page.keyboard.press('Enter')
}
async function navigate(name: 'Library' | 'Activity' | 'Settings') {
  if (mode === 'desktop' && name === 'Settings') {
    await activate(page.getByRole('button', { name: 'Settings', exact: true }))
    return
  }
  await activate(
    page
      .getByRole('navigation', { name: 'Main navigation', exact: true })
      .getByRole('button', { name, exact: true }),
  )
}
async function surface() {
  await app.evaluate(({ BrowserWindow }, mode) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setMinimumSize(0, 0)
    window.setContentSize(1920, 1080)
    window.isFullScreen = () => mode === 'fullscreen'
    window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
    window.focus()
  }, mode)
  await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
  await pad()
}
async function seed(kind: string) {
  await navigate('Activity')
  return control<State>('seed', { kind })
}
async function openDetails(workId: number) {
  await navigate('Library')
  detailsOrigin = page.locator(`.avalon-library [data-avalon-game="${workId}"]`)
  await activate(detailsOrigin)
  await expect(details()).toBeVisible()
}
async function openBrowser(slot = 'Cover') {
  await activate(details().getByRole('button', { name: 'More', exact: true }))
  const actions =
    mode === 'fullscreen' ? page.getByRole('dialog', { name: 'More game actions', exact: true }) : details()
  await activate(actions.getByRole('button', { name: 'Artwork\u2026', exact: true }))
  await expect(browser()).toBeVisible()
  await activate(browser().getByRole('button', { name: `${slot} artwork`, exact: true }))
}
async function closeBrowser() {
  if (mode === 'fullscreen') await tap(1)
  else await page.keyboard.press('Escape')
  await expect(browser()).toHaveCount(0)
  await expect(details().getByRole('button', { name: 'More', exact: true })).toBeFocused()
}
async function closeDetails() {
  await page.keyboard.press('Escape')
  await expect(details()).toHaveCount(0)
  await expect(detailsOrigin!).toBeFocused()
}
async function capture(name: string) {
  await page.screenshot({ path: test.info().outputPath(`${mode}-${name}.png`) })
}
async function retained(target: Locator) {
  return target.evaluate((node) => {
    const key = crypto.randomUUID()
    const host = window as unknown as { retainedArtworkStateImages: Record<string, HTMLImageElement> }
    ;(host.retainedArtworkStateImages ??= {})[key] = node as HTMLImageElement
    return key
  })
}
async function retainedState(key: string) {
  return page.evaluate((key) => {
    const node = (window as unknown as { retainedArtworkStateImages: Record<string, HTMLImageElement> })
      .retainedArtworkStateImages[key]
    return { connected: node.isConnected, source: node.getAttribute('src') }
  }, key)
}
async function released(key: string) {
  await expect.poll(() => retainedState(key)).toEqual({ connected: false, source: null })
}
async function dimensions(image: Locator) {
  return image.evaluate((node) => ({
    width: (node as HTMLImageElement).naturalWidth,
    height: (node as HTMLImageElement).naturalHeight,
  }))
}
async function setOrder(order: string[]) {
  await api({
    route: 'preferences.presentation.put',
    params: { preference: 'ArtworkSourceOrder' },
    body: { value: order.join(',') },
  })
}
async function current(workId: number) {
  return api<ArtworkState>({ route: 'artwork.get', params: { workId, slot: 'Cover' } })
}
async function sourcePreferences() {
  await navigate('Settings')
  await activate(
    page
      .getByRole('navigation', { name: 'Settings section' })
      .getByRole('button', { name: 'Metadata & artwork', exact: true }),
  )
  if (mode === 'fullscreen')
    await activate(page.getByRole('button', { name: 'Artwork source order', exact: true }))
  await expect(page.locator('.artwork-source-order')).toBeVisible()
}

test.beforeAll(async () => {
  await readFile(fixture)
})
test.beforeEach(async ({}, info) => {
  test.setTimeout(90000)
  mode = info.title.startsWith('fullscreen') ? 'fullscreen' : 'desktop'
  detailsOrigin = undefined
  endpoint = undefined as unknown as typeof endpoint
  errors.length = 0
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-artwork-state-'))
  const profile = structuredClone(DEFAULT_PROFILE)
  profile.appearance.scale = 100
  profile.appearance.reducedMotion = true
  const preferences = profileDirectory(join(directory, 'electron-userdata'), directory)
  await mkdir(preferences, { recursive: true })
  await writeFile(join(preferences, 'preferences.json'), JSON.stringify(profile))
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/artwork-state-main.mjs'),
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
    },
    chromiumSandbox: true,
    timeout: 60000,
  })
  page = await app.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible({ timeout: 45000 })
  endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
  expect((await fetch(new URL('/__fixture/artwork-state/state', endpoint.address))).status).toBe(401)
  await page.evaluate(() => {
    const urls = { created: [] as string[], revoked: [] as string[] }
    Object.assign(window, { artworkStateUrls: urls })
    const create = URL.createObjectURL.bind(URL),
      revoke = URL.revokeObjectURL.bind(URL)
    URL.createObjectURL = (value) => {
      const source = create(value)
      urls.created.push(source)
      return source
    }
    URL.revokeObjectURL = (source) => {
      urls.revoked.push(source)
      revoke(source)
    }
  })
  await surface()
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
})
test.afterEach(async ({}, info) => {
  try {
    if (endpoint)
      await info.attach('artwork-state-fixture-ledger', {
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
      await info.attach('artwork-before-teardown', {
        body: await page.screenshot(),
        contentType: 'image/png',
      })
      await info.attach('artwork-before-teardown-dom', {
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

async function revoked(source: string) {
  return page.evaluate(
    (source) =>
      (window as unknown as { artworkStateUrls: { revoked: string[] } }).artworkStateUrls.revoked.includes(
        source,
      ),
    source,
  )
}
async function pixel(root: Locator, label: string) {
  const bounds = (await root.boundingBox())!
  const point = {
    x: Math.round(bounds.x + bounds.width * 0.88),
    y: Math.round(bounds.y + bounds.height * 0.4),
  }
  const png = await page.screenshot({ path: test.info().outputPath(`${mode}-${label}.png`) })
  const rgba = await app.evaluate(
    ({ nativeImage }, { bytes, point }) => {
      const image = nativeImage.createFromBuffer(Buffer.from(bytes)),
        size = image.getSize(),
        buffer = image.toBitmap()
      const offset = (point.y * size.width + point.x) * 4
      return [buffer[offset + 2], buffer[offset + 1], buffer[offset], buffer[offset + 3]]
    },
    { bytes: [...png], point },
  )
  await test.info().attach(`pixel-${label}`, {
    body: JSON.stringify({ bounds, point, rgba }),
    contentType: 'application/json',
  })
  return rgba
}

for (const presentation of ['desktop', 'fullscreen'] as const) {
  test(`${presentation} source live order retains the 32 by 10 backdrop until the held 32 by 18 replacement and detaches its subscription`, async () => {
    const seeded = await seed('live')
    await openDetails(seeded.workId)
    const root = details().locator('.avalon-backdrop'),
      old = root.locator('[data-key="steam-hero:42"] img')
    await expect(old).toBeVisible()
    await expect.poll(() => dimensions(old)).toEqual({ width: 32, height: 10 })
    const oldNode = await retained(old),
      oldSource = (await old.getAttribute('src'))!
    const before = await pixel(root, 'original-steam32x10')
    const gate = await control<{ gateId: string }>('arm', {
      operation: 'image',
      provider: 'igdb-backdrop',
      id: 'art',
      ignoreCancellation: true,
    })
    const order =
      presentation === 'desktop' ? ['igdb', 'steam', 'steamgriddb'] : ['igdb', 'steamgriddb', 'steam']
    await setOrder(order)
    expect((await state()).sourceOrder).toEqual(order)
    await expect
      .poll(async () =>
        (await state()).calls.some((call) => call.gateId === gate.gateId && call.responseReady),
      )
      .toBe(true)
    await expect(root).toHaveAttribute('data-loading', 'true')
    await expect(old).toBeVisible()
    expect(await retainedState(oldNode)).toEqual({ connected: true, source: oldSource })
    expect(await revoked(oldSource)).toBe(false)
    expect(await pixel(root, 'held-order-retains-original')).toEqual(before)
    await control('release', { gateId: gate.gateId })
    const next = root.locator('[data-key="igdb-backdrop:art"] img')
    await expect(next).toBeVisible()
    await expect.poll(() => dimensions(next)).toEqual({ width: 32, height: 18 })
    await expect(root).not.toHaveAttribute('data-loading')
    const replacementSource = (await next.getAttribute('src'))!
    expect(replacementSource).not.toBe(oldSource)
    // The source keeps one Image control and replaces its bitmap; React likewise reuses its img.
    expect(await retainedState(oldNode)).toEqual({ connected: true, source: replacementSource })
    await expect.poll(() => revoked(oldSource)).toBe(true)
    expect(await pixel(root, 'replacement-igdb32x18')).not.toEqual(before)
    const nextNode = await retained(next),
      nextSource = (await next.getAttribute('src'))!
    await closeDetails()
    await navigate('Activity')
    await expect(page.locator('.avalon-backdrop')).toHaveCount(0)
    await released(nextNode)
    await expect.poll(() => revoked(nextSource)).toBe(true)
    const count = (await state()).calls.filter((call) => call.operation === 'backdrop').length
    await setOrder(['steam', 'igdb', 'steamgriddb'])
    expect((await state()).sourceOrder).toEqual(['steam', 'igdb', 'steamgriddb'])
    // The source pumps queued preference notifications after disposal before inspecting requests.
    await page.waitForTimeout(300)
    expect((await state()).calls.filter((call) => call.operation === 'backdrop')).toHaveLength(count)
    await capture('detached-source-order-no-backdrop')
  })
}

test('fullscreen unowned group root tries rootsaved then Steam42 then the original SteamGridDB50584 asset and excludes childsaved', async () => {
  const seeded = await seed('root')
  await navigate('Library')
  await expect(page.locator(`.avalon-library [data-avalon-game="${seeded.workId}"]`)).toBeVisible()
  const root = page.locator('.avalon-library-backdrop')
  const id = '61ba87bf4177f576150389d84d14bb01.png'
  const image = root.locator(`[data-key="steamgriddb-hero:${id}"] img`)
  await expect(image).toBeVisible()
  await expect.poll(() => dimensions(image)).toEqual({ width: 32, height: 10 })
  const facts = await state()
  const requests = facts.calls.filter((call) => call.operation === 'image')
  const keys = requests.map((call) => `${call.provider}:${call.id}`)
  const relevant = keys.filter((key) =>
    ['user:rootsaved', 'steam-hero:42', `steamgriddb-hero:${id}`].includes(key),
  )
  expect([...new Set(relevant)]).toEqual(['user:rootsaved', 'steam-hero:42', `steamgriddb-hero:${id}`])
  expect(keys).not.toContain('user:childsaved')
  const retainedImage = await retained(image),
    source = (await image.getAttribute('src'))!
  await capture('unowned-root-sgdb-fallback')
  await navigate('Activity')
  await expect(root).toHaveCount(0)
  await released(retainedImage)
  await expect.poll(() => revoked(source)).toBe(true)
})

for (const presentation of ['desktop', 'fullscreen'] as const) {
  test(`${presentation} browser Current follows the live header cover while a saved choice wins across projection changes and source order persists`, async () => {
    const seeded = await seed('browser')
    expect(identity((await current(seeded.workId)).current?.previewKey)).toEqual({
      provider: 'steam',
      id: '620',
    })
    await openDetails(seeded.workId)
    await openBrowser()
    const shown = () => browser().locator('.artwork-browser-gallery > .artwork-choice')
    await expect(shown()).toHaveAttribute('data-artwork-candidate', 'automatic:igdb:preferred')
    await expect(shown()).toContainText('Current')
    await expect(shown()).toHaveAttribute('aria-pressed', 'true')
    await expect(browser().getByRole('button', { name: 'Use artwork', exact: true })).toBeDisabled()
    const candidate = browser().locator('.artwork-source-group [data-artwork-candidate="igdb:manual"]')
    const original = await current(seeded.workId)
    await activate(candidate)
    await expect(candidate).toHaveAttribute('aria-pressed', 'true')
    await expect(shown()).toContainText('Current')
    expect((await state()).choices).toHaveLength(0)
    expect(await current(seeded.workId)).toEqual(original)
    await activate(browser().getByRole('button', { name: 'Use artwork', exact: true }))
    await expect.poll(async () => (await current(seeded.workId)).current?.previewKey.provider).toBe('user')
    const saved = await current(seeded.workId)
    expect(saved.revision).not.toBe(original.revision)
    expect(saved.current).toMatchObject({ sourceId: 'igdb', assetId: 'manual', isCurrent: true })
    await expect(shown()).toHaveAttribute('data-artwork-candidate', 'igdb:manual')
    await expect(shown()).toContainText('Current')
    await expect(browser().getByRole('button', { name: 'Use artwork', exact: true })).toBeDisabled()
    expect((await state()).choices).toHaveLength(1)
    expect((await state()).retained).toEqual([{ workId: seeded.workId, assetId: 'manual', exists: true }])
    await control('change', { stage: 'newprojection' })
    await expect(shown()).toHaveAttribute('data-artwork-candidate', 'igdb:manual')
    expect(identity((await current(seeded.workId)).current?.previewKey)).toEqual(
      identity(saved.current?.previewKey),
    )
    expect((await current(seeded.workId)).revision).toBe(saved.revision)
    await capture('saved-current-over-new-projection')
    await closeBrowser()
    await closeDetails()
    await page.reload()
    await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
    await pad()
    await openDetails(seeded.workId)
    await openBrowser()
    await expect(shown()).toHaveAttribute('data-artwork-candidate', 'igdb:manual')
    expect(identity((await current(seeded.workId)).current?.previewKey)).toEqual(
      identity(saved.current?.previewKey),
    )
    await activate(browser().getByRole('button', { name: 'Use automatic', exact: true }))
    await expect.poll(async () => (await state()).choices.length).toBe(0)
    await expect(shown()).toHaveAttribute('data-artwork-candidate', 'automatic:igdb:newprojection')
    expect((await current(seeded.workId)).revision).not.toBe(saved.revision)
    await control('change', { stage: 'steam440' })
    await expect(shown()).toHaveAttribute('data-artwork-candidate', 'automatic:steam:440')
    await capture('automatic-current-follows-header')
    await closeBrowser()
    await closeDetails()
    await sourcePreferences()
    const rows = page.locator('.artwork-source-order li > span')
    await expect(rows).toHaveText(['High-resolution Steam heroes', 'SteamGridDB', 'IGDB'])
    await activate(page.getByRole('button', { name: 'Move IGDB up', exact: true }))
    await expect(rows).toHaveText(['High-resolution Steam heroes', 'IGDB', 'SteamGridDB'])
    await expect(page.getByRole('button', { name: 'Move IGDB up', exact: true })).toBeEnabled()
    await activate(page.getByRole('button', { name: 'Move IGDB up', exact: true }))
    await expect(rows).toHaveText(['IGDB', 'High-resolution Steam heroes', 'SteamGridDB'])
    await expect.poll(async () => (await state()).sourceOrder).toEqual(['igdb', 'steam', 'steamgriddb'])
    expect((await state()).storedSourceOrder).toBe('igdb,steam,steamgriddb')
    await capture('persisted-source-order')
    await page.reload()
    await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
    await pad()
    await sourcePreferences()
    await expect(rows).toHaveText(['IGDB', 'High-resolution Steam heroes', 'SteamGridDB'])
    expect((await state()).sourceOrder).toEqual(['igdb', 'steam', 'steamgriddb'])
    expect((await state()).storedSourceOrder).toBe('igdb,steam,steamgriddb')
  })
}
