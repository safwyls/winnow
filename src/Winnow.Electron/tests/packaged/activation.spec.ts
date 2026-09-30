import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { execFile, spawn } from 'node:child_process'
import { createHash } from 'node:crypto'
import { promisify } from 'node:util'
import { mkdtemp, readFile, stat } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { closeFixture } from '../electron/fixture-cleanup'
import { quoteArgument } from '../../src/main/activation'
import type { ApplicationActivation } from '../../src/shared/bridge'

test.skip(process.platform !== 'win32', 'Windows executable and shortcut integration')
const executable = resolve(process.env.WINNOW_PACKAGED_EXE ?? 'release/win-unpacked/Winnow.exe')
let application: ElectronApplication, page: Page, directory: string
let endpoint: { processId: number; epoch: string }
let protocolBefore: string
async function protocolSnapshot() {
  try {
    const result = await promisify(execFile)('reg.exe', ['query', 'HKCU\\Software\\Classes\\winnow', '/s'], {
      windowsHide: true,
    })
    return createHash('sha256').update(result.stdout).digest('hex')
  } catch (error) {
    const result = error as { code?: number; stdout?: string; stderr?: string }
    if (result.code !== 1) throw error
    return createHash('sha256').update(`${result.code}:${result.stdout}:${result.stderr}`).digest('hex')
  }
}
const environment = Object.fromEntries(
  Object.entries(process.env).filter(
    ([key, value]) =>
      !['ELECTRON_RUN_AS_NODE', 'WINNOW_BACKEND_PATH', 'WINNOW_APPIMAGE_UPDATED'].includes(key) &&
      value !== undefined,
  ),
) as Record<string, string>

test.beforeAll(async () => {
  expect((await stat(executable)).isFile(), 'Run npm run package before the packaged suite').toBe(true)
  protocolBefore = await protocolSnapshot()
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow packaged library '))
  application = await electron.launch({
    executablePath: executable,
    args: ['--data-dir', directory, '--no-sync'],
    env: environment,
    chromiumSandbox: true,
  })
  expect(await application.evaluate(({ app }) => app.isPackaged)).toBe(true)
  page = await application.firstWindow()
  await page.getByRole('button', { name: 'Skip setup', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Winnow home' })).toBeVisible()
  endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
  await application.evaluate(({ BrowserWindow }) => {
    const state = { received: [] as unknown[] }
    ;(globalThis as any).__packagedActivation = state
    const contents = BrowserWindow.getAllWindows()[0].webContents
    const send = contents.send.bind(contents)
    contents.send = (channel, ...args) => {
      if (channel === 'winnow:activation') {
        state.received.push(args[0])
        // Observe routing without sending game/provider commands to external software.
        if (args[0].kind === 'game' || args[0].kind === 'plugin') return
      }
      return send(channel, ...args)
    }
  })
})
test.afterAll(async () => {
  if (application) {
    await application.evaluate(({ app }) => app.setJumpList(null))
    await closeFixture(application, directory)
  }
})
test.beforeEach(async () => {
  await page.evaluate(() => window.winnow.setFullscreen(false))
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  await application.evaluate(({ BrowserWindow }) => {
    ;(globalThis as any).__packagedActivation.received = []
    BrowserWindow.getAllWindows()[0].hide()
  })
})

const variants: { name: string; args: string[]; expected: ApplicationActivation }[] = [
  { name: 'show', args: [], expected: { kind: 'show' } },
  { name: 'fullscreen', args: ['--jump-list-fullscreen'], expected: { kind: 'fullscreen' } },
  { name: 'game', args: ['--jump-list-game', '42'], expected: { kind: 'game', ownershipId: 42 } },
  {
    name: 'plugin',
    args: ['--uri', 'winnow://plugins/install?id=psn&release=v0.2.0'],
    expected: { kind: 'plugin', pluginId: 'psn', releaseTag: 'v0.2.0' },
  },
]
for (const method of ['executable', 'shortcut'] as const)
  for (const variant of variants) {
    test(`${method} ${variant.name} restores the packaged owner and retains its backend`, async () => {
      const args = ['--data-dir', directory, ...variant.args]
      if (method === 'executable') {
        const child = spawn(executable, args, { windowsHide: true, stdio: 'pipe', env: environment })
        let diagnostic = ''
        child.stderr?.on('data', (chunk) => {
          diagnostic += chunk.toString()
        })
        const code = await new Promise<number | null>((done, reject) => {
          const timer = setTimeout(() => {
            child.kill()
            reject(Error(`Packaged secondary did not exit: ${diagnostic}`))
          }, 15000)
          child.once('error', (error) => {
            clearTimeout(timer)
            reject(error)
          })
          child.once('exit', (value) => {
            clearTimeout(timer)
            done(value)
          })
        })
        expect(code, diagnostic).toBe(0)
      } else {
        const shortcut = join(directory, `${variant.name}.lnk`)
        const opened = await application.evaluate(
          async ({ shell }, input) => {
            if (
              !shell.writeShortcutLink(input.shortcut, 'create', {
                target: input.executable,
                cwd: input.cwd,
                args: input.args,
              })
            )
              throw Error('Could not write the fixture shortcut')
            const resolved = shell.readShortcutLink(input.shortcut)
            return {
              target: resolved.target,
              args: resolved.args,
              error: await shell.openPath(input.shortcut),
            }
          },
          { shortcut, executable, cwd: dirname(executable), args: args.map(quoteArgument).join(' ') },
        )
        expect(opened).toEqual({ target: executable, args: args.map(quoteArgument).join(' '), error: '' })
      }
      await expect
        .poll(() => application.evaluate(() => (globalThis as any).__packagedActivation.received))
        .toEqual([variant.expected])
      expect(
        await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].isVisible()),
      ).toBe(true)
      if (variant.name === 'fullscreen') {
        await expect(page.locator('.avalon-shell.fullscreen')).toBeVisible()
        await expect(page.locator('.startup-presentation')).toHaveCount(0)
      }
      const current = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
      expect({ processId: current.processId, epoch: current.epoch }).toEqual({
        processId: endpoint.processId,
        epoch: endpoint.epoch,
      })
    })
  }

test('the isolated packaged profile leaves the global installation-link association unchanged', async () => {
  expect(await protocolSnapshot()).toBe(protocolBefore)
})
