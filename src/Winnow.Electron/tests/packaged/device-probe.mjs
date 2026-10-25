import assert from 'node:assert/strict'
import { execFile } from 'node:child_process'
import { createHash } from 'node:crypto'
import { mkdir, open, readFile, writeFile } from 'node:fs/promises'
import { basename, dirname, isAbsolute, join, resolve } from 'node:path'
import { promisify } from 'node:util'
import { _electron as electron, expect } from '@playwright/test'

// Opt-in local hardware evidence. It never changes display settings or supplies
// fake controller state, and cannot attach to an existing library.
const values = new Map(),
  args = process.argv.slice(2)
for (let index = 0; index < args.length; index += 2) {
  const [key, value] = args.slice(index, index + 2)
  assert(['--exe', '--output'].includes(key) && isAbsolute(value ?? '') && !values.has(key))
  values.set(key, value)
}
assert.equal(process.platform, 'win32', 'This probe records attached Windows displays.')
assert.equal(values.size, 2, 'Use --exe <absolute Winnow.exe> --output <new absolute evidence directory>.')
const executable = resolve(values.get('--exe')),
  output = resolve(values.get('--output'))
const installation = dirname(executable),
  directory = join(output, 'disposable-data')
await mkdir(output)
await mkdir(directory)
const reportPath = join(output, 'device-report.json')
const result = {
  startedAt: new Date().toISOString(),
  executable,
  directory,
  passed: false,
  closed: false,
  trials: [],
  focusReturns: [],
  errors: [],
  pageErrors: [],
  limitations: [
    'Keyboard and pointer input is automated through the actual packaged Chromium window on attached displays.',
    'No physical controller is connected; button mapping, held buttons, reconnect and battery reporting are not exercised.',
    'Mouse/keyboard focus recovery does not establish controller-owned cursor recovery.',
    'Renderer PNGs establish rendered geometry, not DWM material composition or TV-distance legibility.',
  ],
}
const persist = () => writeFile(reportPath, JSON.stringify(result, null, 2) + '\n')
const digest = async (file) =>
  createHash('sha256')
    .update(await readFile(file))
    .digest('hex')
const packageFiles = [
  'Winnow.exe',
  'resources/app.asar',
  'backend/Winnow.Backend.exe',
  'backend/Winnow.Backend.dll',
  'update-helper/Winnow.Update.Helper.exe',
  'update-helper/Winnow.Update.Helper.dll',
  'release-info.json',
]
const hashes = async () =>
  Object.fromEntries(
    await Promise.all(packageFiles.map(async (file) => [file, await digest(join(installation, file))])),
  )
const environment = Object.fromEntries(
  Object.entries(process.env).filter(
    ([key]) =>
      !/^(ELECTRON_RUN_AS_NODE|ELECTRON_RENDERER_URL|WINNOW_.*(?:PATH|FIXTURE.*)|Igdb__(ClientId|ClientSecret))$/i.test(
        key,
      ),
  ),
)
Object.assign(environment, { Igdb__ClientId: '', Igdb__ClientSecret: '' })
let application, page, endpoint, mainIdentity, backendIdentity
const ps = async (code) =>
  (
    await promisify(execFile)('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', code], {
      windowsHide: true,
      timeout: 10000,
    })
  ).stdout.trim()
