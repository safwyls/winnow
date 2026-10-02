import { test, expect, _electron as electron, type ElectronApplication } from '@playwright/test'
import { spawn } from 'node:child_process'
import { mkdtemp, readFile, readdir } from 'node:fs/promises'
import { join, resolve, sep } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import { build } from 'esbuild'
import { prebuiltBackend as backend, prebuiltActivationHelper } from './prebuilt-backend'

const helperModule = resolve('../..', '.tmp/task38124-activation-native/host.mjs')
const hostEnvironment = {
  WINNOW_BACKEND_PATH: backend,
  WINNOW_ACTIVATION_HELPER_PATH: prebuiltActivationHelper,
  WINNOW_ACTIVATION_MODULE: helperModule,
}
test.beforeAll(async () => {
  await readFile(backend)
  await build({
    entryPoints: ['src/main/activation-host.ts'],
    bundle: true,
    platform: 'node',
    format: 'esm',
    packages: 'external',
    outfile: helperModule,
  })
})

async function directory() {
  return mkdtemp(join(resolve('../..', '.tmp'), 'winnow-native-activation-'))
}
async function launch(
  directory: string,
  early = false,
  initial: string[] = [],
  fixture = 'activation-main.mjs',
) {
  const application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron', fixture),
      '--data-dir',
      directory,
      '--no-sync',
      '--seed-sample',
      '--background',
      ...initial,
    ],
    env: {
      ...Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ),
      WINNOW_FIXTURE_EARLY_ACTIVATION: early ? '1' : '0',
      ...hostEnvironment,
    } as Record<string, string>,
    chromiumSandbox: true,
  })
  try {
    await expect
      .poll(() => application.evaluate(() => (globalThis as any).__activationFixture.state.waiting))
      .toBe(true)
    const page = await application.firstWindow()
    await expect
      .poll(() => page.evaluate(async () => (await window.winnow.connection()).connected), { timeout: 45000 })
      .toBe(true)
    return application
  } catch (failure) {
    const connection = await application
      .firstWindow()
      .then((page) => page.evaluate(() => window.winnow.connection()))
      .catch(() => null)
    try {
      await closeFixture(application, directory)
    } catch (cleanupFailure) {
      throw new AggregateError(
        [failure, cleanupFailure],
        `Activation startup failed: ${directory}; ${JSON.stringify(connection)}`,
      )
    }
    throw new Error(`Activation startup failed: ${directory}; ${JSON.stringify(connection)}`, {
      cause: failure,
    })
  }
}
async function secondary(directory: string, args: string[]) {
  const child = spawn(electronPath as unknown as string, [resolve('.'), '--data-dir', directory, ...args], {
    windowsHide: true,
    stdio: 'pipe',
    env: { ...process.env, ELECTRON_RUN_AS_NODE: undefined, ...hostEnvironment },
  })
  let diagnostics = ''
  child.stderr?.on('data', (chunk) => {
    diagnostics += chunk.toString()
  })
  await new Promise<void>((done, reject) => {
    const timer = setTimeout(() => {
      child.kill()
      reject(Error(`Secondary activation timed out: ${diagnostics}`))
    }, 15000)
    child.once('error', (error) => {
      clearTimeout(timer)
      reject(error)
    })
    child.once('exit', (code) => {
      clearTimeout(timer)
      code === 0 ? done() : reject(Error(`Secondary activation exited ${code}: ${diagnostics}`))
    })
  })
}
async function release(application: ElectronApplication) {
  await application.evaluate(() => (globalThis as any).__activationFixture.release())
  await expect
    .poll(() => application.evaluate(() => (globalThis as any).__activationFixture.state.drained.length))
    .toBeGreaterThan(0)
  return application.evaluate(() => (globalThis as any).__activationFixture.state.drained)
}

