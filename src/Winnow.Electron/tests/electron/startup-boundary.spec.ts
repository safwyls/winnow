import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Page,
  type Locator,
} from '@playwright/test'
import electronPath from 'electron'
import { spawn, execFile } from 'node:child_process'
import { promisify } from 'node:util'
import { mkdtemp, mkdir, readFile, writeFile, cp } from 'node:fs/promises'
import { join, resolve, dirname } from 'node:path'
import { DatabaseSync } from 'node:sqlite'
import { createServer } from 'node:net'
import { DEFAULT_PROFILE } from '../../src/shared/theme'
import { profileDirectory } from '../../src/main/storage'
import { closeFixture } from './fixture-cleanup'
import { libraryAction } from './library-controls'
import {
  prebuiltBackend as backend,
  prebuiltFixture as fixture,
  prebuiltActivationHelper,
} from './prebuilt-backend'

const main = resolve('tests/electron/startup-boundary-main.mjs')
const execute = promisify(execFile)
const environment = (path: string, report?: string) => ({
  ...(Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>),
  WINNOW_BACKEND_PATH: path,
  WINNOW_ACTIVATION_HELPER_PATH: prebuiltActivationHelper,
  ...(report ? { WINNOW_STARTUP_REPORT: report } : {}),
})
type Lease = {
  operation: string
  processId: number
  threadId: number
  threadPool: boolean
  synchronizationContext: string | null
}
type Call = {
  id: number
  route: string
  method: string
  processId: number
  completed: boolean
  status: number
  leases: Lease[]
  snapshots: { workCount: number; ownershipCount: number; bucketCount: number; listReleaseIds: number[] }[]
}
type State = {
  ready: boolean
  fullscreen: boolean
  setup: boolean
  processId: number
  source: string
  calls: Call[]
}
async function state(directory: string): Promise<State> {
  const endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
  const response = await fetch(new URL('/__fixture/startup-boundary/state', endpoint.address), {
    headers: { Authorization: `Bearer ${endpoint.token}` },
  })
  expect(response.ok).toBe(true)
  return response.json()
}
async function profile(directory: string) {
  const value = structuredClone(DEFAULT_PROFILE)
  value.appearance.scale = 100
  value.appearance.reducedMotion = true
  const path = profileDirectory(join(directory, 'electron-userdata'), directory)
  await mkdir(path, { recursive: true })
  await writeFile(join(path, 'preferences.json'), JSON.stringify(value))
}
async function activate(page: Page, target: Locator) {
  await target.focus()
  await expect(target).toBeFocused()
  await page.keyboard.press('Enter')
}
test.beforeAll(async () => {
  await Promise.all([readFile(fixture), readFile(backend)])
})

