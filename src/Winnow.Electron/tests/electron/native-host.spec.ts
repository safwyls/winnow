import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Page,
  type Locator,
} from '@playwright/test'
import electronPath from 'electron'
import { mkdtemp, readFile, stat, mkdir, writeFile } from 'node:fs/promises'
import { join, resolve, relative, isAbsolute } from 'node:path'
import { closeFixture } from './fixture-cleanup'
import { execFile } from 'node:child_process'
import { promisify } from 'node:util'
import { prebuiltFixture as fixture, prebuiltActivationHelper as helper } from './prebuilt-backend'

let app: ElectronApplication, page: Page, directory: string
let endpoint: { address: string; token: string }
let mode: 'desktop' | 'fullscreen'
let helperPid: number | undefined
const alive = (pid: number) => {
  try {
    process.kill(pid, 0)
    return true
  } catch {
    return false
  }
}
type FixtureState = {
  root: string
  databasePath: string
  coverCacheDirectory: string
  userThemeDirectory: string
  theme: string
  authored: boolean
  fullscreen: boolean
  workId: number
  headerWorkId: number
  ownershipId: number
  expectedArtKey: { provider: string; id: string }
  calls: {
    operation: string
    workId?: number
    slot?: string
    provider?: string
    id?: string
    width?: number
    held?: boolean
    responseReady?: boolean
    completed?: boolean
    canceled?: boolean
  }[]
}
async function control<T = FixtureState>(action: string, body?: unknown): Promise<T> {
  const response = await fetch(new URL(`/__fixture/native-host/${action}`, endpoint.address), {
    method: body === undefined ? 'GET' : 'POST',
    headers: {
      Authorization: `Bearer ${endpoint.token}`,
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!response.ok) throw Error(`Native host fixture ${action}: ${response.status}`)
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}
async function tap(button: number) {
  for (const pressed of [[], [button], []])
    await page.evaluate(async (pressed) => {
      ;(window as any).__nativeHostPad.pressed = pressed
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
}
async function activate(target: Locator) {
  await target.focus()
  await expect(target).toBeFocused()
  if (mode === 'fullscreen') await tap(0)
  else await page.keyboard.press('Enter')
}
const publications = () =>
  app.evaluate(
    () =>
      (globalThis as any).__nativeHost.publications as {
        items: { type: string; items: { title: string; args: string; iconPath: string }[] }[] | null
        result: string
        at: number
      }[],
  )

test.beforeAll(async () => {
  await readFile(fixture)
})
test.beforeEach(async ({}, info) => {
  app = undefined!
  page = undefined!
  endpoint = undefined!
  helperPid = undefined
  test.setTimeout(60000)
  mode = info.title.startsWith('fullscreen') ? 'fullscreen' : 'desktop'
  const authored = info.title.includes('authored legacy')
  directory = await mkdtemp(
    join(resolve('../..', '.tmp'), `winnow-electron-native-host-${mode}-${authored ? 'authored' : 'house'}-`),
  )
  const hostApp = join(directory, 'test-host-app')
  await mkdir(hostApp)
  const metadata = JSON.parse(await readFile(resolve('package.json'), 'utf8'))
  await writeFile(
    join(hostApp, 'package.json'),
    JSON.stringify({
      name: metadata.name,
      productName: metadata.productName,
      version: metadata.version,
      type: 'module',
      main: resolve('tests/electron/native-host-main.mjs'),
    }),
  )
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [hostApp, '--data-dir', directory, '--no-sync', '--force-color-profile=srgb'],
    env: {
      ...(Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ) as Record<string, string>),
      WINNOW_BACKEND_PATH: fixture,
      WINNOW_ACTIVATION_HELPER_PATH: helper,
    },
    chromiumSandbox: true,
  })
  page = await app.firstWindow()
  await expect(page.locator('.avalon-shell')).toBeVisible({ timeout: 30000 })
  endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
  helperPid = await app.evaluate(() => (globalThis as any).__nativeHost.helper.processId)
  expect((await fetch(new URL('/__fixture/native-host/state', endpoint.address))).status).toBe(401)
  await app.evaluate(({ BrowserWindow }, mode) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setMinimumSize(0, 0)
    window.setContentSize(mode === 'fullscreen' ? 1920 : 1600, mode === 'fullscreen' ? 1080 : 1000)
    window.isFullScreen = () => mode === 'fullscreen'
    window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
    window.focus()
  }, mode)
  await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
  await page.evaluate(() => {
    const pad = ((window as any).__nativeHostPad = { pressed: [] as number[] })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          id: 'Native host fixture controller',
          connected: true,
          mapping: 'standard',
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, i) => ({
            pressed: pad.pressed.includes(i),
            touched: pad.pressed.includes(i),
            value: pad.pressed.includes(i) ? 1 : 0,
          })),
        },
      ],
    })
  })
})
test.afterEach(async () => {
  try {
    if (endpoint) {
      await control('release', {}).catch(() => undefined)
      await test.info().attach('native-host-backend-ledger', {
        body: Buffer.from(JSON.stringify(await control('state'))),
        contentType: 'application/json',
      })
    }
    if (app)
      await test.info().attach('native-host-shell-ledger', {
        body: Buffer.from(JSON.stringify(await app.evaluate(() => (globalThis as any).__nativeHost))),
        contentType: 'application/json',
      })
    if (test.info().status !== test.info().expectedStatus && page)
      await page
        .screenshot({ path: test.info().outputPath('native-host-before-teardown.png') })
        .catch(() => undefined)
  } finally {
    await closeFixture(app && app.process().exitCode === null ? app : undefined, directory)
    if (helperPid) await expect.poll(() => alive(helperPid!), { timeout: 10000 }).toBe(false)
  }
})

