import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp, readFile, rm } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

const snapshot = (application: ElectronApplication) =>
  application.evaluate(() => (globalThis as any).__tray.snapshot())
const openTray = (application: ElectronApplication, method = 'menu') =>
  application.evaluate((_electron, method) => (globalThis as any).__tray.open(method), method)
async function preference(page: Page, name: string, value: string) {
  const result = await page.evaluate(
    ({ name, value }) =>
      window.winnow.request({
        route: 'preferences.presentation.put',
        params: { preference: name },
        body: { value },
      }),
    { name, value },
  )
  expect(result.ok).toBe(true)
}
async function launch(directory: string, background = false, failure = false) {
  await rm(join(directory, 'tray-exit.json'), { force: true })
  const application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/tray-main.mjs'),
      '--data-dir',
      directory,
      '--no-sync',
      '--seed-sample',
      ...(background ? ['--background'] : []),
    ],
    env: {
      ...(Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ) as Record<string, string>),
      WINNOW_FIXTURE_TRAY_FAILURE: failure ? '1' : '0',
    },
    chromiumSandbox: true,
  })
  try {
    const page = await application.firstWindow()
    await expect
      .poll(() => page.evaluate(() => window.winnow.connection().then((state) => state.connected)), {
        timeout: 45000,
      })
      .toBe(true)
    await expect(page.locator('.startup-presentation')).toHaveCount(0, { timeout: 45000 })
    await expect(page.locator('.avalon-shell')).toHaveCount(1)
    return { application, page }
  } catch (error) {
    await closeFixture(application, directory)
    throw error
  }
}
const directoryFor = () => mkdtemp(join(resolve('../..', '.tmp'), 'winnow-native-tray-'))
async function finishQuit(application: ElectronApplication, directory: string) {
  let result: { alive: number; destroyed: number; created: number; window: unknown } | undefined
  await expect
    .poll(async () => {
      try {
        result = JSON.parse(await readFile(join(directory, 'tray-exit.json'), 'utf8'))
        return true
      } catch {
        return false
      }
    })
    .toBe(true)
  // The fixture detaches its inspector only after native quit; no cleanup action
  // can cause this assertion to pass for a window that merely hid in the tray.
  await expect.poll(() => application.process().exitCode).toBe(0)
  return result!
}

test('tray preferences default off, round trip, and share exactly one native icon', async () => {
  const directory = await directoryFor()
  let { application, page } = await launch(directory)
  try {
    expect(await snapshot(application)).toMatchObject({
      created: 0,
      alive: 0,
      window: { visible: true, skipTaskbar: false },
    })
    const initial = await page.evaluate(() =>
      window.winnow.request({ route: 'preferences.presentation.get' }),
    )
    expect(
      initial.ok &&
        (initial.data as Array<{ preference: string; value: string | null }>)
          .filter((row) => ['MinimizeToTray', 'CloseToTray', 'StartInFullscreen'].includes(row.preference))
          .every((row) => row.value === null || row.value.toLowerCase() === 'false'),
    ).toBe(true)
    await preference(page, 'MinimizeToTray', 'True')
    await expect.poll(() => snapshot(application)).toMatchObject({ created: 1, alive: 1 })
    expect(await snapshot(application)).toMatchObject({ menus: [['Open Winnow', 'separator', 'Exit']] })
    await preference(page, 'CloseToTray', 'True')
    await preference(page, 'MinimizeToTray', 'False')
    await expect.poll(() => snapshot(application)).toMatchObject({ created: 1, alive: 1 })
    await preference(page, 'MinimizeToTray', 'True')
    await closeFixture(application, directory)
    ;({ application, page } = await launch(directory))
    await expect.poll(() => snapshot(application)).toMatchObject({ created: 1, alive: 1 })
    const saved = await page.evaluate(() => window.winnow.request({ route: 'preferences.presentation.get' }))
    expect(saved.ok && saved.data).toEqual(
      expect.arrayContaining([
        { preference: 'CloseToTray', value: 'True' },
        { preference: 'MinimizeToTray', value: 'True' },
      ]),
    )
    await preference(page, 'MinimizeToTray', 'False')
    await preference(page, 'CloseToTray', 'False')
    await expect.poll(() => snapshot(application)).toMatchObject({ alive: 0, destroyed: 1 })
    await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].minimize())
    await expect
      .poll(() => snapshot(application))
      .toMatchObject({ window: { minimized: true, skipTaskbar: false } })
  } finally {
    await closeFixture(application, directory)
  }
})