for (const mode of ['desktop', 'fullscreen'])
  test(`${mode} saved startup shows a responsive native window with one exact first library snapshot and backend-only read leases`, async ({}, info) => {
    test.setTimeout(90000)
    const directory = await mkdtemp(
      join(resolve('../..', '.tmp'), `winnow-electron-startup-boundary-${mode}-`),
    )
    await profile(directory)
    let app: ElectronApplication | undefined
    let page: Page | undefined
    const errors: string[] = []
    try {
      const start = performance.now()
      app = await electron.launch({
        executablePath: electronPath as unknown as string,
        args: [main, '--data-dir', directory, '--no-sync', '--force-color-profile=srgb'],
        env: environment(fixture),
        chromiumSandbox: true,
        timeout: 30000,
      })
      page = await app.firstWindow()
      page.on('pageerror', (error) => errors.push(error.message))
      await expect(page.getByRole('navigation', { name: 'Main navigation', exact: true })).toBeVisible({
        timeout: 30000,
      })
      await expect(page.locator('.startup-presentation')).toHaveCount(0)
      const elapsed = performance.now() - start
      expect(elapsed).toBeLessThan(30000)
      const native = await app.evaluate(({ BrowserWindow }) => {
        const window = BrowserWindow.getAllWindows()[0]!
        return {
          mainPid: process.pid,
          rendererPid: window.webContents.getOSProcessId(),
          visible: window.isVisible(),
          destroyed: window.isDestroyed(),
          fullscreen: window.isFullScreen(),
          nativeHandle: window.getNativeWindowHandle().some((byte) => byte !== 0),
        }
      })
      expect(native).toMatchObject({
        visible: true,
        destroyed: false,
        fullscreen: mode === 'fullscreen',
        nativeHandle: true,
      })
      const responsive = await execute(
        'powershell.exe',
        [
          '-NoProfile',
          '-NonInteractive',
          '-Command',
          `$process = [Diagnostics.Process]::GetProcessById(${native.mainPid}); $idle = $process.WaitForInputIdle(10000); Start-Sleep -Milliseconds 1000; $process.Refresh(); @{ idle=$idle; exited=$process.HasExited; responding=$process.Responding } | ConvertTo-Json -Compress`,
        ],
        { windowsHide: true, timeout: 15000 },
      )
      expect(JSON.parse(responsive.stdout)).toEqual({ idle: true, exited: false, responding: true })
      const first = await state(directory)
      expect(first).toMatchObject({
        ready: true,
        fullscreen: mode === 'fullscreen',
        setup: false,
        source: 'LibraryReadFixtures.Seed(20)',
      })
      expect(first.processId).not.toBe(native.rendererPid)
      expect(first.processId).not.toBe(native.mainPid)
      const libraries = first.calls.filter((call) => call.route === '/api/v1/library' && call.completed)
      expect(libraries).toHaveLength(1)
      expect(libraries[0]!.snapshots).toEqual([
        { workCount: 20, ownershipCount: 20, bucketCount: 20, listReleaseIds: [20, 1] },
      ])
      expect(libraries[0]!.leases.filter((lease) => lease.operation === 'library.snapshot')).toHaveLength(1)
      expect(libraries[0]!.leases.filter((lease) => lease.operation === 'request')).toHaveLength(3)
      expect(first.calls.filter((call) => call.route === '/api/v1/identity/review/')).toEqual([])
      const initialFeedReads = first.calls.filter((call) => call.route === '/api/v1/feed').length
      expect(initialFeedReads).toBe(1)
      await activate(page, page.getByRole('button', { name: 'Library', exact: true }).first())
      await expect(page.locator('[data-avalon-game]').first()).toBeVisible()
      await expect(page.locator('.avalon-library .avalon-results-count')).toHaveText('20 games')
      if (mode === 'desktop') await expect(page.locator('.avalon-library-total strong')).toHaveText('20')
      expect((await state(directory)).calls.filter((call) => call.route === '/api/v1/feed').length).toBe(
        initialFeedReads,
      )
      await page.screenshot({ path: info.outputPath(`${mode}-responsive-library.png`) })
      await activate(page, page.getByRole('button', { name: 'Settings', exact: true }))
      const settings = page.getByRole('navigation', { name: 'Settings section', exact: true })
      await activate(page, settings.getByRole('button', { name: 'Appearance', exact: true }))
      await expect(page.locator('.settings-page, .fullscreen-settings-page').first()).toBeVisible()
      await activate(page, settings.getByRole('button', { name: 'Library', exact: true }))
      await expect(
        page.getByRole(mode === 'desktop' ? 'combobox' : 'button', {
          name: 'Default library sort',
          exact: true,
        }),
      ).toBeVisible()
      await activate(
        page,
        page
          .getByRole('navigation', { name: 'Main navigation', exact: true })
          .getByRole('button', { name: 'Library', exact: true }),
      )
      await activate(page, await libraryAction(page, 'Manage library'))
      const list = page
        .locator('.feature-panel')
        .filter({ has: page.getByRole('heading', { name: 'Try next', exact: true }) })
      await expect(list).toContainText('2 editions')
      await list.locator('summary').click()
      await expect(list.locator('article strong')).toHaveText(['Game 20', 'Game 1'])
      await page.screenshot({ path: info.outputPath(`${mode}-published-list-order.png`) })
      await activate(page, page.getByRole('button', { name: 'Identity review', exact: true }))
      await expect(page.locator('.merge-queue')).toBeVisible()
      await expect(page.locator('.merge-queue')).not.toHaveAttribute('aria-busy', 'true')
      const last = await state(directory)
      for (const call of last.calls)
        for (const lease of call.leases) {
          expect(lease.processId).toBe(last.processId)
          expect(lease.processId).not.toBe(native.rendererPid)
          expect(lease.synchronizationContext).toBeNull()
        }
      expect(
        last.calls.some(
          (call) =>
            call.route === '/api/v1/preferences/presentation' && call.completed && call.status === 200,
        ),
      ).toBe(true)
      expect(
        last.calls.some(
          (call) => call.route === '/api/v1/preferences/library' && call.completed && call.status === 200,
        ),
      ).toBe(true)
      expect(
        last.calls.some(
          (call) => call.route === '/api/v1/identity/review/' && call.completed && call.status === 200,
        ),
      ).toBe(true)
      const startup = await app.evaluate(
        () =>
          (globalThis as any).__startupBoundary as {
            fetches: { path: string; nativeReady: boolean }[]
            windows: { nativeReady: boolean }[]
          },
      )
      expect(startup.fetches.length).toBeGreaterThan(0)
      expect(startup.fetches.some((read) => read.path === '/api/v1/events')).toBe(true)
      expect(startup.fetches.every((read) => read.nativeReady)).toBe(true)
      expect(startup.windows.every((window) => window.nativeReady)).toBe(true)
      await info.attach('source-startup-native-and-repository-ledger', {
        body: JSON.stringify(
          {
            elapsed,
            native,
            responsive: JSON.parse(responsive.stdout),
            first,
            last,
            main: await app.evaluate(() => (globalThis as any).__startupBoundary),
          },
          null,
          2,
        ),
        contentType: 'application/json',
      })
      expect(errors).toEqual([])
    } finally {
      if (info.status !== info.expectedStatus && page && !page.isClosed())
        await info.attach('startup-before-teardown', {
          body: await page.screenshot(),
          contentType: 'image/png',
        })
      await closeFixture(app, directory)
    }
  })