test('early handoff restores a background window and queues real second-process launches in order', async () => {
  const profile = await directory(),
    application = await launch(profile, true)
  try {
    expect(
      await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].isVisible()),
    ).toBe(true)
    const before = JSON.parse(await readFile(join(profile, 'backend/endpoint.json'), 'utf8'))
    const plugin = ['--uri', 'winnow://plugins/install?id=psn&release=v1.2.3']
    for (const args of [plugin, plugin, ['--jump-list-fullscreen'], ['--jump-list-game', '42'], [], []])
      await secondary(profile, args)
    expect(await release(application)).toEqual([
      { kind: 'show' },
      { kind: 'plugin', pluginId: 'psn', releaseTag: 'v1.2.3' },
      { kind: 'fullscreen' },
      { kind: 'game', ownershipId: 42 },
      { kind: 'show' },
    ])
    await secondary(profile, [])
    await secondary(`${profile}${sep}`, [])
    await expect
      .poll(() => application.evaluate(() => (globalThis as any).__activationFixture.state.delivered))
      .toEqual([{ kind: 'show' }, { kind: 'show' }])
    const after = JSON.parse(await readFile(join(profile, 'backend/endpoint.json'), 'utf8'))
    expect({ processId: after.processId, epoch: after.epoch }).toEqual({
      processId: before.processId,
      epoch: before.epoch,
    })
  } finally {
    await closeFixture(application, profile)
  }
})

test('native dispatch rejects malformed payloads without showing the window and bounds then recovers its queue', async () => {
  const profile = await directory(),
    application = await launch(profile)
  try {
    const rejected = await application.evaluate(async () => {
      const { send } = (globalThis as any).__activationFixture
      return [
        await send('bytes', [0]),
        await send('bytes', [255]),
        await send('game', '-1'),
        await send('game', '0'),
        await send('plugin', 'winnow://plugins/install?id=&release='),
        await send('plugin', 'x'.repeat(65535)),
        await send('plugin', 'winnow://plugins/install?id=unknown&release=v1.2.3'),
      ]
    })
    expect(rejected.map((value) => value.accepted)).toEqual(Array(7).fill(false))
    expect(
      await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].isVisible()),
    ).toBe(false)
    await application.evaluate(async () => {
      for (let id = 1; id <= 65; id++) await (globalThis as any).__activationFixture.send('game', id)
    })
    expect(await release(application)).toEqual(
      Array.from({ length: 64 }, (_, i) => ({ kind: 'game', ownershipId: i + 1 })),
    )
    await secondary(profile, ['--jump-list-fullscreen'])
    await expect
      .poll(() => application.evaluate(() => (globalThis as any).__activationFixture.state.delivered))
      .toEqual([{ kind: 'fullscreen' }])
  } finally {
    await closeFixture(application, profile)
  }
})

test('cold-start ownership and second-process Int64 requests retain the original complete FIFO sequence', async () => {
  const profile = await directory(),
    application = await launch(profile, false, ['--jump-list-game', '23'])
  try {
    await secondary(profile, ['--jump-list-fullscreen'])
    await secondary(profile, ['--jump-list-game', '9223372036854775807'])
    expect(await release(application)).toEqual([
      { kind: 'game', ownershipId: 23 },
      { kind: 'fullscreen' },
      { kind: 'game', ownershipId: '9223372036854775807' },
    ])
    await secondary(profile, [])
    await expect
      .poll(() => application.evaluate(() => (globalThis as any).__activationFixture.state.delivered))
      .toEqual([{ kind: 'show' }])
  } finally {
    await closeFixture(application, profile)
  }
})

test('a real secondary launch waits while the primary has not registered its listener or become ready', async () => {
  const profile = await directory(),
    application = await launch(profile, false, [], 'activation-pre-ready-main.mjs')
  try {
    expect(
      await application.evaluate(() => (globalThis as any).__preReady.requestedBeforeFrontendReady),
    ).toBe(true)
    expect(await application.evaluate(() => (globalThis as any).__preReady.platformReady)).toBe(true)
    await expect.poll(() => application.evaluate(() => (globalThis as any).__preReady.exitCode)).toBe(0)
    expect(await release(application)).toEqual([{ kind: 'fullscreen' }])
  } finally {
    await closeFixture(application, profile)
  }
})

for (const mode of ['desktop', 'fullscreen'])
  test(`${mode} extended activation reaches the real backend with an exact Int64 URL`, async () => {
    const profile = await directory(),
      application = await launch(profile)
    try {
      const page = await application.firstWindow()
      await application.evaluate(({ BrowserWindow }) => {
        const fixture = (globalThis as any).__activationFixture
        fixture.state.forward = true
        fixture.release()
        BrowserWindow.getAllWindows()[0].show()
      })
      await expect
        .poll(() => application.evaluate(() => (globalThis as any).__activationFixture.state.ready))
        .toBe(true)
      expect(await page.evaluate(async () => window.winnow.request({ route: 'setup.get' }))).toMatchObject({
        ok: true,
        data: { step: null },
      })
      await expect(page.locator('.startup-presentation')).toHaveCount(0)
      await expect(page.getByRole('navigation', { name: 'Main navigation' })).toBeVisible()
      if (mode === 'fullscreen') {
        await page.evaluate(() => window.winnow.setFullscreen(true))
        await expect(page.locator('.avalon-shell.fullscreen')).toBeVisible()
        await expect(page.locator('.startup-presentation')).toHaveCount(0)
      }
      await secondary(profile, ['--jump-list-game', '9223372036854775807'])
      await expect
        .poll(() => application.evaluate(() => (globalThis as any).__activationFixture.state.actionPaths))
        .toEqual(['/api/v1/entries/9223372036854775807/actions'])
      await expect(page.getByText('Game entry not found.', { exact: true })).toBeVisible()
    } finally {
      await closeFixture(application, profile)
    }
  })

