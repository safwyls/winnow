import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Page,
  type Locator,
} from '@playwright/test'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import { assertAccessibleControls, assertDirectionalReachability } from './controller-accessibility-helpers'
import { prebuiltBackend, prebuiltActivationHelper } from './prebuilt-backend'

let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-accessibility-settings-'))
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('.'), '--data-dir', directory, '--seed-sample', '--no-sync'],
    env: {
      ...(Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ) as Record<string, string>),
      WINNOW_BACKEND_PATH: prebuiltBackend,
      WINNOW_ACTIVATION_HELPER_PATH: prebuiltActivationHelper,
    },
    chromiumSandbox: true,
    timeout: 60000,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible({ timeout: 45000 })
})
test.afterAll(async () => closeFixture(application, directory))
test.afterEach(async ({}, info) => {
  if (info.status !== info.expectedStatus)
    await info.attach('accessibility-failure', { body: await page.screenshot(), contentType: 'image/png' })
  expect(errors).toEqual([])
})

async function tap(button: number) {
  for (const pressed of [[], [button], []])
    await page.evaluate(async (pressed) => {
      ;(window as unknown as { accessibilityPad: { pressed: number[] } }).accessibilityPad.pressed = pressed
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
}

async function surface(mode: 'desktop' | 'fullscreen') {
  await page.reload()
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
  await application.evaluate(({ BrowserWindow }, mode) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setContentSize(1920, 1080)
    window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
    window.focus()
  }, mode)
  await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
  await page.evaluate(() => {
    const state = { pressed: [] as number[] }
    Object.assign(window, { accessibilityPad: state })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          connected: true,
          mapping: 'standard',
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, index) => ({
            pressed: state.pressed.includes(index),
            value: state.pressed.includes(index) ? 1 : 0,
          })),
        },
      ],
    })
  })
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
  await expect(page.locator('[data-avalon-game]').first()).toBeVisible()
}

async function screen(name: string, mode: 'desktop' | 'fullscreen'): Promise<Locator> {
  if (name === 'Search') {
    await page.getByRole('button', { name: 'Search library', exact: true }).click()
    const scope = page.locator(mode === 'fullscreen' ? '.avalon-search-page' : '.avalon-toolbar')
    await expect(
      scope
        .getByRole('searchbox', { name: 'Search games' })
        .or(scope.getByRole('textbox', { name: 'Search games' })),
    ).toBeVisible()
    return scope
  }
  if (name === 'Filters') {
    await page
      .getByRole('button', { name: mode === 'fullscreen' ? 'Filter & sort' : 'Filters', exact: true })
      .click()
    const scope = page.getByRole(mode === 'fullscreen' ? 'dialog' : 'region', {
      name: 'Library filters',
      exact: true,
    })
    await expect(scope).toBeVisible()
    return scope
  }
  await page.getByRole('button', { name: 'Settings', exact: true }).click()
  await page
    .getByRole('navigation', { name: 'Settings section' })
    .getByRole('button', { name: name === 'Steam key' ? 'Platforms' : name, exact: true })
    .click()
  const scope = page.locator('.settings-page')
  if (name === 'Platforms' || name === 'Steam key') {
    if (mode === 'fullscreen') {
      const summary = page.locator('.fullscreen-platform-summary')
      await expect(summary.getByRole('button', { name: 'Steam', exact: true })).toBeVisible()
      if (name === 'Platforms') return summary
      await summary.getByRole('button', { name: 'Steam', exact: true }).click()
      await page.getByRole('button', { name: 'Steam Web API key', exact: true }).click()
      await expect(
        page.getByLabel('Steam Web API key', { exact: true }).and(page.locator('input')),
      ).toBeVisible()
      return page.getByRole('region', { name: 'Steam Web API key', exact: true })
    }
    await expect(
      page.getByLabel('Steam Web API key', { exact: true }).and(page.locator('input')),
    ).toBeVisible()
    if (name === 'Steam key') return page.getByRole('region', { name: 'Steam API key method', exact: true })
  }
  if (name === 'Plugins') await expect(scope.getByText('Reading plugins…', { exact: true })).toHaveCount(0)
  if (name === 'Appearance' && mode === 'fullscreen')
    await expect(scope.getByRole('button', { name: 'Text size', exact: true })).toBeEnabled()
  if (name === 'Library')
    await expect(
      scope.getByRole(mode === 'fullscreen' ? 'switch' : 'checkbox', {
        name: mode === 'fullscreen' ? 'Journal after playing' : /Ask for a note after playing/,
      }),
    ).toBeVisible()
  return scope
}

const sourceScreens = [
  'Appearance',
  'Controller',
  'Library',
  'Platforms',
  'Metadata & artwork',
  'Plugins',
  'Application',
  'Steam key',
  'Search',
  'Filters',
] as const
for (const mode of ['fullscreen', 'desktop'] as const)
  for (const name of sourceScreens) {
    // Desktop has no Controller section; its controller guide is part of the fullscreen presentation.
    if (mode === 'desktop' && name === 'Controller') continue
    test(`${mode} ${name} exposes named enabled states and complete D-pad button routes at 1920x1080`, async ({}, info) => {
      test.setTimeout(180000)
      await surface(mode)
      const scope = await screen(name, mode)
      const nodes = await assertAccessibleControls(page, scope)
      const routes = await assertDirectionalReachability(page, scope, tap)
      await info.attach('accessible-controls-and-routes', {
        body: JSON.stringify({ nodes, routes }, null, 2),
        contentType: 'application/json',
      })
      await scope.evaluate((node) => node.scrollTo({ top: 0 }))
      await page.screenshot({
        path: info.outputPath(`${mode}-${name.replaceAll(/[^a-z0-9]/gi, '-')}-accessible.png`),
      })
    })
  }