for (const surface of ['desktop', 'fullscreen'] as const)
  for (const authored of [false, true])
    test(`${surface} native host preserves ${authored ? 'authored legacy theme precedence' : 'the house legacy alias'} with real isolated data artwork tasks and frontend build identity`, async () => {
      const before = await control('state')
      expect(before.theme).toBe('hoard')
      expect(before.authored).toBe(authored)
      expect(before.fullscreen).toBe(surface === 'fullscreen')
      expect(before.ownershipId).toBe(42)
      expect(resolve(before.root)).toBe(directory)
      expect(resolve(before.databasePath)).toBe(join(directory, 'winnow.db'))
      expect(resolve(before.coverCacheDirectory)).toBe(join(directory, 'covers'))
      expect(resolve(before.userThemeDirectory)).toBe(join(directory, 'themes'))
      const inspected = await promisify(execFile)(
        fixture,
        ['--inspect-frontend-activation', '--data-dir', directory],
        { windowsHide: true, timeout: 10000 },
      )
      const acl = JSON.parse(inspected.stdout)
      const observed = await app.evaluate(() => ({
        pid: process.pid,
        helper: (globalThis as any).__nativeHost.helper,
      }))
      expect(acl.activationProcessId).toBe(observed.pid)
      expect(acl.mutexName).toBe(observed.helper.primary.mutexName)
      expect(acl.pipeName).toBe(observed.helper.primary.pipeName)
      for (const kernelObject of [acl.mutex, acl.pipe]) {
        expect(kernelObject.ownerSid).toBe(acl.currentSid)
        expect(kernelObject.isProtected).toBe(true)
        expect(kernelObject.rules).toHaveLength(2)
        expect(
          kernelObject.rules.map((rule: any) => ({
            type: rule.type,
            sid: rule.sid,
            inherited: rule.inherited,
          })),
        ).toEqual([
          { type: 'Deny', sid: 'S-1-5-2', inherited: false },
          { type: 'Allow', sid: acl.currentSid, inherited: false },
        ])
      }
      await test.info().attach('native-kernel-access-policy', {
        body: Buffer.from(JSON.stringify({ acl, observed })),
        contentType: 'application/json',
      })
      for (const path of [before.databasePath, before.coverCacheDirectory, before.userThemeDirectory]) {
        const inside = relative(directory, path)
        expect(inside.startsWith('..') || isAbsolute(inside)).toBe(false)
        expect(await stat(path)).toBeTruthy()
      }
      await expect
        .poll(async () => (await page.evaluate(() => window.winnow.loadPreferences())) as any)
        .toMatchObject({ settings: { avalon: { palette: authored ? 'hoard' : 'winnow' } } })
      const catalogue = await page.evaluate(() => window.winnow.listAvalonThemes!())
      if (authored) expect(JSON.stringify(catalogue)).toContain('My Hoard')
      const hasGame = (values: Awaited<ReturnType<typeof publications>>, decorated: boolean) =>
        values.some((value) =>
          value.items?.some((category) =>
            category.items.some(
              (item) =>
                item.args.includes('--jump-list-game 42') &&
                item.iconPath.includes('jump-list-icons') === decorated,
            ),
          ),
        )
      await expect.poll(async () => hasGame(await publications(), false)).toBe(true)
      await expect
        .poll(async () =>
          (await control('state')).calls.some(
            (call) => call.operation === 'image' && call.width === 128 && call.held && call.responseReady,
          ),
        )
        .toBe(true)
      expect(hasGame(await publications(), true)).toBe(false)
      await control('release', {})
      await expect.poll(async () => hasGame(await publications(), true)).toBe(true)
      const after = await control('state')
      expect(
        after.calls.some((call) => call.operation === 'artworkState' && call.workId === after.headerWorkId),
      ).toBe(true)
      expect(
        after.calls.some(
          (call) =>
            call.operation === 'image' &&
            call.width === 128 &&
            call.provider === after.expectedArtKey.provider &&
            call.id === after.expectedArtKey.id,
        ),
      ).toBe(true)
      for (const publication of await publications())
        for (const category of publication.items ?? [])
          for (const item of category.items) {
            expect(item.args).toContain('--data-dir ')
            expect(item.args).toContain(directory)
            if (item.iconPath.includes('jump-list-icons'))
              expect(resolve(item.iconPath).startsWith(join(directory, 'jump-list-icons'))).toBe(true)
          }
      await activate(
        mode === 'desktop'
          ? page.getByRole('button', { name: 'Settings', exact: true })
          : page
              .getByRole('navigation', { name: 'Main navigation' })
              .getByRole('button', { name: 'Settings', exact: true }),
      )
      await activate(
        page
          .getByRole('navigation', { name: 'Settings section' })
          .getByRole('button', { name: 'Application', exact: true }),
      )
      const about = page.getByRole('region', { name: 'About Winnow', exact: true })
      await about.scrollIntoViewIfNeeded()
      await expect(about).toBeVisible()
      const info = await page.evaluate(() => window.winnow.applicationInfo!())
      expect(info.packaged).toBe(true)
      expect(info.version).toBe(JSON.parse(await readFile(resolve('package.json'), 'utf8')).version)
      expect(info.commit).toMatch(/^[a-f0-9]{40}$/)
      await expect(about.locator('dd')).toHaveText([info.version, info.commit])
      await page.screenshot({
        path: test.info().outputPath(`${surface}-${authored ? 'authored' : 'house'}-about-native-host.png`),
      })
      await test.info().attach('native-host-identity-theme-ledger', {
        body: Buffer.from(
          JSON.stringify({
            info,
            catalogue,
            dataPaths: {
              root: before.root,
              database: before.databasePath,
              covers: before.coverCacheDirectory,
              themes: before.userThemeDirectory,
            },
          }),
        ),
        contentType: 'application/json',
      })
      if (surface === 'desktop' && !authored) {
        const child = app.process()
        await test.info().attach('native-host-shell-ledger-before-loss', {
          body: Buffer.from(JSON.stringify(await app.evaluate(() => (globalThis as any).__nativeHost))),
          contentType: 'application/json',
        })
        await app.evaluate(({ app }) => app.setJumpList(null))
        process.kill(helperPid!)
        await expect.poll(() => child.exitCode, { timeout: 30000 }).toBe(3)
        const error = await readFile(join(directory, 'frontend-error.jsonl'), 'utf8')
        expect(error).toContain('Winnow could not start')
        expect(error).not.toContain('Unhandled exception')
        expect(
          (
            await fetch(new URL('/api/v1/health', endpoint.address), {
              headers: { Authorization: `Bearer ${endpoint.token}` },
            })
          ).ok,
        ).toBe(true)
        await test
          .info()
          .attach('native-helper-loss-report', { body: Buffer.from(error), contentType: 'application/json' })
        app = undefined!
        page = undefined!
      }
    })
