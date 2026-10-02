import assert from 'node:assert/strict'
import { execFile, spawn } from 'node:child_process'
import { createHash } from 'node:crypto'
import { access, mkdir, open, readFile, readdir, writeFile } from 'node:fs/promises'
import { basename, dirname, isAbsolute, join, resolve } from 'node:path'
import { promisify } from 'node:util'
import { _electron as electron, expect } from '@playwright/test'

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
}
await mkdir(dirname(reportPath), { recursive: true })
const persist = () => writeFile(reportPath, JSON.stringify(result, null, 2))
const digest = (bytes) => createHash('sha256').update(bytes).digest('hex')
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
let application, page, endpoint, portableLock
async function waitGone(pid) {
  await expect
    .poll(
      () => {
        try {
          process.kill(pid, 0)
          return false
        } catch (error) {
          if (error.code === 'ESRCH') return true
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
  assert.equal(result.identity.executable.toLowerCase(), executable.toLowerCase())
  assert.equal(result.identity.appPath.toLowerCase(), join(root, 'resources/app.asar').toLowerCase())
  assert(result.identity.electron && result.identity.chrome && result.identity.nativeReady)
  page = await application.firstWindow()
  page.on('pageerror', (error) => result.pageErrors.push(error.message))
  result.stage = 'renderer'
  await persist()
  await expect(page.locator('.avalon-shell')).toBeVisible({ timeout: 30000 })
  await expect(page.locator('.startup-presentation')).toHaveCount(0, { timeout: 30000 })
  await page.evaluate((fullscreen) => window.winnow.setFullscreen(fullscreen), mode === 'fullscreen')
  await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
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
  await application.evaluate(({ BrowserWindow }) => {
    const owner = BrowserWindow.getAllWindows()[0]
    globalThis.__packageProbeActivations = []
    const send = owner.webContents.send.bind(owner.webContents)
    owner.webContents.send = (channel, ...args) => {
      if (channel === 'winnow:activation') globalThis.__packageProbeActivations.push(args[0])
      return send(channel, ...args)
    }
    owner.hide()
  })
  const secondaryArgs = [
    '--data-dir',
    directory,
    ...(mode === 'fullscreen' ? ['--jump-list-fullscreen'] : []),
  ]
  const child = spawn(executable, secondaryArgs, { env: environment, windowsHide: true, stdio: 'ignore' })
  const code = await new Promise((done, failed) => {
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
      const response = await fetch(new URL('/api/v1/lifecycle/shutdown', endpoint.address), {
        method: 'POST',
        headers: { Authorization: `Bearer ${endpoint.token}` },
        signal: AbortSignal.timeout(5000),
      })
      assert(response.ok, 'Owned backend shutdown must succeed.')
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
              () => reject(Error('Owned packaged frontend did not close within10 seconds.')),
              10000,
            )
          }),
        ])
      } finally {
        clearTimeout(closeTimer)
      }
    }
    if (result.backendProcessId) await waitGone(result.backendProcessId)
    if (result.processId) await waitGone(result.processId)
    if (portableLock) {
      await expect
        .poll(
          async () => {
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
  }
  await persist()
}
console.log(JSON.stringify({ passed: result.passed, closed: result.closed, report: reportPath }))
