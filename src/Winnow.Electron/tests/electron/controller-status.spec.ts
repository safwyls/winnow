import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import electronPath from 'electron'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { closeFixture } from './fixture-cleanup'

let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-controller-status-'))
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/controller-status-main.mjs'),
      '--data-dir',
      directory,
      '--seed-sample',
      '--no-sync',
    ],
    env: Object.fromEntries(
      Object.entries(process.env).filter(
        ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
      ),
    ) as Record<string, string>,
    chromiumSandbox: true,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
})
test.afterAll(async () => closeFixture(application, directory))
test.afterEach(async ({}, info) => {
  if (info.status !== info.expectedStatus)
    await info.attach('header-failure', { body: await page.screenshot(), contentType: 'image/png' })
  expect(errors).toEqual([])
})
async function fullscreen(width = 1280, height = 720, scale = 1) {
  await application.evaluate(
    ({ BrowserWindow }, size) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setContentSize(size.width, size.height)
      window.webContents.send('winnow:fullscreen:changed', true)
    },
    { width, height },
  )
  await expect(page.locator('.avalon-shell')).toHaveClass(/fullscreen/)
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
  await page.evaluate(async (scale) => {
    for (const [preference, value] of [
      ['FullscreenTextScale', String(scale)],
      ['FullscreenInterfaceScale', scale === 1 ? '1' : '1.2'],
    ]) {
      const result = await window.winnow.request({
        route: 'preferences.presentation.put',
        params: { preference },
        body: { value },
      })
      if (!result.ok) throw Error(result.message)
    }
  }, scale)
  await expect
    .poll(() =>
      page.evaluate(() => document.documentElement.style.getPropertyValue('--fullscreen-text-scale')),
    )
    .toBe(String(scale))
  await expect
    .poll(() =>
      page.evaluate(() => document.documentElement.style.getPropertyValue('--fullscreen-interface-scale')),
    )
    .toBe(scale === 1 ? '1' : '1.2')
}
async function nativeReadings(type: number | null, level: number | null, duplicate = false) {
  await application.evaluate(
    (_, data) => {
      const row = {
        slot: 2,
        type: data.type,
        level: data.level,
        buttons: Array(16).fill(0),
        axes: [0, 0, 0, 0],
      }
      Object.assign(globalThis, { __controllerReadings: data.duplicate ? [row, { ...row, slot: 1 }] : [row] })
    },
    { type, level, duplicate },
  )
}
async function connect(connected = true) {
  await page.evaluate((connected) => {
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () =>
        connected
          ? [
              null,
              {
                id: 'Xbox 360 Controller (XInput STANDARD GAMEPAD)',
                index: 7,
                connected: true,
                mapping: 'standard',
                buttons: Array.from({ length: 17 }, () => ({ pressed: false, value: 0 })),
                axes: [0, 0, 0, 0],
              },
            ]
          : [],
    })
    window.dispatchEvent(new Event(connected ? 'gamepadconnected' : 'gamepaddisconnected'))
  }, connected)
}

test('native preload validates samples and preserves unknown, wired, known, ambiguous and reconnect states', async () => {
  await fullscreen()
  const status = page.locator('.avalon-controller-status')
  await expect(status).toHaveText('Controller disconnected')
  for (const [type, level, label] of [
    [0, 0, 'Controller connected'],
    [255, 3, 'Controller connected'],
    [1, 0, 'Wired controller'],
    [2, 1, 'Controller battery low'],
    [3, 3, 'Controller battery full'],
  ] as const) {
    await nativeReadings(type, level)
    await connect()
    await expect(status).toHaveText(label)
  }
  await nativeReadings(3, 3, true)
  await connect()
  await expect(status).toHaveText('Controller connected')
  await connect(false)
  await expect(status).toHaveText('Controller disconnected')
  await nativeReadings(2, 2)
  await connect()
  await expect(status).toHaveText('Controller battery medium')
  const rejection = await page.evaluate(async () => {
    try {
      await window.winnow.controllerBattery!({ id: 'bad', index: -1, buttons: [], axes: [] })
      return 'accepted'
    } catch (error) {
      return String(error)
    }
  })
  expect(rejection).toContain('Invalid controller sample')
  expect(
    await application.evaluate(
      () => (globalThis as unknown as { __controllerStarts: number }).__controllerStarts,
    ),
  ).toBeGreaterThan(0)
})