test('a confirmed tray save reaches the native handler before a subsequent close can exit', async () => {
  const directory = await directoryFor()
  const { application, page } = await launch(directory)
  try {
    await application.evaluate(() => (globalThis as any).__tray.holdPreferences())
    await page.evaluate(() => {
      ;(window as any).trayWriteSettled = false
      void window.winnow
        .request({
          route: 'preferences.presentation.put',
          params: { preference: 'CloseToTray' },
          body: { value: 'True' },
        })
        .then((result) => {
          ;(window as any).trayWriteSettled = result.ok
        })
    })
    await expect
      .poll(() => application.evaluate(() => (globalThis as any).__tray.pendingPreferences()))
      .toBeGreaterThan(0)
    expect(await page.evaluate(() => (window as any).trayWriteSettled)).toBe(false)
    await application.evaluate(() => (globalThis as any).__tray.releasePreferences())
    await expect.poll(() => page.evaluate(() => (window as any).trayWriteSettled)).toBe(true)
    await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].close())
    await expect
      .poll(() => snapshot(application))
      .toMatchObject({ alive: 1, window: { visible: false, skipTaskbar: true } })
  } finally {
    await application.evaluate(() => (globalThis as any).__tray.releasePreferences())
    await closeFixture(application, directory)
  }
})

for (const state of ['normal', 'maximized', 'fullscreen'] as const)
  test(`native tray restoration preserves ${state} presentation when visible, minimized, or closed`, async () => {
    const directory = await directoryFor()
    const { application, page } = await launch(directory)
    try {
      await preference(page, 'MinimizeToTray', 'True')
      await preference(page, 'CloseToTray', 'True')
      await expect.poll(() => snapshot(application)).toMatchObject({ alive: 1 })
      await application.evaluate(({ BrowserWindow }, state) => {
        const window = BrowserWindow.getAllWindows()[0]
        if (state === 'maximized') window.maximize()
        if (state === 'fullscreen') window.setFullScreen(true)
      }, state)
      const restored = {
        visible: true,
        minimized: false,
        maximized: state === 'maximized',
        fullscreen: state === 'fullscreen',
        skipTaskbar: false,
      }
      await expect.poll(() => snapshot(application)).toMatchObject({ window: restored })
      await openTray(application)
      await expect.poll(() => snapshot(application)).toMatchObject({ window: restored })
      await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].minimize())
      await expect
        .poll(() => snapshot(application))
        .toMatchObject({ alive: 1, window: { visible: false, skipTaskbar: true } })
      await openTray(application, 'double-click')
      await expect.poll(() => snapshot(application)).toMatchObject({ window: restored })
      await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].close())
      await expect
        .poll(() => snapshot(application))
        .toMatchObject({ alive: 1, window: { visible: false, skipTaskbar: true } })
      await openTray(application)
      await expect.poll(() => snapshot(application)).toMatchObject({ window: restored })
      if (state === 'fullscreen') await expect(page.locator('.avalon-shell')).toHaveClass(/fullscreen/)
      else await expect(page.locator('.avalon-shell')).not.toHaveClass(/fullscreen/)
    } finally {
      await closeFixture(application, directory)
    }
  })

for (const action of ['minimize', 'close'] as const)
  test(`disabling tray preferences while hidden by ${action} retains a native recovery route until restored`, async () => {
    const directory = await directoryFor()
    const { application, page } = await launch(directory)
    try {
      await preference(page, 'MinimizeToTray', 'True')
      await preference(page, 'CloseToTray', 'True')
      await expect.poll(() => snapshot(application)).toMatchObject({ alive: 1 })
      await application.evaluate(
        ({ BrowserWindow }, action) => BrowserWindow.getAllWindows()[0][action](),
        action,
      )
      await expect
        .poll(() => snapshot(application))
        .toMatchObject({ window: { visible: false, skipTaskbar: true } })
      await preference(page, 'MinimizeToTray', 'False')
      await preference(page, 'CloseToTray', 'False')
      await expect
        .poll(() =>
          page.evaluate(() =>
            window.winnow
              .request({ route: 'preferences.presentation.get' })
              .then((result) => result.ok && result.data),
          ),
        )
        .toEqual(
          expect.arrayContaining([
            { preference: 'MinimizeToTray', value: 'False' },
            { preference: 'CloseToTray', value: 'False' },
          ]),
        )
      expect(await snapshot(application)).toMatchObject({ alive: 1, destroyed: 0 })
      await openTray(application)
      await expect
        .poll(() => snapshot(application))
        .toMatchObject({
          alive: 0,
          destroyed: 1,
          window: { visible: true, skipTaskbar: false, minimized: false },
        })
    } finally {
      await closeFixture(application, directory)
    }
  })

test('fullscreen startup round trips both values without requesting a tray icon or reapplying on restore', async () => {
  const directory = await directoryFor()
  let { application, page } = await launch(directory)
  try {
    expect(await snapshot(application)).toMatchObject({ alive: 0, window: { fullscreen: false } })
    await preference(page, 'StartInFullscreen', 'True')
    expect(await snapshot(application)).toMatchObject({ alive: 0, window: { fullscreen: false } })
    await closeFixture(application, directory)
    ;({ application, page } = await launch(directory))
    await expect
      .poll(() => snapshot(application))
      .toMatchObject({ created: 0, alive: 0, window: { fullscreen: true } })
    await expect(page.locator('.avalon-shell')).toHaveClass(/fullscreen/)
    await page.evaluate(() => window.winnow.setFullscreen(false))
    await expect(page.locator('.avalon-shell')).not.toHaveClass(/fullscreen/)
    await application.evaluate(({ BrowserWindow, app }) => {
      BrowserWindow.getAllWindows()[0].hide()
      app.emit('activate')
    })
    await expect
      .poll(() => snapshot(application))
      .toMatchObject({ created: 0, window: { visible: true, fullscreen: false } })
    await preference(page, 'StartInFullscreen', 'False')
    await preference(page, 'StartInFullscreen', 'True')
    expect(await snapshot(application)).toMatchObject({ created: 0, window: { fullscreen: false } })
    await preference(page, 'StartInFullscreen', 'False')
    await closeFixture(application, directory)
    ;({ application, page } = await launch(directory))
    expect(await snapshot(application)).toMatchObject({ created: 0, alive: 0, window: { fullscreen: false } })
  } finally {
    await closeFixture(application, directory)
  }
})