test('profile locks isolate active libraries and a stopped profile can become primary again', async () => {
  const first = await directory(),
    second = await directory()
  let one: ElectronApplication | undefined, two: ElectronApplication | undefined
  try {
    one = await launch(first)
    two = await launch(second)
    await secondary(first, ['--jump-list-fullscreen'])
    await secondary(second, ['--jump-list-game', '73'])
    expect(await release(one)).toEqual([{ kind: 'fullscreen' }])
    expect(await release(two)).toEqual([{ kind: 'game', ownershipId: 73 }])
    const firstEndpoint = JSON.parse(await readFile(join(first, 'backend/endpoint.json'), 'utf8'))
    const secondEndpoint = JSON.parse(await readFile(join(second, 'backend/endpoint.json'), 'utf8'))
    expect(firstEndpoint.processId).not.toBe(secondEndpoint.processId)
    expect(await one.evaluate(({ app }) => app.getPath('userData'))).not.toBe(
      await two.evaluate(({ app }) => app.getPath('userData')),
    )
    await closeFixture(one, first)
    one = undefined
    one = await launch(first)
    await secondary(first, [])
    expect(await release(one)).toEqual([{ kind: 'show' }])
    expect(await two.evaluate(() => (globalThis as any).__activationFixture.state.delivered)).toEqual([])
  } finally {
    if (one) await closeFixture(one, first)
    if (two) await closeFixture(two, second)
  }
})

test('secondary startup exits before creating a database and an invalid installation URI never activates its owner', async () => {
  const profile = await directory()
  const application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('tests/electron/activation-owner-main.mjs'), '--data-dir', profile],
    env: {
      ...(Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ) as Record<string, string>),
      ...hostEnvironment,
    },
    chromiumSandbox: true,
  })
  try {
    await application.evaluate(({ app }) => app.whenReady())
    for (const args of [
      [],
      ['--jump-list-fullscreen'],
      ['--jump-list-game', '42'],
      ['--uri', 'winnow://plugins/install?id=psn&release=v0.2.0'],
    ])
      await secondary(profile, args)
    await expect
      .poll(() => application.evaluate(() => (globalThis as any).__received))
      .toEqual([
        { kind: 'show' },
        { kind: 'fullscreen' },
        { kind: 'game', ownershipId: 42 },
        { kind: 'plugin', pluginId: 'psn', releaseTag: 'v0.2.0' },
      ])
    expect(await readdir(profile)).toEqual(['electron-userdata'])
    const child = spawn(
      electronPath as unknown as string,
      [
        resolve('tests/electron/activation-invalid-main.mjs'),
        '--data-dir',
        profile,
        '--uri',
        'winnow://plugins/install?id=psn&release=v0.2.0" --data-dir untrusted',
      ],
      {
        windowsHide: true,
        stdio: 'pipe',
        env: {
          ...process.env,
          ELECTRON_RUN_AS_NODE: undefined,
          WINNOW_FIXTURE_PROFILE: join(profile, 'invalid-chromium'),
        },
      },
    )
    let diagnostics = ''
    child.stderr?.on('data', (chunk) => {
      diagnostics += chunk.toString()
    })
    const code = await new Promise<number | null>((done, reject) => {
      const timer = setTimeout(() => {
        child.kill()
        reject(Error('Invalid URI process did not exit'))
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
    expect(code, diagnostics).toBe(2)
    expect(diagnostics).toContain('startup arguments are invalid')
    expect(await application.evaluate(() => (globalThis as any).__received.length)).toBe(4)
    expect((await readdir(profile)).sort()).toEqual(['electron-userdata', 'invalid-chromium'])
  } finally {
    await closeFixture(application)
  }
})