for (const [width, height] of [
  [1280, 720],
  [1920, 1080],
])
  for (const scale of [1, 1.4])
    test(`Main_navigation_stays_centered_with_varying_controller_status_and_clock at ${width} text ${scale}`, async ({}, info) => {
      await fullscreen(width, height, scale)
      await nativeReadings(2, 2)
      await connect(false)
      for (const value of ['', 'Controller disconnected', 'Controller connected · Battery 100%']) {
        const geometry = await page.evaluate((value) => {
          const header = document.querySelector('.avalon-header')!,
            nav = header.querySelector('nav')!,
            status = header.querySelector('.avalon-controller-status')!,
            clock = header.querySelector('time')!
          const originalStatus = status.textContent,
            originalClock = clock.textContent
          status.textContent = value
          clock.textContent = value ? '11:59 PM' : '1:01'
          const h = header.getBoundingClientRect(),
            n = nav.getBoundingClientRect(),
            s = status.getBoundingClientRect(),
            c = clock.getBoundingClientRect()
          const geometry = {
            center: n.x + n.width / 2,
            expected: h.x + h.width / 2,
            clockRight: c.right,
            right: h.right,
            statusLeft: s.left,
            navRight: n.right,
            statusColor: getComputedStyle(status).color,
            clockColor: getComputedStyle(clock).color,
            ellipsis: getComputedStyle(status).textOverflow,
            clockFont: getComputedStyle(clock).fontFamily,
          }
          status.textContent = originalStatus
          clock.textContent = originalClock
          return geometry
        }, value)
        expect(Math.abs(geometry.center - geometry.expected)).toBeLessThanOrEqual(0.5)
        expect(Math.abs(geometry.clockRight - geometry.right)).toBeLessThanOrEqual(1)
        expect(geometry.statusLeft).toBeGreaterThanOrEqual(geometry.navRight)
        expect(geometry.statusColor).toBe(geometry.clockColor)
        expect(geometry.ellipsis).toBe('ellipsis')
        expect(geometry.clockFont).toMatch(/Avalon Data|Mono/)
      }
      await connect()
      await expect(page.locator('.avalon-controller-status')).toHaveText('Controller battery medium')
      await page.screenshot({ path: info.outputPath('fullscreen-clock-status.png') })
      await page.locator('.avalon-cover').first().click()
      await expect(page.locator('.avalon-details.fullscreen')).toBeVisible()
      await expect(page.locator('.avalon-header time')).toBeVisible()
      await expect(page.locator('.avalon-header nav')).toHaveCount(0)
      await expect(page.locator('.avalon-controller-status')).toHaveText('Controller battery medium')
      await page.screenshot({ path: info.outputPath('details-clock-status.png') })
      await page.keyboard.press('Escape')
    })

test('every fullscreen root page retains live status and local clock', async () => {
  await fullscreen()
  await nativeReadings(1, 0)
  await connect()
  for (const name of ['For you', 'Library', 'Activity', 'Settings']) {
    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name, exact: true })
      .click()
    await expect(page.locator('.avalon-header time')).toBeVisible()
    await expect(page.locator('.avalon-controller-status')).toHaveText('Wired controller')
    const clock = await page.locator('.avalon-header time').evaluate((node) => ({
      text: node.textContent,
      stamp: node.getAttribute('datetime'),
      label: node.getAttribute('aria-label'),
    }))
    expect(clock.label).toBe(`Local time: ${clock.text}`)
    expect(Math.abs(Date.now() - Date.parse(clock.stamp!))).toBeLessThan(16000)
  }
})

test('External_restore_hides_tv_and_controller_status_omits_unknown_battery', async ({}, info) => {
  await fullscreen()
  await nativeReadings(255, 3)
  await connect()
  await expect(page.locator('.avalon-controller-status')).toHaveText('Controller connected')
  await connect(false)
  await expect(page.locator('.avalon-controller-status')).toHaveText('Controller disconnected')
  await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0]!.setFullScreen(true))
  await expect
    .poll(() => application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0]!.isFullScreen()))
    .toBe(true)
  await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0]!.setFullScreen(false))
  await expect(page.locator('.avalon-shell')).toHaveClass(/desktop/)
  await expect(page.locator('.avalon-system-status')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Enter fullscreen', exact: true })).toBeVisible()
  await expect(
    page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'For you', exact: true }),
  ).toHaveAttribute('aria-current', 'page')
  const counts = await application.evaluate(() => {
    const state = globalThis as unknown as { __controllerStarts: number; __controllerStops: number }
    return [state.__controllerStarts, state.__controllerStops]
  })
  expect(counts[1]).toBe(counts[0])
  await page.screenshot({ path: info.outputPath('desktop-restored.png') })
})
