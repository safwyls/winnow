import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Page,
  type Locator,
} from '@playwright/test'
import electronPath from 'electron'
import { mkdtemp, mkdir, readFile, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { prebuiltActivationHelper, prebuiltFixture } from './prebuilt-backend'
import { closeFixture } from './fixture-cleanup'
import { profileDirectory } from '../../src/main/storage'
import { DEFAULT_PROFILE } from '../../src/shared/theme'

let application: ElectronApplication, page: Page, directory: string
let endpoint: { address: string; token: string }
let mode: 'desktop' | 'fullscreen'
const errors: string[] = []
async function control(action: string, body?: unknown) {
  const response = await fetch(new URL(`/__fixture/diagnostics/${action}`, endpoint.address), {
    method: body === undefined ? 'GET' : 'POST',
    headers: { Authorization: `Bearer ${endpoint.token}`, 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!response.ok) throw Error(`Diagnostics fixture ${action}: ${response.status}`)
  return response.status === 204 ? null : response.json()
}
async function progress(total: number, remaining: number) {
  await control('progress', { total, remaining })
}
async function tap(button: number) {
  for (const pressed of [[], [button], []])
    await page.evaluate(async (pressed) => {
      ;(window as any).__diagnosticsPad.pressed = pressed
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
}
async function activate(target: Locator) {
  await target.focus()
  await expect(target).toBeFocused()
  if (mode === 'fullscreen') await tap(0)
  else if (await target.evaluate((node) => node instanceof HTMLInputElement && node.type === 'checkbox'))
    await page.keyboard.press('Space')
  else await page.keyboard.press('Enter')
}
async function settings() {
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
  await expect(page.getByRole('region', { name: 'About Winnow', exact: true })).toBeVisible()
}
async function capture(name: string) {
  await page.screenshot({ path: test.info().outputPath(`${name}.png`) })
}
async function nativeState() {
  return application.evaluate(() => (globalThis as any).__diagnosticsNative)
}

test.beforeAll(async () => {
  await Promise.all([readFile(prebuiltFixture), readFile(prebuiltActivationHelper)])
})
test.beforeEach(async ({}, info) => {
  application = undefined!
  page = undefined!
  endpoint = undefined!
  errors.length = 0
  mode = info.title.startsWith('fullscreen') ? 'fullscreen' : 'desktop'
  directory = await mkdtemp(join(resolve('../..', '.tmp'), `winnow-electron-diagnostics-${mode}-`))
  const host = join(directory, 'test-host-app')
  await mkdir(host)
  const metadata = JSON.parse(await readFile('package.json', 'utf8'))
  await writeFile(
    join(host, 'package.json'),
    JSON.stringify({
      name: metadata.name,
      productName: metadata.productName,
      version: metadata.version,
      type: 'module',
      main: resolve('tests/electron/diagnostics-main.mjs'),
    }),
  )
  const profile = structuredClone(DEFAULT_PROFILE)
  profile.appearance.scale = 100
  profile.appearance.reducedMotion = true
  const profileRoot = profileDirectory(join(directory, 'electron-userdata'), directory)
  await mkdir(profileRoot, { recursive: true })
  await writeFile(join(profileRoot, 'preferences.json'), JSON.stringify(profile))
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [host, '--data-dir', directory, '--no-sync', '--force-color-profile=srgb'],
    env: {
      ...(Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ) as Record<string, string>),
      WINNOW_BACKEND_PATH: prebuiltFixture,
      WINNOW_ACTIVATION_HELPER_PATH: prebuiltActivationHelper,
    },
    chromiumSandbox: true,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.locator('.avalon-shell')).toBeVisible({ timeout: 30000 })
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
  expect((await fetch(new URL('/__fixture/diagnostics/state', endpoint.address))).status).toBe(401)
  await application.evaluate(({ BrowserWindow }, mode) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setMinimumSize(0, 0)
    window.setContentSize(mode === 'desktop' ? 1200 : 1920, mode === 'desktop' ? 688 : 1080)
    if (mode === 'fullscreen') window.isFullScreen = () => true
    window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
    window.focus()
  }, mode)
  await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
  await page.evaluate(() => {
    const state = ((window as any).__diagnosticsPad = { pressed: [] as number[] })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          id: 'Diagnostics standard simulated controller',
          connected: true,
          mapping: 'standard',
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, i) => ({
            pressed: state.pressed.includes(i),
            touched: state.pressed.includes(i),
            value: state.pressed.includes(i) ? 1 : 0,
          })),
        },
      ],
    })
    window.dispatchEvent(new Event('gamepadconnected'))
  })
})
test.afterEach(async () => {
  try {
    if (endpoint)
      await test.info().attach('diagnostics-backend-state', {
        body: JSON.stringify(await control('state')),
        contentType: 'application/json',
      })
    if (application)
      await test.info().attach('diagnostics-native-boundaries', {
        body: JSON.stringify(await nativeState()),
        contentType: 'application/json',
      })
    if (test.info().status !== test.info().expectedStatus && page && !page.isClosed())
      await capture('diagnostics-before-teardown')
  } finally {
    await closeFixture(application, directory)
  }
  expect(errors).toEqual([])
})