for (const mode of ['desktop', 'fullscreen'])
  for (const fault of ['configuration', 'logging', 'future-schema'])
    test(`${mode} ${fault} startup exits three without a library window and preserves database bytes`, async ({}, info) => {
      test.setTimeout(60000)
      const directory = await mkdtemp(
        join(resolve('../..', '.tmp'), `winnow-electron-startup-boundary-${mode}-`),
      )
      await execute(fixture, ['--data-dir', directory, '--no-sync', '--prepare-only'], {
        windowsHide: true,
        timeout: 30000,
      })
      const database = join(directory, 'winnow.db')
      if (fault === 'future-schema') {
        const connection = new DatabaseSync(database)
        try {
          connection
            .prepare('INSERT INTO SchemaVersions (ScriptName,Applied) VALUES (?,?)')
            .run('Winnow.Data.Migrations.9999_future_schema.sql', '2026-09-11 00:00:00')
          connection.exec('PRAGMA wal_checkpoint(TRUNCATE)')
        } finally {
          connection.close()
        }
      }
      const before = await readFile(database)
      const installation = join(directory, 'isolated-backend-installation')
      await cp(dirname(backend), installation, { recursive: true })
      if (fault !== 'future-schema')
        await writeFile(
          join(installation, 'appsettings.local.json'),
          fault === 'configuration'
            ? '{ invalid-json'
            : '{"Logging":{"LogLevel":{"Default":"invalid-review-level"}}}',
        )
      const report = info.outputPath('actual-process-report.json')
      await mkdir(dirname(report), { recursive: true })
      const result = await fatal(directory, join(installation, 'Winnow.Backend.exe'), report)
      expect(result.code).toBe(3)
      expect(result.report.windows).toEqual([])
      expect(result.report.uncaught).toEqual([])
      expect(result.report.errors).toHaveLength(1)
      expect(result.report.errors[0].title).toBe('Winnow could not start')
      expect(`${result.stdout}\n${result.stderr}`).not.toContain('Unhandled exception')
      if (fault === 'future-schema') expect(result.report.errors[0].content).toContain('does not support')
      expect(await readFile(database)).toEqual(before)
      await info.attach('fatal-process-and-database-preservation', {
        body: JSON.stringify(
          { ...result, beforeBytes: before.length, afterBytes: (await readFile(database)).length },
          null,
          2,
        ),
        contentType: 'application/json',
      })
    })

