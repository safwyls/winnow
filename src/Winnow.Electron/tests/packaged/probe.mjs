import assert from 'node:assert/strict'
import { execFile, spawn } from 'node:child_process'
import { createHash } from 'node:crypto'
import { access, mkdir, open, readFile, readdir, readlink, realpath, writeFile } from 'node:fs/promises'
import { basename, dirname, isAbsolute, join, resolve } from 'node:path'
import { promisify } from 'node:util'
import { _electron as electron, expect } from '@playwright/test'
import { inspectLinuxCommandLine } from './linux-command-line.mjs'

const args = process.argv.slice(2),
  values = new Map()
for (let index = 0; index < args.length; index += 2) {
  const key = args[index],
    value = args[index + 1]
  assert(
    ['--exe', '--data-dir', '--report', '--mode'].includes(key) && value && !values.has(key),
    'Expected unique --exe, --data-dir, --report and --mode arguments.',
  )
  values.set(key, value)
}
for (const key of ['--exe', '--data-dir', '--report'])
  assert(isAbsolute(values.get(key) ?? ''), `${key} must be absolute.`)
const executable = resolve(values.get('--exe')),
  directory = resolve(values.get('--data-dir')),
  reportPath = resolve(values.get('--report'))
const mode = values.get('--mode')
assert(mode === 'desktop' || mode === 'fullscreen', '--mode must be desktop or fullscreen.')
assert(['win32', 'linux'].includes(process.platform), 'The primary probe supports Windows and Linux.')
const root = dirname(executable),
  extension = process.platform === 'win32' ? '.exe' : ''
const environment = Object.fromEntries(
  Object.entries(process.env).filter(
    ([key, value]) =>
      value !== undefined &&
      !/^Igdb__(ClientId|ClientSecret)$/i.test(key) &&
      ![
        'ELECTRON_RUN_AS_NODE',
        'ELECTRON_RENDERER_URL',
        'WINNOW_BACKEND_PATH',
        'WINNOW_ACTIVATION_HELPER_PATH',
        'WINNOW_UPDATE_HELPER_PATH',
        'WINNOW_ELECTRON_FIXTURE_PATH',
        'WINNOW_APPIMAGE_UPDATED',
        'APPIMAGE',
        'APPDIR',
      ].includes(key),
  ),
)
// Match the integration runner: blank overrides also suppress configured fallback credentials.
Object.assign(environment, { Igdb__ClientId: '', Igdb__ClientSecret: '' })
const result = {
  mode,
  executable,
  directory,
  stage: 'package',
  passed: false,
  errors: [],
  pageErrors: [],
  processId: null,
  backendProcessId: null,
  identity: null,
  package: null,
  activation: null,
  closed: false,
  platform: process.platform,
}
await mkdir(dirname(reportPath), { recursive: true })
const persist = () => writeFile(reportPath, JSON.stringify(result, null, 2))
const digest = (bytes) => createHash('sha256').update(bytes).digest('hex')
const samePath = (actual, expected) =>
  process.platform === 'win32'
    ? resolve(actual).toLowerCase() === resolve(expected).toLowerCase()
    : resolve(actual) === resolve(expected)