test('desktop source 1200 by 688 fetch caption preserves exact 997 singular fullscreen round trip and modal input', async () => {
  const caption = page.locator('.fetch-status')
  await expect(caption).toHaveCount(0)
  const origin = page.getByRole('button', { name: 'Search library', exact: true })
  await origin.focus()
  await expect(origin).toBeFocused()
  const readsBefore = (await control('state')).requests
  await progress(997, 997)
  await expect(caption).toHaveAccessibleName('Fetching details, 997 titles left')
  await expect(origin).toBeFocused()
  await expect(caption.locator('.fetch-status-count')).toHaveText('997')
  const readsAfter = (await control('state')).requests
  for (const path of ['/api/v1/library', '/api/v1/library/workspace', '/api/v1/feed'])
    expect(readsAfter[path] ?? 0).toBe(readsBefore[path] ?? 0)
  await expect(caption).toHaveAttribute('title', 'Fetching details, 997 titles left')
  const geometry = await caption.evaluate((element) => {
    const parent = element.closest('.avalon-header')!,
      bounds = element.getBoundingClientRect(),
      header = parent.getBoundingClientRect()
    return {
      bounds: bounds.toJSON(),
      header: header.toJSON(),
      scrollWidth: element.scrollWidth,
      clientWidth: element.clientWidth,
      pointer: getComputedStyle(element).pointerEvents,
      drag: getComputedStyle(element).getPropertyValue('-webkit-app-region'),
      hit: element.contains(
        document.elementFromPoint(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2),
      ),
      focusable: element.querySelectorAll('button,input,a,[tabindex]').length,
      tabindex: element.getAttribute('tabindex'),
      animation: getComputedStyle(element).animationName,
      transition: getComputedStyle(element).transitionDuration,
      fonts: ['label', 'count', 'note'].map((part) =>
        Number.parseFloat(getComputedStyle(element.querySelector(`.fetch-status-${part}`)!).fontSize),
      ),
    }
  })
  expect(geometry.bounds.height).toBeGreaterThan(0)
  expect(geometry.bounds.height).toBeLessThanOrEqual(geometry.header.height)
  expect(geometry.bounds.x).toBeGreaterThanOrEqual(geometry.header.x)
  expect(geometry.bounds.right).toBeLessThanOrEqual(geometry.header.right)
  expect(geometry.clientWidth).toBeGreaterThanOrEqual(geometry.scrollWidth - 1)
  expect(geometry.pointer).toBe('none')
  expect(geometry.drag).toBe('drag')
  expect(geometry.hit).toBe(false)
  expect(geometry.focusable).toBe(0)
  expect(geometry.tabindex).toBeNull()
  expect(geometry.animation).toBe('none')
  expect(geometry.transition).toBe('0s')
  expect(geometry.fonts).toEqual([10, 10, 11])
  await capture('desktop-997-caption-1200x688')
  await page.evaluate(() => window.winnow.setFullscreen(true))
  await expect(page.locator('.avalon-shell.fullscreen')).toBeVisible()
  await expect(caption).toHaveCount(0)
  await progress(997, 1)
  await expect
    .poll(
      async () =>
        ((await page.evaluate(() => window.winnow.request({ route: 'progress.get' }))) as any).data
          ?.remaining,
    )
    .toBe(1)
  await page.evaluate(() => window.winnow.setFullscreen(false))
  await expect(page.locator('.avalon-shell.desktop')).toBeVisible()
  await expect(caption).toHaveAttribute('data-item-status', 'Fetching details, 1 title left')
  await expect(caption.locator('.fetch-status-note')).toHaveText('title left')
  await progress(997, 0)
  await expect(caption).toHaveCount(0)
  await activate(page.getByRole('button', { name: 'New list', exact: true }))
  await activate(page.getByRole('menuitem', { name: 'Static list', exact: true }))
  const dialog = page.getByRole('dialog', { name: 'Name this list', exact: true })
  const input = dialog.getByRole('textbox', { name: 'List name', exact: true })
  await input.fill('Still editing')
  const retained = await input.elementHandle()
  await progress(1247, 1247)
  await expect(caption).toHaveAttribute('aria-label', 'Fetching details, 1,247 titles left')
  await expect(input).toBeFocused()
  await expect(input).toHaveValue('Still editing')
  expect(await input.evaluate((node, original) => node === original, retained)).toBe(true)
  await progress(80, 40)
  await expect(caption.locator('.fetch-status-count')).toHaveText('40')
  await page.keyboard.type(' through progress')
  await expect(input).toHaveValue('Still editing through progress')
  await capture('desktop-progress-keeps-modal-draft')
  await page.keyboard.press('Escape')
  await expect(dialog).toHaveCount(0)
  await progress(80, 0)
  await expect(caption).toHaveCount(0)
  await test
    .info()
    .attach('source-caption-geometry', { body: JSON.stringify(geometry), contentType: 'application/json' })
})