async function identity(pid) {
  assert(Number.isSafeInteger(pid) && pid > 0)
  const text = await ps(
    `$p = Get-Process -Id ${pid} -ErrorAction SilentlyContinue; if ($p) { @{ pid=$p.Id; path=$p.Path; startTicks=$p.StartTime.ToUniversalTime().Ticks.ToString() } | ConvertTo-Json -Compress }; exit 0`,
  )
  return text ? JSON.parse(text) : null
}
async function gone(actor) {
  if (!actor) return
  await expect
    .poll(
      async () => {
        const current = await identity(actor.pid)
        return !current || current.startTicks !== actor.startTicks
      },
      { timeout: 10000 },
    )
    .toBe(true)
}
async function forceOwn(actor) {
  if (!actor) return
  const current = await identity(actor.pid)
  if (!current || current.startTicks !== actor.startTicks) return
  assert.equal(current.path.toLowerCase(), actor.path.toLowerCase())
  await ps(
    `$p = Get-Process -Id ${actor.pid} -ErrorAction SilentlyContinue; if ($p -and $p.StartTime.ToUniversalTime().Ticks.ToString() -eq '${actor.startTicks}') { $p.Kill() }`,
  )
  result.forcedCleanup ??= []
  result.forcedCleanup.push(actor.pid)
  await gone(actor)
}
const native = () =>
  application.evaluate(({ BrowserWindow, screen }) => {
    const window = BrowserWindow.getAllWindows()[0]
    return {
      bounds: window.getBounds(),
      normalBounds: window.getNormalBounds(),
      contentBounds: window.getContentBounds(),
      fullscreen: window.isFullScreen(),
      maximized: window.isMaximized(),
      minimized: window.isMinimized(),
      visible: window.isVisible(),
      focused: window.isFocused(),
      displayId: screen.getDisplayMatching(window.getBounds()).id,
      handle: window.getNativeWindowHandle().readBigUInt64LE().toString(),
    }
  })
const ready = async (mode) => {
  await expect(page.locator(`.avalon-shell.${mode}:visible`)).toBeVisible({ timeout: 30000 })
  await expect(page.locator('.startup-presentation')).toHaveCount(0, { timeout: 30000 })
}
const api = (input) =>
  page.evaluate(async (input) => {
    const response = await window.winnow.request(input)
    if (!response.ok) throw Error(`${input.route}: ${response.status}`)
    return response.data
  }, input)