test('unusable explicit data directory exits two before any native library window', async ({}, info) => {
  test.setTimeout(45000)
  const root = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-startup-boundary-unusable-'))
  const directory = join(root, 'actual-file')
  await writeFile(directory, 'a file cannot be the data directory')
  const report = info.outputPath('actual-process-report.json')
  await mkdir(dirname(report), { recursive: true })
  const result = await fatal(directory, backend, report)
  expect(result.code).toBe(2)
  expect(result.report.windows).toEqual([])
  expect(result.report.uncaught).toEqual([])
  expect(result.report.errors).toHaveLength(1)
  expect(result.report.errors[0].title).toBe('Winnow could not start')
  expect(await readFile(directory, 'utf8')).toBe('a file cannot be the data directory')
  await info.attach('explicit-data-directory-refusal', {
    body: JSON.stringify(result, null, 2),
    contentType: 'application/json',
  })
})

test('unloadable renderer startup reports exit three without an unhandled rejection and leaves its independent backend available until explicit cleanup', async ({}, info) => {
  test.setTimeout(45000)
  const directory = await mkdtemp(
    join(resolve('../..', '.tmp'), 'winnow-electron-startup-boundary-desktop-unloadable-'),
  )
  const reservation = createServer()
  await new Promise<void>((done) => reservation.listen(0, '127.0.0.1', done))
  const address = reservation.address()
  if (!address || typeof address === 'string') throw Error('Loopback port reservation failed')
  await new Promise<void>((done, reject) => reservation.close((error) => (error ? reject(error) : done())))
  const report = info.outputPath('actual-process-report.json')
  await mkdir(dirname(report), { recursive: true })
  let result: Awaited<ReturnType<typeof fatal>>
  try {
    result = await fatal(directory, fixture, report, {
      ELECTRON_RENDERER_URL: `http://127.0.0.1:${address.port}/`,
    })
    expect(result.code).toBe(3)
    expect(result.report.uncaught).toEqual([])
    expect(result.report.errors).toHaveLength(1)
    expect(result.report.errors[0].title).toBe('Winnow could not start')
    expect(`${result.stdout}\n${result.stderr}`).not.toMatch(
      /Unhandled (?:exception|rejection)|UnhandledPromiseRejection/i,
    )
    expect(result.report.windows).toHaveLength(1)
    expect(result.report.backendProcessId).toBeGreaterThan(0)
    expect((await state(directory)).ready).toBe(true)
    process.kill(result.report.backendProcessId, 0)
  } finally {
    await closeFixture(undefined, directory)
  }
  await expect
    .poll(
      () => {
        try {
          process.kill(result.report.backendProcessId, 0)
          return false
        } catch (error) {
          if ((error as NodeJS.ErrnoException).code === 'ESRCH') return true
          throw error
        }
      },
      { timeout: 10000 },
    )
    .toBe(true)
  await info.attach('actual-renderer-startup-failure', {
    body: JSON.stringify(result, null, 2),
    contentType: 'application/json',
  })
})

async function fatal(directory: string, path: string, report: string, extra: Record<string, string> = {}) {
  const child = spawn(
    electronPath as unknown as string,
    [main, '--data-dir', directory, '--no-sync', '--force-color-profile=srgb'],
    { env: { ...environment(path, report), ...extra }, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] },
  )
  let stdout = '',
    stderr = ''
  child.stdout.on('data', (value) => {
    stdout += value.toString()
  })
  child.stderr.on('data', (value) => {
    stderr += value.toString()
  })
  let timer: ReturnType<typeof setTimeout> | undefined
  try {
    const code = await Promise.race([
      new Promise<number | null>((resolve, reject) => {
        child.once('error', reject)
        child.once('exit', (code) => resolve(code))
      }),
      new Promise<never>((_, reject) => {
        timer = setTimeout(
          () => reject(Error(`Fatal startup did not exit within30seconds: ${stderr}`)),
          30000,
        )
      }),
    ])
    return { code, stdout, stderr, report: JSON.parse(await readFile(report, 'utf8')) }
  } finally {
    clearTimeout(timer)
    if (child.exitCode === null && child.pid)
      await execute('taskkill.exe', ['/pid', String(child.pid), '/T', '/F'], {
        windowsHide: true,
        timeout: 5000,
      }).catch(() => {})
  }
}