test('background startup suppresses saved fullscreen and releases its temporary native icon on restore', async () => {
  const directory = await directoryFor()
  let { application, page } = await launch(directory)
  try {
    await preference(page, 'StartInFullscreen', 'True')
    await closeFixture(application, directory)
    ;({ application, page } = await launch(directory, true))
    await expect
      .poll(() => snapshot(application))
      .toMatchObject({
        created: 1,
        alive: 1,
        window: { visible: false, fullscreen: false, skipTaskbar: true },
      })
    expect(await page.evaluate(() => window.winnow.presentationVisible?.())).toBe(false)
    await openTray(application)
    await expect
      .poll(() => snapshot(application))
      .toMatchObject({
        alive: 0,
        destroyed: 1,
        window: { visible: true, fullscreen: false, skipTaskbar: false },
      })
    await expect(page.locator('.avalon-shell')).not.toHaveClass(/fullscreen/)
  } finally {
    await closeFixture(application, directory)
  }
})

test('session notification activation restores a maximized window and retires an unneeded tray icon', async () => {
  const directory = await directoryFor()
  const { application, page } = await launch(directory)
  try {
    await preference(page, 'CloseToTray', 'True')
    await expect.poll(() => snapshot(application)).toMatchObject({ alive: 1 })
    await page.evaluate(() => {
      window.winnow.onJournalNotificationActivated?.((id) => {
        ;(window as any).trayJournalSession = id
      })
    })
    await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].maximize())
    await expect.poll(() => snapshot(application)).toMatchObject({ window: { maximized: true } })
    await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].close())
    await expect.poll(() => snapshot(application)).toMatchObject({ window: { visible: false } })
    await preference(page, 'CloseToTray', 'False')
    expect(
      await page.evaluate(() =>
        window.winnow.notifySessionEnded?.({ sessionId: 42, title: 'Isolated session' }),
      ),
    ).toBe(true)
    await application.evaluate(() => (globalThis as any).__tray.activateNotification())
    await expect
      .poll(() => snapshot(application))
      .toMatchObject({
        alive: 0,
        destroyed: 1,
        window: { visible: true, maximized: true, skipTaskbar: false },
      })
    await expect.poll(() => page.evaluate(() => (window as any).trayJournalSession)).toBe(42)
  } finally {
    await closeFixture(application, directory)
  }
})

for (const exit of ['tray', 'ordinary'] as const)
  test(`${exit} quit destroys the native icon and exits instead of hiding`, async () => {
    const directory = await directoryFor()
    const { application, page } = await launch(directory)
    let closed = false
    try {
      await preference(page, 'CloseToTray', 'True')
      await expect.poll(() => snapshot(application)).toMatchObject({ alive: 1 })
      await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].close())
      await expect.poll(() => snapshot(application)).toMatchObject({ window: { visible: false } })
      await application.evaluate(({ app }, exit) => {
        ;(globalThis as any).__tray.prepareExit()
        setImmediate(() => {
          if (exit === 'tray') (globalThis as any).__tray.quit()
          else app.quit()
        })
      }, exit)
      const result = await finishQuit(application, directory)
      closed = true
      expect(result).toMatchObject({
        alive: 0,
        destroyed: 1,
        window: null,
      })
    } finally {
      await closeFixture(closed ? undefined : application, directory)
    }
  })

test('failed native tray setup leaves a background launch visible and permits ordinary close', async () => {
  const directory = await directoryFor()
  let { application, page } = await launch(directory)
  let closed = false
  try {
    await preference(page, 'CloseToTray', 'True')
    await closeFixture(application, directory)
    ;({ application, page } = await launch(directory, true, true))
    await expect
      .poll(() => snapshot(application))
      .toMatchObject({ alive: 0, window: { visible: true, skipTaskbar: false } })
    await application.evaluate(({ BrowserWindow }) => {
      ;(globalThis as any).__tray.prepareExit()
      setImmediate(() => BrowserWindow.getAllWindows()[0].close())
    })
    const result = await finishQuit(application, directory)
    closed = true
    expect(result.alive).toBe(0)
    expect(result.created).toBeGreaterThan(0)
    expect(result.created).toBe(result.destroyed)
  } finally {
    await closeFixture(closed ? undefined : application, directory)
  }
})