async function tabTo(locator) {
  await expect(locator).toBeVisible()
  for (let count = 0; count < 100; count++) {
    if (await locator.evaluate((node) => node === document.activeElement)) return count
    await page.keyboard.press('Tab')
  }
  throw Error(
    `Keyboard did not reach ${(await locator.getAttribute('aria-label')) ?? (await locator.textContent())}`,
  )
}
async function route(name) {
  const button =
    name === 'Settings'
      ? page.getByRole('button', { name, exact: true })
      : page.getByRole('navigation', { name: 'Main navigation' }).getByRole('button', { name, exact: true })
  const tabs = await tabTo(button)
  await page.keyboard.press('Enter')
  await expect(button).toHaveAttribute('aria-current', 'page')
  return tabs
}
const tile = (id) => page.locator(`.avalon-shell:visible .avalon-cover[data-work-id="${id}"]`)
async function capture(label) {
  await page.screenshot({ path: join(output, `${label}.png`) })
}
async function uiState() {
  return page.evaluate(() => {
    const active = document.activeElement,
      rect = active?.getBoundingClientRect()
    return {
      viewport: { width: innerWidth, height: innerHeight, dpr: devicePixelRatio },
      mode: document.documentElement.dataset.mode,
      controllerCursor: document.documentElement.dataset.controller ?? null,
      focused: document.hasFocus(),
      active: active
        ? {
            tag: active.tagName,
            label: active.getAttribute('aria-label'),
            workId: active.getAttribute('data-work-id'),
            text: active.textContent?.slice(0, 100),
            focusVisible: active.matches(':focus-visible'),
            cursor: getComputedStyle(active).cursor,
            bounds: rect ? { x: rect.x, y: rect.y, width: rect.width, height: rect.height } : null,
          }
        : null,
      selected: [...document.querySelectorAll('.avalon-shell .avalon-cover[data-selected="true"]')]
        .filter((node) => node.getBoundingClientRect().width > 0)
        .map((node) => node.getAttribute('data-work-id')),
      gamepads: Array.from(navigator.getGamepads?.() ?? [])
        .filter(Boolean)
        .map((pad) => ({ id: pad.id, mapping: pad.mapping, connected: pad.connected })),
      controllerStatus: document.querySelector('.avalon-controller-status')?.textContent ?? null,
    }
  })
}
async function browse(mode, game, label) {
  const tabsToLibrary = await route('Library')
  await expect(tile(game.workId)).toBeVisible()
  const tabsToTile = await tabTo(tile(game.workId))
  await expect(tile(game.workId)).toBeFocused()
  await page.keyboard.press('Enter')
  const details = page.locator(`.avalon-details.${mode}:visible`)
  await expect(details).toBeVisible()
  await expect(details.locator('h1')).toHaveText(game.title)
  await capture(`${label}-${mode}-details`)
  await page.keyboard.press('Escape')
  await expect(details).toHaveCount(0)
  await expect(tile(game.workId)).toHaveAttribute('data-selected', 'true')
  const focusReturn = { label, mode, workId: game.workId, immediate: await uiState(), passed: false }
  result.focusReturns.push(focusReturn)
  // Modal teardown and fullscreen page focus both restore on a render frame.
  // Observe the result without sending another key that would own focus.
  await expect(tile(game.workId)).toBeFocused()
  await expect.poll(() => tile(game.workId).evaluate((node) => node.matches(':focus-visible'))).toBe(true)
  const afterBack = await uiState()
  focusReturn.restored = afterBack
  focusReturn.passed = true
  const tabsToSettings = await route('Settings')
  await capture(`${label}-${mode}-settings`)
  return { tabsToLibrary, tabsToTile, tabsToSettings, selectedWorkId: game.workId, afterBack }
}
try {
  result.package = JSON.parse(
    (await readFile(join(installation, 'release-info.json'), 'utf8')).replace(/^\uFEFF/, ''),
  )
  assert.equal(result.package.frontend, 'electron')
  result.packageHashes = await hashes()
  application = await electron.launch({
    executablePath: executable,
    args: ['--data-dir', directory, '--no-sync'],
    env: environment,
    chromiumSandbox: true,
    timeout: 30000,
  })
  result.application = await application.evaluate(({ app, screen }) => ({
    pid: process.pid,
    executable: process.execPath,
    packaged: app.isPackaged,
    version: app.getVersion(),
    appPath: app.getAppPath(),
    electron: process.versions.electron,
    chrome: process.versions.chrome,
    primaryDisplayId: screen.getPrimaryDisplay().id,
    displays: screen.getAllDisplays().map((display) => ({
      id: display.id,
      label: display.label,
      bounds: display.bounds,
      workArea: display.workArea,
      scaleFactor: display.scaleFactor,
      rotation: display.rotation,
      displayFrequency: display.displayFrequency,
      internal: display.internal,
      colorDepth: display.colorDepth,
    })),
  }))
  assert.equal(result.application.packaged, true)
  mainIdentity = await identity(result.application.pid)
  await persist()
  page = await application.firstWindow()
  page.on('pageerror', (error) => result.pageErrors.push(error.message))
  await ready('desktop')
  await expect
    .poll(() => page.evaluate(() => window.winnow.connection()), { timeout: 30000 })
    .toMatchObject({ connected: true })
  endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
  backendIdentity = await identity(endpoint.processId)
  assert.equal(
    backendIdentity.path.toLowerCase(),
    join(installation, 'backend/Winnow.Backend.exe').toLowerCase(),
  )
  result.processes = { frontend: mainIdentity, backend: backendIdentity }
  const setup = page.getByRole('button', { name: 'Skip setup', exact: true })
  if (await setup.isVisible()) await setup.click()
  const games = []
  for (const title of ['Device Alpha', 'Device Beta', 'Device Gamma'])
    games.push(
      await api({
        route: 'manual.create',
        body: {
          title,
          platformLabel: 'PC',
          executablePath: null,
          installPath: null,
          igdbId: null,
          steamAppId: null,
        },
      }),
    )
  result.games = games.map(({ workId, title }) => ({ workId, title }))
  assert.equal((await api({ route: 'library.get' })).games.length, 3)
  const displays = result.application.displays.sort(
    (a, b) =>
      Number(b.id === result.application.primaryDisplayId) -
      Number(a.id === result.application.primaryDisplayId),
  )
  for (const display of displays) {
    const trial = { display, passed: false },
      label = `display-${display.id}`
    result.trials.push(trial)
    try {
      await application.evaluate(({ BrowserWindow }, area) => {
        const window = BrowserWindow.getAllWindows()[0]
        window.setFullScreen(false)
        window.unmaximize()
        window.setBounds({
          x: area.x + 30,
          y: area.y + 30,
          width: Math.min(1280, area.width - 60),
          height: Math.min(900, area.height - 60),
        })
        window.show()
        window.focus()
      }, display.workArea)
      await ready('desktop')
      await expect.poll(async () => (await native()).displayId).toBe(display.id)
      trial.desktopWindow = await native()
      trial.desktop = await browse('desktop', games[1], label)
      // Tabbing past another cover legitimately selects it. Set the departure
      // selection after the Settings journey, then compare before further input.
      await route('Library')
      await tabTo(tile(games[1].workId))
      await expect(tile(games[1].workId)).toHaveAttribute('data-selected', 'true')
      trial.desktop.departure = await uiState()
      await page.keyboard.press('F11')
      await ready('fullscreen')
      await expect.poll(async () => (await native()).fullscreen).toBe(true)
      await expect.poll(async () => (await native()).bounds).toEqual(display.bounds)
      trial.fullscreenWindow = await native()
      trial.fullscreen = await browse('fullscreen', games[2], label)
      await expect(page.locator('.avalon-controller-status')).toHaveText('Controller disconnected')
      await expect(page.getByRole('group', { name: 'Keyboard guidance', exact: true })).toBeVisible()
      const bumpers = page.locator('[data-root-bumper]')
      await expect(bumpers).toHaveCount(2)
      trial.hints = await bumpers.evaluateAll((nodes) =>
        nodes.map((node) => {
          const r = node.getBoundingClientRect()
          return {
            label: node.getAttribute('data-root-bumper'),
            x: r.x,
            y: r.y,
            width: r.width,
            height: r.height,
            visible:
              r.width > 0 &&
              r.height > 0 &&
              r.left >= 0 &&
              r.top >= 0 &&
              r.right <= innerWidth &&
              r.bottom <= innerHeight,
          }
        }),
      )
      assert(trial.hints.every((hint) => hint.visible))
      trial.disconnected = await uiState()
      assert.deepEqual(trial.disconnected.gamepads, [])
      await page.keyboard.press('F11')
      await ready('desktop')
      await expect.poll(async () => (await native()).normalBounds).toEqual(trial.desktopWindow.normalBounds)
      await expect(
        page
          .getByRole('navigation', { name: 'Main navigation' })
          .getByRole('button', { name: 'Library', exact: true }),
      ).toHaveAttribute('aria-current', 'page')
      await expect(tile(games[1].workId)).toHaveAttribute('data-selected', 'true')
      await tabTo(tile(games[1].workId))
      await expect(tile(games[1].workId)).toBeFocused()
      trial.beforeMinimize = await uiState()
      await page.mouse.move(80, 120)
      await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].minimize())
      await expect.poll(async () => (await native()).minimized).toBe(true)
      await application.evaluate(({ BrowserWindow }) => {
        const window = BrowserWindow.getAllWindows()[0]
        window.restore()
        window.focus()
      })
      await expect.poll(() => page.evaluate(() => document.hasFocus())).toBe(true)
      await expect(tile(games[1].workId)).toBeFocused()
      await expect
        .poll(() => tile(games[1].workId).evaluate((node) => node.matches(':focus-visible')))
        .toBe(true)
      await page.mouse.move(90, 130)
      trial.recovered = { native: await native(), ui: await uiState() }
      assert.notEqual(trial.recovered.ui.active.cursor, 'none')
      assert.notEqual(trial.recovered.ui.controllerCursor, 'true')
      await capture(`${label}-desktop-restored`)
      if (display.id === result.application.primaryDisplayId) {
        await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].maximize())
        await expect.poll(async () => (await native()).maximized).toBe(true)
        const before = await native()
        await page.keyboard.press('F11')
        await ready('fullscreen')
        await route('Library')
        await expect(tile(games[2].workId)).toHaveAttribute('data-selected', 'true')
        await capture(`${label}-fullscreen-library`)
        await page.keyboard.press('F11')
        await ready('desktop')
        await expect.poll(async () => (await native()).maximized).toBe(true)
        const after = await native()
        assert.deepEqual(after.normalBounds, before.normalBounds)
        trial.maximizedRestore = { before, after }
      }
      trial.passed = true
    } catch (error) {
      trial.error = error.stack ?? String(error)
      trial.lastWindow = await native().catch(() => null)
      trial.lastUi = await uiState().catch(() => null)
      await capture(`${label}-failure`).catch(() => {})
    }
    await persist()
  }
  assert(
    result.trials.length > 0 && result.trials.every((trial) => trial.passed),
    'One or more attached-display trials failed.',
  )
  assert.deepEqual(result.pageErrors, [])
  result.passed = true
} catch (error) {
  result.errors.push(error.stack ?? String(error))
  process.exitCode = 1
} finally {
  try {
    endpoint ??= JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
    const address = new URL(endpoint.address)
    assert.equal(address.protocol, 'http:')
    assert.equal(address.hostname, '127.0.0.1')
    assert.equal(address.pathname, '/')
    backendIdentity ??= await identity(endpoint.processId)
    if (backendIdentity) {
      assert.equal(
        backendIdentity.path.toLowerCase(),
        join(installation, 'backend/Winnow.Backend.exe').toLowerCase(),
      )
      const response = await fetch(new URL('/api/v1/lifecycle/shutdown', address), {
        method: 'POST',
        headers: { Authorization: `Bearer ${endpoint.token}` },
        signal: AbortSignal.timeout(5000),
      })
      assert(response.ok)
      result.shutdownStatus = response.status
    }
  } catch (error) {
    if (error.code !== 'ENOENT') result.errors.push(`Backend cleanup: ${error.message}`)
  }
  try {
    if (application) {
      await application.evaluate(({ app }) => {
        app.setJumpList(null)
        const quit = app.quit.bind(app)
        app.quit = () => setImmediate(quit)
      })
      let timeout
      try {
        await Promise.race([
          application.close(),
          new Promise((_, reject) => {
            timeout = setTimeout(() => reject(Error('Frontend close timed out.')), 10000)
          }),
        ])
      } finally {
        clearTimeout(timeout)
      }
    }
    await gone(mainIdentity)
    await gone(backendIdentity)
  } catch (error) {
    result.errors.push(`Owned process cleanup: ${error.message}`)
    await forceOwn(mainIdentity).catch((error) => result.errors.push(error.message))
    await forceOwn(backendIdentity).catch((error) => result.errors.push(error.message))
  }
  try {
    const lock = await open(
      join(dirname(installation), `.${basename(installation)}.winnow-update.lock`),
      'r+',
    )
    await lock.close()
    result.portableLeaseReleased = true
    await gone(mainIdentity)
    await gone(backendIdentity)
    result.closed = true
    assert.deepEqual(await hashes(), result.packageHashes)
    result.packageUnchanged = true
  } catch (error) {
    result.errors.push(`Final verification: ${error.message}`)
  }
  result.passed = result.passed && result.closed && result.errors.length === 0
  if (!result.passed) process.exitCode = 1
  result.finishedAt = new Date().toISOString()
  await persist()
}
console.log(JSON.stringify({ passed: result.passed, closed: result.closed, report: reportPath }))