async function exists(path) {
  try {
    await access(path)
    return true
  } catch (error) {
    if (error.code === 'ENOENT') return false
    throw error
  }
}
async function linuxProcess(pid) {
  const base = `/proc/${pid}`
  const stat = await readFile(join(base, 'stat'), 'utf8')
  // comm may contain spaces or parentheses; fields after the last ')' begin at state (3).
  const fields = stat.slice(stat.lastIndexOf(')') + 2).split(' ')
  const status = await readFile(join(base, 'status'), 'utf8')
  const commandLine = inspectLinuxCommandLine(await readFile(join(base, 'cmdline'), 'utf8'))
  let executable = null
  try {
    executable = await readlink(join(base, 'exe'))
  } catch (error) {
    // Chromium may deny ptrace-style reads of a sandboxed child's exe link.
    if (!['EACCES', 'EPERM'].includes(error.code)) throw error
  }
  return {
    pid,
    parentPid: Number(fields[1]),
    startTicks: fields[19],
    state: fields[0],
    executable,
    command: commandLine.command,
    commandLineRepresentation: commandLine.representation,
    type: commandLine.type,
    disabledSandboxArguments: commandLine.disabledSandboxArguments,
    noNewPrivileges: Number(status.match(/^NoNewPrivs:\s+(\d+)/m)?.[1]),
    seccomp: Number(status.match(/^Seccomp:\s+(\d+)/m)?.[1]),
  }
}
async function linuxDescendants(parentPid) {
  const children = []
  const pending = [parentPid]
  while (pending.length) {
    const parent = pending.shift()
    let pids
    try {
      pids = (await readFile(`/proc/${parent}/task/${parent}/children`, 'utf8'))
        .trim()
        .split(/\s+/)
        .filter(Boolean)
    } catch (error) {
      if (error.code === 'ENOENT' || error.code === 'ESRCH') continue
      throw error
    }
    for (const value of pids) {
      try {
        const child = await linuxProcess(Number(value))
        children.push(child)
        pending.push(child.pid)
      } catch (error) {
        if (error.code !== 'ENOENT' && error.code !== 'ESRCH') throw error
      }
    }
  }
  return children
}
async function linuxLeaseAvailable(path) {
  try {
    // Unlike open(), flock observes the advisory shared lease held by .NET on Linux.
    await promisify(execFile)(
      '/usr/bin/flock',
      ['--exclusive', '--nonblock', '--conflict-exit-code', '73', path, '/usr/bin/true'],
      { timeout: 5000 },
    )
    return true
  } catch (error) {
    if (error.code === 73) return false
    throw error
  }
}
async function forceOwnedLinuxCleanup() {
  result.forcedCleanup = []
  for (const actor of [...ownedLinux.values()].reverse()) {
    try {
      const current = await linuxProcess(actor.pid)
      if (current.startTicks !== actor.startTicks || ['Z', 'X'].includes(current.state)) continue
      process.kill(actor.pid, 'SIGKILL')
      result.forcedCleanup.push(actor.pid)
      await waitGone(actor.pid, actor.startTicks)
    } catch (error) {
      if (error.code !== 'ENOENT' && error.code !== 'ESRCH')
        result.errors.push(`Cleanup PID ${actor.pid}: ${error.message}`)
    }
  }
}
async function protocolSnapshot() {
  if (process.platform !== 'win32') return null
  try {
    return digest(
      (
        await promisify(execFile)('reg.exe', ['query', 'HKCU\\Software\\Classes\\winnow', '/s'], {
          windowsHide: true,
        })
      ).stdout,
    )
  } catch (error) {
    if (error.code !== 1) throw error
    return digest(`${error.code}:${error.stdout}:${error.stderr}`)
  }
}
const beforeProtocol = await protocolSnapshot()
let application, page, endpoint, portableLock, managedLock
const ownedLinux = new Map()
async function waitGone(pid, startTicks) {
  await expect
    .poll(
      async () => {
        try {
          if (process.platform === 'linux') {
            const stat = await readFile(`/proc/${pid}/stat`, 'utf8')
            const fields = stat.slice(stat.lastIndexOf(')') + 2).split(' ')
            // An unreaped orphan has exited and holds no resources. Do not signal a reused PID.
            return (startTicks && fields[19] !== startTicks) || ['Z', 'X'].includes(fields[0])
          }
          process.kill(pid, 0)
          return false
        } catch (error) {
          if (error.code === 'ESRCH' || error.code === 'ENOENT') return true
          throw error
        }
      },
      { timeout: 10000 },
    )
    .toBe(true)
}
try {
  const manifest = JSON.parse(
    (await readFile(join(root, 'release-info.json'), 'utf8')).replace(/^\uFEFF/, ''),
  )
  assert.equal(manifest.frontend, 'electron')
  assert.equal(manifest.runtime, process.platform === 'win32' ? 'win-x64' : 'linux-x64')
  assert.match(manifest.commit, /^[a-f0-9]{40}$/)
  const required = [
    'resources/app.asar',
    'resources/icon.ico',
    'resources/THIRD-PARTY-NOTICES.md',
    'resources/DOTNET-NOTICES.md',
    'LICENSE.electron.txt',
    'LICENSES.chromium.html',
    'plugins/steamgriddb/plugin.json',
    'plugins/steamgriddb/Winnow.Plugin.SteamGridDb.dll',
    `backend/Winnow.Backend${extension}`,
    'backend/Winnow.Backend.runtimeconfig.json',
    `update-helper/Winnow.Update.Helper${extension}`,
    'update-helper/Winnow.Update.Helper.runtimeconfig.json',
  ]
  if (process.platform === 'linux')
    for (const directory of ['backend', 'update-helper'])
      for (const library of ['libcoreclr.so', 'libhostfxr.so', 'libhostpolicy.so'])
        required.push(`${directory}/${library}`)
  for (const file of required) await access(join(root, file))
  const runtime = JSON.parse(await readFile(join(root, 'backend/Winnow.Backend.runtimeconfig.json'), 'utf8'))
  assert(
    runtime.runtimeOptions.includedFrameworks.some(
      (framework) => framework.name === 'Microsoft.AspNetCore.App',
    ),
    'Backend must include its ASP.NET runtime.',
  )
  const files = await readdir(root, { recursive: true })
  assert(
    !files.some((file) => /(?:^|[\\/])Avalonia[^\\/]*\.dll$/i.test(file)),
    'Primary package must not contain an Avalonia runtime.',
  )
  const asarHash = digest(await readFile(join(root, 'resources/app.asar')))
  result.package = { manifest, asarHash, required }
  if (process.platform === 'linux') {
    assert.notEqual(process.getuid(), 0, 'Run the Linux package probe as a non-root desktop user.')
    const managed = await exists(join(root, 'package-managed'))
    if (root === '/opt/winnow') assert(managed, 'The Debian installation must have its managed marker.')
    result.package.managed = managed
    if (managed) {
      managedLock = join(dirname(root), `.${basename(root)}.winnow-update.lock`)
      assert.equal(
        await exists(managedLock),
        false,
        'Managed startup must not create an adjacent portable lease.',
      )
    }
  }
  result.stage = 'launch'
  await persist()
  application = await electron.launch({
    executablePath: executable,
    args: [
      '--data-dir',
      directory,
      '--no-sync',
      ...(mode === 'fullscreen' ? ['--jump-list-fullscreen'] : []),
    ],
    env: environment,
    chromiumSandbox: true,
    timeout: 30000,
  })
  result.processId = application.process().pid
  result.identity = await application.evaluate(({ app, BrowserWindow }) => ({
    packaged: app.isPackaged,
    name: app.getName(),
    version: app.getVersion(),
    appPath: app.getAppPath(),
    executable: process.execPath,
    electron: process.versions.electron,
    chrome: process.versions.chrome,
    pid: process.pid,
    nativeReady: app.isReady(),
    windows: BrowserWindow.getAllWindows().length,
  }))
  result.processId = result.identity.pid
  assert.equal(result.identity.packaged, true)
  assert.equal(result.identity.name, 'Winnow')
  assert.equal(result.identity.version, manifest.version)
  assert(samePath(result.identity.executable, executable))
  assert(samePath(result.identity.appPath, join(root, 'resources/app.asar')))
  assert(result.identity.electron && result.identity.chrome && result.identity.nativeReady)
  if (process.platform === 'linux') {
    const main = await linuxProcess(result.processId)
    ownedLinux.set(main.pid, main)
    assert.equal(main.executable, await realpath(executable))
    assert.deepEqual(main.disabledSandboxArguments, [])
    result.linuxMain = main
    const expectedProfile = result.package.managed
      ? 'winnow-electron'
      : `winnow-portable-${digest(await realpath(executable)).slice(0, 16)}`
    const profile = (await readFile(`/proc/${main.pid}/attr/current`, 'utf8')).trim()
    result.appArmor = { expectedProfile, actual: profile }
    assert.equal(
      profile,
      `${expectedProfile} (unconfined)`,
      'The exact executable AppArmor profile must be active.',
    )
  }
  page = await application.firstWindow()
  page.on('pageerror', (error) => result.pageErrors.push(error.message))
  result.stage = 'renderer'
  await persist()
  await expect(page.locator('.avalon-shell')).toBeVisible({ timeout: 30000 })
  await expect(page.locator('.startup-presentation')).toHaveCount(0, { timeout: 30000 })
  await page.evaluate((fullscreen) => window.winnow.setFullscreen(fullscreen), mode === 'fullscreen')
  await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
  await expect(page.locator('.startup-presentation')).toHaveCount(0, { timeout: 30000 })
  await expect
    .poll(() => application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].isFullScreen()))
    .toBe(mode === 'fullscreen')
  if (mode === 'fullscreen')
    await expect
      .poll(() =>
        application.evaluate(({ BrowserWindow, screen }) => {
          const owner = BrowserWindow.getAllWindows()[0]
          return { window: owner.getBounds(), display: screen.getDisplayMatching(owner.getBounds()).bounds }
        }),
      )
      .toEqual(
        await application.evaluate(({ BrowserWindow, screen }) => {
          const display = screen.getDisplayMatching(BrowserWindow.getAllWindows()[0].getBounds()).bounds
          return { window: display, display }
        }),
      )
  result.nativeWindow = await application.evaluate(({ BrowserWindow, screen }) => {
    const owner = BrowserWindow.getAllWindows()[0]
    return {
      visible: owner.isVisible(),
      fullscreen: owner.isFullScreen(),
      bounds: owner.getBounds(),
      contentBounds: owner.getContentBounds(),
      displayBounds: screen.getDisplayMatching(owner.getBounds()).bounds,
      nativeHandlePresent: owner.getNativeWindowHandle().some((byte) => byte !== 0),
      xWindowId: process.platform === 'linux' ? owner.getNativeWindowHandle().readUInt32LE(0) : null,
    }
  })
  assert.equal(result.nativeWindow.visible, true)
  assert.equal(result.nativeWindow.nativeHandlePresent, true)
  if (process.platform === 'linux') {
    const properties = (
      await promisify(execFile)(
        '/usr/bin/xprop',
        ['-id', `0x${result.nativeWindow.xWindowId.toString(16)}`, 'WM_CLASS', '_NET_WM_PID'],
        { timeout: 5000 },
      )
    ).stdout
    const wmClass = [...(properties.match(/^WM_CLASS\([^\n]+/m)?.[0] ?? '').matchAll(/"([^"\n]+)"/g)].map(
      (match) => match[1],
    )
    assert.equal(wmClass.length, 2, 'The native X11 window must expose instance and class identity.')
    assert.equal(Number(properties.match(/^_NET_WM_PID\([^\n]+?=\s*(\d+)/m)?.[1]), result.processId)
    result.linux = { wmClass, xWindowId: result.nativeWindow.xWindowId }
  }
  await expect
    .poll(() => page.evaluate(() => window.winnow.connection()), { timeout: 30000 })
    .toMatchObject({ connected: true })
  const info = await page.evaluate(() => window.winnow.applicationInfo())
  assert.equal(info.packaged, true)
  assert.equal(info.version, manifest.version)
  assert.equal(info.commit, manifest.commit)
  const library = await page.evaluate(() => window.winnow.request({ route: 'library.get' }))
  assert.equal(library.ok, true)
  result.libraryReadSucceeded = true
  endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
  result.backendProcessId = endpoint.processId
  assert(
    Number.isSafeInteger(endpoint.processId) &&
      endpoint.processId > 0 &&
      endpoint.processId !== result.processId,
  )
  const address = new URL(endpoint.address)
  assert.equal(address.protocol, 'http:')
  assert.equal(address.hostname, '127.0.0.1')
  assert.equal(address.pathname, '/')
  if (process.platform === 'win32') {
    const processPath = (
      await promisify(execFile)(
        'powershell.exe',
        [
          '-NoProfile',
          '-NonInteractive',
          '-Command',
          `(Get-CimInstance Win32_Process -Filter 'ProcessId = ${endpoint.processId}').ExecutablePath`,
        ],
        { windowsHide: true },
      )
    ).stdout.trim()
    assert.equal(processPath.toLowerCase(), join(root, 'backend/Winnow.Backend.exe').toLowerCase())
    result.backendExecutable = processPath
  } else {
    const backend = await linuxProcess(endpoint.processId)
    ownedLinux.set(backend.pid, backend)
    assert.equal(backend.executable, await realpath(join(root, 'backend/Winnow.Backend')))
    result.backendExecutable = backend.executable
    result.linuxBackend = backend
    // Pinned Electron exposes this main-side inspection method internally. Pair its
    // effective preferences with kernel sandbox evidence and the actual page globals.
    const preferences = await application.evaluate(({ BrowserWindow }) => {
      const preferences = BrowserWindow.getAllWindows()[0].webContents.getLastWebPreferences()
      return {
        sandbox: preferences.sandbox,
        contextIsolation: preferences.contextIsolation,
        nodeIntegration: preferences.nodeIntegration,
        nodeIntegrationInWorker: preferences.nodeIntegrationInWorker,
        webSecurity: preferences.webSecurity,
      }
    })
    assert.deepEqual(preferences, {
      sandbox: true,
      contextIsolation: true,
      nodeIntegration: false,
      nodeIntegrationInWorker: false,
      webSecurity: true,
    })
    const rendererPid = await application.evaluate(({ BrowserWindow }) =>
      BrowserWindow.getAllWindows()[0].webContents.getOSProcessId(),
    )
    // getOSProcessId identifies the host PID even when Chromium uses a PID namespace.
    const renderer = await linuxProcess(rendererPid)
    ownedLinux.set(renderer.pid, renderer)
    assert.equal(renderer.type, 'renderer')
    assert.equal(renderer.noNewPrivileges, 1)
    assert.equal(renderer.seccomp, 2)
    assert.deepEqual(renderer.disabledSandboxArguments, [])
    if (renderer.executable) assert.equal(renderer.executable, await realpath(executable))
    const pageGlobals = await page.evaluate(() => ({
      nodeRequire: typeof globalThis.require,
      nodeProcess: typeof globalThis.process,
      nodeBuffer: typeof globalThis.Buffer,
      bridge: typeof window.winnow.request,
    }))
    assert.deepEqual(pageGlobals, {
      nodeRequire: 'undefined',
      nodeProcess: 'undefined',
      nodeBuffer: 'undefined',
      bridge: 'function',
    })
    result.rendererSandbox = {
      preferences,
      process: renderer,
      pageGlobals,
      nodeIntegrationExposed: false,
      preferenceInspection: 'Pinned Electron webContents.getLastWebPreferences (internal inspection only)',
    }
    const children = await linuxDescendants(result.processId)
    children.forEach((child) => ownedLinux.set(child.pid, child))
    const helper = await realpath(join(root, 'update-helper/Winnow.Update.Helper'))
    const helpers = children.filter((child) => child.executable === helper || child.command === helper)
    if (result.package.managed) {
      assert.deepEqual(helpers, [], 'A managed frontend must not launch the portable installation guard.')
      assert.equal(await exists(managedLock), false)
      const before = await page.evaluate(() => window.winnow.updateSnapshot())
      assert.equal(before.canDownload, false)
      assert.equal(before.canRestart, false)
      const ciVersion = /-(?:dev|ci)(?:\.|$)/i.test(manifest.version)
      const checked = ciVersion ? await page.evaluate(() => window.winnow.updateAction('check')) : null
      if (checked) assert.equal(checked.status, 'Development and CI builds do not receive release updates.')
      const downloaded = await page.evaluate(() => window.winnow.updateAction('download'))
      const restarted = await page.evaluate(() => window.winnow.updateAction('restart'))
      for (const snapshot of [downloaded, restarted]) {
        assert.equal(snapshot.canDownload, false)
        assert.equal(snapshot.canRestart, false)
        assert.equal(snapshot.busy, false)
      }
      const afterChildren = await linuxDescendants(result.processId)
      afterChildren.forEach((child) => ownedLinux.set(child.pid, child))
      assert.equal(
        afterChildren.some((child) => child.executable === helper || child.command === helper),
        false,
      )
      assert.equal(await exists(managedLock), false)
      result.managedUpdate = {
        marker: true,
        before,
        checked,
        downloaded,
        restarted,
        portableHelperAbsent: true,
        adjacentLeaseAbsent: true,
        limitation: ciVersion
          ? 'The CI version refuses release discovery before asset selection. Managed asset selection is verified by policy tests.'
          : 'No release discovery is forced. Disabled download/restart actions and the managed installation boundary are observed.',
      }
    } else {
      assert.equal(helpers.length, 1, 'A portable frontend must own one real installation-guard helper.')
      portableLock = join(dirname(root), `.${basename(root)}.winnow-update.lock`)
      assert.equal(await exists(portableLock), true)
      assert.equal(
        await linuxLeaseAvailable(portableLock),
        false,
        'The live portable guard must exclude an exclusive flock.',
      )
      result.linuxPortableGuard = { process: helpers[0], exclusiveFlockDenied: true }
    }
  }
  let installed = false
  try {
    await access(join(root, 'unins000.exe'))
    installed = true
  } catch {}
  if (!installed && process.platform === 'win32') {
    const lock = join(dirname(root), `.${basename(root)}.winnow-update.lock`)
    portableLock = lock
    let writable = false
    try {
      const file = await open(lock, 'r+')
      await file.close()
      writable = true
    } catch (error) {
      assert.match(error.code, /EACCES|EPERM|EBUSY/)
    }
    assert.equal(writable, false, 'Real primary portable frontend must hold its replacement exclusion lease.')
    result.portableLeaseDeniedWrite = true
  }
  const managedLauncher = process.platform === 'linux' && result.package.managed
  const activationExecutable = managedLauncher ? '/usr/bin/winnow' : executable
  if (managedLauncher) await access(activationExecutable)
  await application.evaluate(({ BrowserWindow }, interceptPlugin) => {
    const owner = BrowserWindow.getAllWindows()[0]
    globalThis.__packageProbeActivations = []
    globalThis.__packageProbeInterceptedPlugins = []
    const send = owner.webContents.send.bind(owner.webContents)
    owner.webContents.send = (channel, ...args) => {
      if (channel === 'winnow:activation') {
        globalThis.__packageProbeActivations.push(args[0])
        // Exercise the shipped parser and owner IPC without asking the renderer to install a plugin.
        if (interceptPlugin && args[0].kind === 'plugin') {
          globalThis.__packageProbeInterceptedPlugins.push(args[0])
          return
        }
      }
      return send(channel, ...args)
    }
    owner.hide()
  }, managedLauncher)
  const secondaryArgs = [
    '--data-dir',
    directory,
    ...(mode === 'fullscreen' ? ['--jump-list-fullscreen'] : []),
  ]
  const launchSecondary = (args) => {
    const child = spawn(activationExecutable, args, { env: environment, windowsHide: true, stdio: 'ignore' })
    return new Promise((done, failed) => {
      const timer = setTimeout(() => {
        child.kill()
        failed(Error('Secondary packaged activation timed out.'))
      }, 15000)
      child.once('error', (error) => {
        clearTimeout(timer)
        failed(error)
      })
      child.once('exit', (code) => {
        clearTimeout(timer)
        done(code)
      })
    })
  }
  const code = await launchSecondary(secondaryArgs)
  assert.equal(code, 0)
  const expectedActivation = { kind: mode === 'fullscreen' ? 'fullscreen' : 'show' }
  await expect
    .poll(() => application.evaluate(() => globalThis.__packageProbeActivations))
    .toEqual([expectedActivation])
  assert.equal(
    await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].isVisible()),
    true,
  )
  const current = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
  assert.equal(current.processId, endpoint.processId)
  assert.equal(current.epoch, endpoint.epoch)
  result.activation = { expected: expectedActivation, secondaryExitCode: code, sameBackend: true }
  if (managedLauncher) {
    const uri = 'winnow://plugins/install?id=psn&release=v0.2.0'
    const pluginActivation = { kind: 'plugin', pluginId: 'psn', releaseTag: 'v0.2.0' }
    const pluginCode = await launchSecondary(['--data-dir', directory, uri])
    assert.equal(pluginCode, 0)
    await expect
      .poll(() => application.evaluate(() => globalThis.__packageProbeActivations))
      .toEqual([expectedActivation, pluginActivation])
    assert.deepEqual(await application.evaluate(() => globalThis.__packageProbeInterceptedPlugins), [
      pluginActivation,
    ])
    const afterPlugin = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
    assert.equal(afterPlugin.processId, endpoint.processId)
    assert.equal(afterPlugin.epoch, endpoint.epoch)
    result.managedLauncher = {
      path: activationExecutable,
      activation: expectedActivation,
      plugin: {
        uri,
        expected: pluginActivation,
        secondaryExitCode: pluginCode,
        sameBackend: true,
        rendererDeliveryIntercepted: true,
      },
      limitation:
        'The positional URI reaches the real parser and owner IPC. Only its plugin delivery to the renderer is intercepted, preventing installation and associated network requests.',
    }
  }
  await expect
    .poll(() => application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].isFullScreen()))
    .toBe(mode === 'fullscreen')
  await expect(page.locator('.startup-presentation')).toHaveCount(0, { timeout: 30000 })
  result.presentationReadyBeforeCapture = true
  await page.screenshot({ path: reportPath.replace(/\.json$/i, '') + '.png' })
  assert.deepEqual(result.pageErrors, [])
  result.passed = true
  result.stage = 'verified'
} catch (error) {
  result.errors.push(error.stack ?? String(error))
  process.exitCode = 1
  await page?.screenshot({ path: reportPath.replace(/\.json$/i, '') + '-failure.png' }).catch(() => {})
} finally {
  try {
    if (process.platform === 'linux' && result.processId) {
      const children = await linuxDescendants(result.processId)
      children.forEach((child) => ownedLinux.set(child.pid, child))
      result.ownedLinuxProcesses = [...ownedLinux.values()]
    }
    if (!endpoint) {
      try {
        endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
      } catch (error) {
        if (error.code !== 'ENOENT') throw error
      }
    }
    if (endpoint) {
      const ownedAddress = new URL(endpoint.address)
      assert.equal(ownedAddress.protocol, 'http:')
      assert.equal(ownedAddress.hostname, '127.0.0.1')
      assert.equal(ownedAddress.pathname, '/')
      assert(Number.isSafeInteger(endpoint.processId) && endpoint.processId > 0)
      result.backendProcessId = endpoint.processId
      if (process.platform === 'linux') {
        const backend = await linuxProcess(endpoint.processId)
        assert.equal(backend.executable, await realpath(join(root, 'backend/Winnow.Backend')))
        ownedLinux.set(backend.pid, backend)
      }
      const response = await fetch(new URL('/api/v1/lifecycle/shutdown', endpoint.address), {
        method: 'POST',
        headers: { Authorization: `Bearer ${endpoint.token}` },
        signal: AbortSignal.timeout(5000),
      })
      assert(response.ok, 'Owned backend shutdown must succeed.')
      result.backendShutdownStatus = response.status
    }
    if (application) {
      await application.evaluate(({ app }) => {
        if (process.platform === 'win32') app.setJumpList(null)
        const quit = app.quit.bind(app)
        app.quit = () => setImmediate(quit)
      })
      let closeTimer
      try {
        await Promise.race([
          application.close(),
          new Promise((_, reject) => {
            closeTimer = setTimeout(
              () => reject(Error('Owned packaged frontend did not close within 10 seconds.')),
              10000,
            )
          }),
        ])
      } finally {
        clearTimeout(closeTimer)
      }
    }
    if (result.backendProcessId)
      await waitGone(result.backendProcessId, ownedLinux.get(result.backendProcessId)?.startTicks)
    if (result.processId) await waitGone(result.processId, ownedLinux.get(result.processId)?.startTicks)
    if (process.platform === 'linux') {
      await Promise.all([...ownedLinux.values()].map((actor) => waitGone(actor.pid, actor.startTicks)))
      result.linuxProcessesTerminated = true
      result.cleanupMeaning =
        'All observed owned processes exited; an unreaped zombie is terminated, not a running process.'
    }
    if (portableLock) {
      await expect
        .poll(
          async () => {
            if (process.platform === 'linux') return linuxLeaseAvailable(portableLock)
            try {
              const file = await open(portableLock, 'r+')
              await file.close()
              return true
            } catch (error) {
              if (/EACCES|EPERM|EBUSY/.test(error.code)) return false
              throw error
            }
          },
          { timeout: 10000 },
        )
        .toBe(true)
      result.portableLeaseReleased = true
    }
    if (managedLock) assert.equal(await exists(managedLock), false)
    assert.equal(
      await protocolSnapshot(),
      beforeProtocol,
      'Explicit data root must preserve the global winnow protocol association.',
    )
    result.closed = true
  } catch (error) {
    result.errors.push(error.stack ?? String(error))
    result.passed = false
    process.exitCode = 1
    if (result.processId && process.platform === 'win32')
      await promisify(execFile)('taskkill.exe', ['/pid', String(result.processId), '/T', '/F'], {
        windowsHide: true,
        timeout: 5000,
      }).catch(() => {})
    if (process.platform === 'linux') await forceOwnedLinuxCleanup()
  }
  await persist()
}
console.log(JSON.stringify({ passed: result.passed, closed: result.closed, report: reportPath }))
