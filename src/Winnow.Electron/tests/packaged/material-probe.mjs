import assert from 'node:assert/strict'
import { execFile } from 'node:child_process'
import { createHash } from 'node:crypto'
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { dirname, isAbsolute, join, resolve } from 'node:path'
import { release } from 'node:os'
import { fileURLToPath } from 'node:url'
import { promisify } from 'node:util'
import { _electron as electron, expect } from '@playwright/test'

// Opt-in composed-pixel evidence on the local Windows desktop. No wallpaper,
// display, accessibility or OS transparency preference is changed.
const values = new Map()
const args = process.argv.slice(2)
for (let index = 0; index < args.length; index += 2) {
  const [key, value] = args.slice(index, index + 2)
  assert(['--exe', '--output'].includes(key) && isAbsolute(value ?? '') && !values.has(key))
  values.set(key, value)
}
assert.equal(process.platform, 'win32')
assert.equal(values.size, 2, 'Use --exe <absolute Winnow.exe> --output <new absolute evidence directory>.')
const executable = resolve(values.get('--exe'))
const output = resolve(values.get('--output'))
const installation = dirname(executable)
const directory = join(output, 'disposable-data')
await mkdir(output)
await mkdir(directory)
const reportPath = join(output, 'material-report.json')
const report = {
  startedAt: new Date().toISOString(),
  executable,
  directory,
  passed: false,
  closed: false,
  captures: [],
  errors: [],
  pageErrors: [],
  limitations: [
    'Automated keyboard and pointer input uses the actual packaged Chromium window; no physical controller is tested.',
    'DWM backdrop configuration and renderer alpha are supporting observations; visible materials require inspection of composed screen PNGs.',
    'Only current Windows policy is observed. High contrast, reduced transparency, remote sessions and other OS versions are not forced or physically tested.',
    'Screen capture has foreground and z-order guards before and after, but Windows offers no atomic transaction for these checks.',
    'Mica depends on the current unchanged wallpaper and need not respond to the owned underlay pattern.',
  ],
}
const persist = () => writeFile(reportPath, JSON.stringify(report, null, 2) + '\n')
const run = promisify(execFile)
const ps = async (code) =>
  (
    await run('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', code], {
      windowsHide: true,
      timeout: 10000,
    })
  ).stdout.trim()
async function identity(pid) {
  assert(Number.isSafeInteger(pid) && pid > 0)
  const value = await ps(
    `$p = Get-Process -Id ${pid} -ErrorAction SilentlyContinue; if ($p) { @{ pid=$p.Id; path=$p.Path; startTicks=$p.StartTime.ToUniversalTime().Ticks.ToString() } | ConvertTo-Json -Compress }; exit 0`,
  )
  return value ? JSON.parse(value) : null
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
  report.forcedCleanup ??= []
  report.forcedCleanup.push(actor.pid)
  await gone(actor)
}
const digest = async (path) =>
  createHash('sha256')
    .update(await readFile(path))
    .digest('hex')