for (const surface of ['desktop', 'fullscreen'] as const) {
  test(`${surface} watcher recovery retains Settings logs and failed folder open exposes the exact path until successful retry`, async () => {
    const origin = page.getByRole('button', { name: 'Search library', exact: true })
    await origin.focus()
    await control('health', { failure: true })
    const notice = page.locator('.session-health-notice')
    await expect(notice).toBeVisible()
    await expect(origin).toBeFocused()
    await control('health', { failure: false })
    await expect(notice).toHaveCount(0)
    await settings()
    const logs = page.getByRole('button', { name: 'Open logs folder', exact: true })
    await expect(logs).toBeVisible()
    await application.evaluate(() => {
      ;(globalThis as any).__diagnosticsNative.folderFailure = true
    })
    await activate(logs)
    const failure = page.getByRole('alert').filter({ hasText: "Couldn't open the logs folder." })
    await expect(failure).toHaveText(
      `Couldn't open the logs folder. Open it manually: ${join(directory, 'logs')}`,
    )
    await expect(failure).not.toContainText('Error invoking remote method')
    await expect(failure).not.toContainText('winnow:folder')
    expect((await nativeState()).folders).toEqual([join(directory, 'logs')])
    await failure.scrollIntoViewIfNeeded()
    await capture(`${surface}-logs-manual-path`)
    await application.evaluate(() => {
      ;(globalThis as any).__diagnosticsNative.folderFailure = false
    })
    await activate(logs)
    await expect(failure).toHaveCount(0)
    expect((await nativeState()).folders).toEqual([join(directory, 'logs'), join(directory, 'logs')])
    await expect(logs).toBeFocused()
    await capture(`${surface}-logs-recovered`)
  })

  test(`${surface} captured Windows autostart reads and writes the same executable and background isolated-root arguments`, async () => {
    await settings()
    const toggle = page.getByRole(surface === 'desktop' ? 'checkbox' : 'switch', {
      name: /^Start with Windows/,
    })
    await expect(toggle).not.toBeChecked()
    await activate(toggle)
    await expect(toggle).toBeChecked()
    await expect(toggle).toBeEnabled()
    await activate(toggle)
    await expect(toggle).not.toBeChecked()
    await expect(toggle).toBeEnabled()
    const state = await nativeState()
    const path = await application.evaluate(() => process.execPath)
    const expected = { path, args: ['--background', '--data-dir', directory] }
    expect(state.loginReads.length).toBeGreaterThanOrEqual(3)
    for (const options of state.loginReads) expect(options).toEqual(expected)
    expect(state.loginWrites).toEqual([
      { ...expected, openAtLogin: true },
      { ...expected, openAtLogin: false },
    ])
    await capture(`${surface}-autostart-captured`)
  })
}