const hashes = async () =>
  Object.fromEntries(
    await Promise.all(
      [
        'Winnow.exe',
        'resources/app.asar',
        'backend/Winnow.Backend.exe',
        'backend/Winnow.Backend.dll',
        'update-helper/Winnow.Update.Helper.exe',
        'release-info.json',
      ].map(async (file) => [file, await digest(join(installation, file))]),
    ),
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
let application, page, endpoint, frontend, backend
const ready = async (mode) => {
  await expect(page.locator(`.avalon-shell.${mode}:visible`)).toBeVisible({ timeout: 30000 })
  await expect(page.locator('.startup-presentation')).toHaveCount(0, { timeout: 30000 })
}
const native = () =>
  application.evaluate(({ BrowserWindow }) => {
    const window = BrowserWindow.getAllWindows()[0]
    return {
      handle: window.getNativeWindowHandle().readBigUInt64LE().toString(),
      bounds: window.getBounds(),
      normalBounds: window.getNormalBounds(),
      contentBounds: window.getContentBounds(),
      fullscreen: window.isFullScreen(),
      opacity: window.getOpacity(),
      focused: window.isFocused(),
    }
  })
const appearance = () =>
  page.evaluate(() => {
    const shell = document.querySelector('.avalon-shell:has(.avalon-content)')
    const style = getComputedStyle(shell)
    const cover = document.querySelector('.avalon-cover')
    const rect = (node) => {
      if (!node) return null
      const { x, y, width, height } = node.getBoundingClientRect()
      return { x, y, width, height }
    }
    return {
      material: document.documentElement.dataset.avalonMaterial,
      htmlBackground: getComputedStyle(document.documentElement).backgroundColor,
      shellGround: style.getPropertyValue('--avalon-shell-ground'),
      paneGround: style.getPropertyValue('--avalon-pane-ground'),
      coverBackground: cover ? getComputedStyle(cover).backgroundColor : null,
      regions: {
        rail: rect(document.querySelector('.avalon-rail')),
        content: rect(document.querySelector('.avalon-content')),
        cover: rect(cover),
      },
      viewport: { width: innerWidth, height: innerHeight, dpr: devicePixelRatio },
    }
  })
async function library() {
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
}
async function choose(transparency, backdrop) {
  await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
  const slider = page.getByRole('slider', { name: 'Transparency', exact: true })
  await expect(slider).toBeEnabled()
  await slider.fill(String(transparency))
  await expect(slider).toHaveValue(String(transparency))
  const select = page.getByRole('button', { name: 'Backdrop', exact: true })
  await expect(select).toBeEnabled()
  await select.click()
  await page
    .getByRole('menu', { name: 'Backdrop', exact: true })
    .getByRole('menuitemradio', { name: backdrop === 'mica' ? /^Mica —/ : /^Acrylic —/ })
    .click()
  await expect(select).toHaveJSProperty('value', backdrop)
  const wall = page.getByRole('checkbox', { name: /Include content panes/ })
  await expect(wall).toBeEnabled()
  await wall.check()
  await expect(slider).toBeEnabled()
  await library()
  const expected = transparency > 0 && report.materialEligible ? backdrop : 'none'
  await expect(page.locator('html')).toHaveAttribute('data-avalon-material', expected)
}
async function capture(label) {
  await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].focus())
  await page.mouse.move(80, 80)
  const state = await native()
  assert.equal(state.opacity, 1)
  const rendered = await appearance()
  assert.match(rendered.coverBackground, /^rgb\(/, 'Covers must remain opaque.')
  const record = { label, native: state, appearance: rendered, frames: [] }
  report.captures.push(record)
  for (const pattern of [0, 1]) {
    const path = join(output, `${label}-pattern-${pattern}.png`)
    try {
      await run(
        'powershell.exe',
        [
          '-NoProfile',
          '-NonInteractive',
          '-ExecutionPolicy',
          'Bypass',
          '-File',
          fileURLToPath(new URL('./windows-material-probe.ps1', import.meta.url)),
          '-WindowHandle',
          state.handle,
          '-ProcessId',
          String(frontend.pid),
          '-OutputPath',
          path,
          '-Pattern',
          String(pattern),
          ...(state.fullscreen ? ['-ClientCenter'] : []),
        ],
        { windowsHide: true, timeout: 20000, maxBuffer: 1024 * 1024 },
      )
      record.frames.push(
        JSON.parse((await readFile(path.replace(/\.png$/, '.json'), 'utf8')).replace(/^\uFEFF/, '')),
      )
    } catch (error) {
      record.error = error.stderr ?? error.message
      await persist()
      throw error
    }
  }
  await persist()
}
try {
  report.package = JSON.parse(
    (await readFile(join(installation, 'release-info.json'), 'utf8')).replace(/^\uFEFF/, ''),
  )
  assert.equal(report.package.frontend, 'electron')
  report.hashes = await hashes()
  application = await electron.launch({
    executablePath: executable,
    args: ['--data-dir', directory, '--no-sync'],
    env: environment,
    chromiumSandbox: true,
    timeout: 30000,
  })
  report.application = await application.evaluate(({ app, nativeTheme, screen }) => ({
    pid: process.pid,
    packaged: app.isPackaged,
    version: app.getVersion(),
    executable: process.execPath,
    appPath: app.getAppPath(),
    electron: process.versions.electron,
    chrome: process.versions.chrome,
    nativeTheme: {
      highContrast: nativeTheme.shouldUseHighContrastColors,
      forcedColors: nativeTheme.inForcedColorsMode,
      reducedTransparency: nativeTheme.prefersReducedTransparency,
      darkColors: nativeTheme.shouldUseDarkColors,
    },
    primaryDisplay: {
      id: screen.getPrimaryDisplay().id,
      bounds: screen.getPrimaryDisplay().bounds,
      workArea: screen.getPrimaryDisplay().workArea,
      scaleFactor: screen.getPrimaryDisplay().scaleFactor,
    },
  }))
  assert.equal(report.application.packaged, true)
  assert.equal(report.application.executable.toLowerCase(), executable.toLowerCase())
  assert.equal(report.application.version, report.package.version)
  frontend = await identity(report.application.pid)
  report.windows = { release: release(), session: process.env.SESSIONNAME ?? null }
  const [major, , build] = release().split('.').map(Number)
  const flags = report.application.nativeTheme
  report.materialEligible =
    major >= 10 &&
    build >= 22621 &&
    !flags.highContrast &&
    !flags.forcedColors &&
    !flags.reducedTransparency &&
    !process.env.SESSIONNAME?.toUpperCase().startsWith('RDP-')
  page = await application.firstWindow()
  page.on('pageerror', (error) => report.pageErrors.push(error.message))
  await ready('desktop')
  await expect
    .poll(() => page.evaluate(() => window.winnow.connection()), { timeout: 30000 })
    .toMatchObject({ connected: true })
  endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
  backend = await identity(endpoint.processId)
  assert.equal(backend.path.toLowerCase(), join(installation, 'backend/Winnow.Backend.exe').toLowerCase())
  report.processes = { frontend, backend }
  const skip = page.getByRole('button', { name: 'Skip setup', exact: true })
  if (await skip.isVisible()) await skip.click()
  for (const title of ['Material Alpha', 'Material Beta', 'Material Gamma']) {
    await page.evaluate(async (title) => {
      const result = await window.winnow.request({
        route: 'manual.create',
        body: {
          title,
          platformLabel: 'PC',
          executablePath: null,
          installPath: null,
          igdbId: null,
          steamAppId: null,
        },
      })
      if (!result.ok) throw Error(`manual.create: ${result.status}`)
    }, title)
  }
  await application.evaluate(({ BrowserWindow, screen }) => {
    const window = BrowserWindow.getAllWindows()[0],
      area = screen.getPrimaryDisplay().workArea
    window.setFullScreen(false)
    window.unmaximize()
    window.setBounds({
      x: area.x + 100,
      y: area.y + 100,
      width: Math.min(1440, area.width - 200),
      height: Math.min(980, area.height - 200),
    })
    window.show()
    window.focus()
  })
  for (const [label, transparency, backdrop] of [
    ['desktop-solid', 0, 'acrylic'],
    ['desktop-acrylic-30', 30, 'acrylic'],
    ['desktop-acrylic-100', 100, 'acrylic'],
    ['desktop-mica-100', 100, 'mica'],
  ]) {
    await choose(transparency, backdrop)
    await capture(label)
  }
  const desktop = await native()
  await page.keyboard.press('F11')
  await ready('fullscreen')
  await expect.poll(async () => (await native()).fullscreen).toBe(true)
  await expect.poll(async () => (await native()).bounds).toEqual(report.application.primaryDisplay.bounds)
  await library()
  await expect(page.locator('html')).toHaveAttribute('data-avalon-material', 'none')
  assert.match((await appearance()).paneGround, /FF$/i)
  await capture('fullscreen-solid')
  await page.keyboard.press('F11')
  await ready('desktop')
  await expect.poll(async () => (await native()).normalBounds).toEqual(desktop.normalBounds)
  await expect(page.locator('html')).toHaveAttribute(
    'data-avalon-material',
    report.materialEligible ? 'mica' : 'none',
  )
  await capture('desktop-restored-mica-100')
  await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
  await expect(page.getByRole('slider', { name: 'Transparency', exact: true })).toHaveValue('100')
  await expect(page.getByRole('button', { name: 'Backdrop', exact: true })).toHaveJSProperty('value', 'mica')
  assert.deepEqual(report.pageErrors, [])
  report.passed = true
} catch (error) {
  report.errors.push(error.stack ?? String(error))
  process.exitCode = 1
} finally {
  try {
    endpoint ??= JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
    backend ??= await identity(endpoint.processId)
    assert.equal(backend.path.toLowerCase(), join(installation, 'backend/Winnow.Backend.exe').toLowerCase())
    const address = new URL(endpoint.address)
    assert.equal(address.protocol, 'http:')
    assert.equal(address.hostname, '127.0.0.1')
    assert.equal(address.pathname, '/')
    const response = await fetch(new URL('/api/v1/lifecycle/shutdown', address), {
      method: 'POST',
      headers: { Authorization: `Bearer ${endpoint.token}` },
      signal: AbortSignal.timeout(5000),
    })
    assert(response.ok)
    report.shutdownStatus = response.status
  } catch (error) {
    if (error.code !== 'ENOENT') report.errors.push(`Backend cleanup: ${error.message}`)
  }
  try {
    if (application) {
      await application.evaluate(({ app }) => {
        app.setJumpList(null)
        const quit = app.quit.bind(app)
        app.quit = () => setImmediate(quit)
      })
      let timer
      try {
        await Promise.race([
          application.close(),
          new Promise((_, reject) => {
            timer = setTimeout(() => reject(Error('Frontend close timed out.')), 10000)
          }),
        ])
      } finally {
        clearTimeout(timer)
      }
    }
    await gone(frontend)
    await gone(backend)
  } catch (error) {
    report.errors.push(`Owned cleanup: ${error.message}`)
    await forceOwn(frontend).catch((error) => report.errors.push(error.message))
    await forceOwn(backend).catch((error) => report.errors.push(error.message))
  }
  try {
    await gone(frontend)
    await gone(backend)
    report.closed = true
    assert.deepEqual(await hashes(), report.hashes)
    report.packageUnchanged = true
  } catch (error) {
    report.errors.push(`Final verification: ${error.message}`)
  }
  report.passed = report.passed && report.closed && report.errors.length === 0
  if (!report.passed) process.exitCode = 1
  report.finishedAt = new Date().toISOString()
  await persist()
}
console.log(JSON.stringify({ passed: report.passed, closed: report.closed, report: reportPath }))
