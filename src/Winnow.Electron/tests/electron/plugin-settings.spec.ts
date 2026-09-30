import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import type { PluginSnapshot, PluginSetting } from '../../src/renderer/api/types'

const field = (key: string, label: string, overrides: Partial<PluginSetting> = {}): PluginSetting => ({
  key,
  label,
  value: '',
  isSecret: false,
  isRequired: false,
  isBoolean: false,
  isAdvanced: false,
  hasStoredSecret: false,
  ...overrides,
})
const artwork: PluginSnapshot = {
  id: 'community-artwork',
  name: 'Community artwork',
  description: 'Covers from the community.',
  version: '2.0',
  capabilities: 'Artwork',
  enabled: true,
  isLoaded: true,
  restartRequired: false,
  status: 'Active',
  canConfigure: true,
  hasAccount: false,
  accountConnected: false,
  settings: [
    field('apiKey', 'API key', { isSecret: true, hasStoredSecret: true }),
    field('language', 'Language', { value: 'en', isRequired: true }),
  ],
}
const xbox: PluginSnapshot = {
  ...artwork,
  id: 'xbox',
  name: 'Xbox',
  version: '1.0',
  capabilities: 'Library',
  hasAccount: true,
  accountHosts: ['login.example.com'],
  settings: [
    field('local', 'Scan installed games', { isBoolean: true, value: 'true' }),
    field('override', 'Application ID override', { isAdvanced: true, value: 'existing-override' }),
    field('secret', 'Advanced secret', { isAdvanced: true, isSecret: true, hasStoredSecret: true }),
  ],
}
async function launch(mode: 'desktop' | 'fullscreen', plugins = [artwork, xbox]) {
  const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-plugin-settings-'))
  const fixture = join(directory, 'plugins.json')
  await writeFile(fixture, JSON.stringify(plugins))
  const application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/plugin-settings-main.mjs'),
      '--data-dir',
      directory,
      '--no-sync',
      '--seed-sample',
    ],
    env: {
      ...Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ),
      WINNOW_PLUGIN_FIXTURE: fixture,
    } as Record<string, string>,
    chromiumSandbox: true,
    timeout: 60000,
  })
  try {
    const page = await application.firstWindow()
    await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
    if (await page.getByRole('dialog', { name: 'Winnow setup', exact: true }).count())
      await page.getByRole('button', { name: 'Skip setup', exact: true }).click()
    await application.evaluate(({ BrowserWindow }, mode) => {
      const window = BrowserWindow.getAllWindows()[0]
      window.setFullScreen(false)
      window.setContentSize(mode === 'desktop' ? 1200 : 1920, mode === 'desktop' ? 688 : 1080)
      window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
    }, mode)
    await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
    const tap = await controller(page)
    await page.getByRole('button', { name: 'Settings', exact: true }).click()
    return { application, page, tap, directory }
  } catch (error) {
    await closeFixture(application, directory)
    throw error
  }
}
async function controller(page: Page) {
  await page.evaluate(() => {
    const state = { pressed: [] as number[] }
    Object.assign(window, { pluginController: state })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          connected: true,
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, index) => ({
            pressed: state.pressed.includes(index),
            value: state.pressed.includes(index) ? 1 : 0,
          })),
        },
      ],
    })
  })
  return async (button: number) => {
    for (const pressed of [[], [button], []])
      await page.evaluate(async (pressed) => {
        ;(window as unknown as { pluginController: { pressed: number[] } }).pluginController.pressed = pressed
        await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
      }, pressed)
  }
}
type Fixture = {
  calls: { path: string; body?: { values?: Record<string, string> } }[]
  failSave: boolean
  failActivation: boolean
  connected: boolean
  holdSignIn: boolean
  releaseSignIn(): void
}
async function calls(application: ElectronApplication) {
  return application.evaluate(
    () => (globalThis as unknown as { __pluginFixture: Fixture }).__pluginFixture.calls,
  )
}
for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} plugin forms preserve source sizing, advanced drafts, masked keyboard input and save focus`, async ({}, info) => {
    const { application, page, tap, directory } = await launch(mode)
    try {
      if (mode === 'fullscreen') {
        await application.evaluate(({ BrowserWindow }) =>
          BrowserWindow.getAllWindows()[0].setContentSize(1000, 800),
        )
        const applicationTab = page.getByRole('button', { name: 'Application', exact: true })
        await applicationTab.focus()
        const bounds = (await applicationTab.boundingBox())!
        expect(bounds.x).toBeGreaterThanOrEqual(0)
        expect(bounds.x + bounds.width).toBeLessThanOrEqual(1000)
        await application.evaluate(({ BrowserWindow }) =>
          BrowserWindow.getAllWindows()[0].setContentSize(1920, 1080),
        )
      } else {
        await application.evaluate(({ BrowserWindow }) =>
          BrowserWindow.getAllWindows()[0].setContentSize(1040, 688),
        )
        const buttons = page.getByRole('navigation', { name: 'Settings section' }).getByRole('button')
        for (const button of await buttons.all()) {
          const bounds = (await button.boundingBox())!
          expect(bounds.x).toBeGreaterThanOrEqual(0)
          expect(bounds.x + bounds.width).toBeLessThanOrEqual(1040)
        }
        await application.evaluate(({ BrowserWindow }) =>
          BrowserWindow.getAllWindows()[0].setContentSize(1200, 688),
        )
      }
      expect((await calls(application)).filter((call) => call.path === '')).toHaveLength(0)
      await page.getByRole('button', { name: 'Plugins', exact: true }).click()
      const card = page.getByRole('region', { name: 'Community artwork settings' })
      await expect(card).toBeVisible()
      const pane = await page.locator('.settings-page').boundingBox(),
        bounds = await card.boundingBox()
      const widthCap = mode === 'desktop' ? 1100 : 1320
      expect(bounds!.width).toBeLessThanOrEqual(widthCap)
      expect(bounds!.width).toBeGreaterThanOrEqual(Math.min(widthCap, pane!.width - 60))
      expect(bounds!.x - pane!.x).toBeCloseTo(24, 0)
      if (mode === 'desktop') {
        await application.evaluate(({ BrowserWindow }) =>
          BrowserWindow.getAllWindows()[0].setContentSize(3456, 900),
        )
        await expect.poll(async () => (await card.boundingBox())!.width).toBe(1100)
        expect(
          (await card.boundingBox())!.x - (await page.locator('.settings-page').boundingBox())!.x,
        ).toBeCloseTo(24, 0)
        await application.evaluate(({ BrowserWindow }) =>
          BrowserWindow.getAllWindows()[0].setContentSize(1200, 688),
        )
      }
      const secret = page.getByLabel('Community artwork API key', { exact: true })
      await expect(secret).toHaveAttribute('type', 'password')
      await secret.focus()
      if (mode === 'fullscreen') {
        await tap(0)
        const keyboard = page.getByRole('dialog', { name: 'Enter text' })
        await expect(keyboard).toBeVisible()
        for (const letter of 'test') await keyboard.getByRole('button', { name: letter, exact: true }).click()
        await expect(keyboard.getByRole('status', { name: 'Current text' })).toHaveText('••••')
        await keyboard.getByRole('button', { name: 'Done', exact: true }).click()
        await expect(keyboard).not.toBeVisible()
        await expect(secret).toBeFocused()
      } else await secret.fill('test-key')
      const save = page.getByRole('button', { name: 'Save Community artwork settings' })
      await save.focus()
      if (mode === 'fullscreen') await tap(0)
      else await page.keyboard.press('Enter')
      await expect(page.getByText('Provider settings saved.')).toBeVisible()
      await expect(secret).toHaveValue('')
      await expect(save).toBeFocused()
      await secret.fill('unsaved-key')
      await page.getByLabel('Community artwork Language', { exact: true }).fill('fr')
      await page.getByRole('tab', { name: 'Xbox' }).click()
      await expect(page.getByLabel('Xbox Application ID override', { exact: true })).not.toBeVisible()
      const toggle = page.getByLabel('Xbox Scan installed games', { exact: true })
      await toggle.focus()
      if (mode === 'fullscreen') await tap(0)
      else await page.keyboard.press('Space')
      await expect(toggle).not.toBeChecked()
      const disclosure = page.getByRole('button', { name: 'Show advanced settings: Xbox' })
      await disclosure.focus()
      if (mode === 'fullscreen') await tap(13)
      else await page.keyboard.press('Tab')
      await expect(page.getByRole('button', { name: 'Save Xbox settings' })).toBeFocused()
      await page.getByRole('button', { name: 'Show advanced settings: Xbox' }).click()
      const override = page.getByLabel('Xbox Application ID override', { exact: true })
      await override.focus()
      if (mode === 'fullscreen') {
        await tap(0)
        await expect(page.getByRole('dialog', { name: 'Enter text' })).toBeVisible()
        await tap(1)
        await expect(override).toBeFocused()
      }
      await override.fill('new-override')
      await page.getByLabel('Xbox Advanced secret', { exact: true }).fill('replacement')
      await page.getByRole('button', { name: 'Hide advanced settings: Xbox' }).click()
      await page.getByRole('button', { name: 'Save Xbox settings' }).click()
      await expect
        .poll(
          async () => (await calls(application)).find((call) => call.path === '/xbox/settings')?.body?.values,
        )
        .toEqual({ local: 'false', override: 'new-override', secret: 'replacement' })
      await page.getByRole('button', { name: 'Show advanced settings: Xbox' }).click()
      await expect(page.getByLabel('Xbox Advanced secret', { exact: true })).toHaveValue('')
      await override.fill('')
      await page.getByRole('button', { name: 'Hide advanced settings: Xbox' }).click()
      await page.getByRole('button', { name: 'Save Xbox settings' }).click()
      await expect
        .poll(
          async () =>
            (await calls(application)).filter((call) => call.path === '/xbox/settings').at(-1)?.body?.values,
        )
        .toEqual({ local: 'false', override: '' })
      await page.getByRole('button', { name: 'Show advanced settings: Xbox' }).click()
      await page.getByRole('tab', { name: 'Community artwork' }).click()
      await page.getByRole('tab', { name: 'Xbox' }).click()
      await expect(override).not.toBeVisible()
      await page.getByRole('tab', { name: 'Community artwork' }).click()
      await expect(page.getByLabel('Community artwork API key', { exact: true })).toHaveValue('')
      await expect(page.getByLabel('Community artwork Language', { exact: true })).toHaveValue('fr')
      const activation = page.getByRole('checkbox', { name: 'Disable plugin: Community artwork' })
      await activation.focus()
      if (mode === 'fullscreen') await tap(0)
      else await page.keyboard.press('Space')
      await expect(page.getByRole('checkbox', { name: 'Enable plugin: Community artwork' })).not.toBeChecked()
      await expect(page.getByRole('tab', { name: 'Community artwork' })).toBeVisible()
      await expect(page.getByRole('button', { name: 'Refresh Community artwork' })).toBeDisabled()
      await application.evaluate(() => {
        ;(globalThis as unknown as { __pluginFixture: Fixture }).__pluginFixture.failActivation = true
      })
      const enable = page.getByRole('checkbox', { name: 'Enable plugin: Community artwork' })
      await expect(enable).toBeEnabled()
      await enable.focus()
      if (mode === 'fullscreen') await tap(0)
      else await page.keyboard.press('Space')
      await expect(page.getByRole('alert')).toContainText('Could not change this plugin.')
      await expect(enable).not.toBeChecked()
      await expect(page.getByRole('alert')).not.toContainText('private activation detail')
      await page.getByRole('button', { name: 'Remove saved Community artwork API key' }).click()
      await expect(
        page.getByRole('button', { name: 'Remove saved Community artwork API key' }),
      ).toBeDisabled()
      await application.evaluate(() => {
        ;(globalThis as unknown as { __pluginFixture: Fixture }).__pluginFixture.failSave = true
      })
      await page.getByRole('button', { name: 'Save Community artwork settings' }).click()
      await expect(page.getByRole('alert')).toContainText('Could not save plugin settings.')
      await expect(page.getByRole('alert')).not.toContainText('private storage detail')
      await page.screenshot({ path: info.outputPath(`${mode}-plugin-form.png`) })
      if (mode === 'fullscreen') {
        await page.getByRole('button', { name: 'Metadata & artwork', exact: true }).click()
        await tap(7)
        await expect(page.getByRole('tab', { name: 'Community artwork' })).toBeVisible()
        await tap(7)
        await expect(page.getByRole('button', { name: 'Application', exact: true })).toHaveAttribute(
          'aria-pressed',
          'true',
        )
        await tap(6)
      }
      await page.getByRole('tab', { name: 'Manage plugins' }).click()
      await expect(page.getByText('Community artwork · 2.0')).toBeVisible()
      await expect(page.getByText(/ZIPs unpack automatically/)).toBeVisible()
      await page.screenshot({ path: info.outputPath(`${mode}-plugin-management.png`) })
      const beforeRestart = (await calls(application)).filter((call) => call.path === '').length
      const restart = page.getByRole('button', { name: 'Restart library service', exact: true })
      await restart.focus()
      if (mode === 'fullscreen') await tap(0)
      else await page.keyboard.press('Enter')
      await expect(restart).toBeDisabled()
      await expect(
        page.getByText('The library service restarted. Provider changes are now applied.'),
      ).toBeVisible()
      await expect(restart).toBeEnabled()
      expect((await calls(application)).filter((call) => call.path === '').length).toBeGreaterThan(
        beforeRestart,
      )
    } finally {
      await closeFixture(application, directory)
    }
  })
  for (const [width, height, scale] of mode === 'desktop'
    ? [
        [700, 700, 1],
        [1280, 820, 1],
      ]
    : [
        [1280, 720, 1],
        [1920, 1080, 1.4],
      ])
    test(`${mode} twelve plugin tabs preserve selection and keyboard navigation at ${width}x${height} text ${scale}`, async ({}, info) => {
      const plugins = Array.from({ length: 12 }, (_, index) => ({
        ...artwork,
        id: `artwork-${index}`,
        name: `Community provider ${String(index + 1).padStart(2, '0')}`,
      }))
      plugins.push(
        { ...artwork, id: 'disabled', name: 'Disabled provider', enabled: false, isLoaded: false },
        {
          ...artwork,
          id: 'broken',
          name: 'Broken package',
          enabled: false,
          isLoaded: false,
          canConfigure: false,
          status: 'The package could not be loaded.',
        },
      )
      const { application, page, tap, directory } = await launch(mode, plugins)
      try {
        await application.evaluate(
          ({ BrowserWindow }, size) => {
            const window = BrowserWindow.getAllWindows()[0]
            window.setMinimumSize(640, 500)
            window.setContentSize(size[0], size[1])
          },
          [width, height],
        )
        await page.evaluate(async (scale) => {
          const response = await window.winnow.request({
            route: 'preferences.presentation.put',
            params: { preference: 'FullscreenTextScale' },
            body: { value: String(scale) },
          })
          if (!response.ok) throw Error('Could not save fixture text scale')
        }, scale)
        await page.getByRole('button', { name: 'Plugins', exact: true }).click()
        const first = page.getByRole('tab', { name: 'Community provider 01' }),
          last = page.getByRole('tab', { name: 'Manage plugins' })
        await expect(page.getByRole('tab')).toHaveCount(13)
        await expect
          .poll(() =>
            page.getByRole('tab').evaluateAll((tabs) =>
              tabs.every((tab) => {
                const range = document.createRange()
                range.selectNodeContents(tab)
                const text = range.getBoundingClientRect(),
                  box = tab.getBoundingClientRect()
                return text.left >= box.left + 10 && text.right <= box.right - 10
              }),
            ),
          )
          .toBe(true)
        await page.getByLabel('Community provider 01 Language', { exact: true }).fill('Draft language')
        await page.getByLabel('Community provider 01 API key', { exact: true }).fill('Private draft')
        await expect(first).toHaveAttribute('aria-selected', 'true')
        const right = page.getByRole('button', { name: 'Scroll plugin tabs right' }),
          left = page.getByRole('button', { name: 'Scroll plugin tabs left' })
        await expect(left).toBeDisabled()
        await expect(right).toBeEnabled()
        await right.focus()
        if (mode === 'desktop') await page.keyboard.press('Enter')
        else await tap(0)
        await expect(first).toHaveAttribute('aria-selected', 'true')
        await expect(left).toBeEnabled()
        await first.focus()
        await page.keyboard.press('End')
        await expect(last).toBeFocused()
        const visible = await page.getByRole('tablist', { name: 'Plugin settings' }).boundingBox(),
          tab = await last.boundingBox()
        expect(tab!.x).toBeGreaterThanOrEqual(visible!.x - 1)
        expect(tab!.x + tab!.width).toBeLessThanOrEqual(visible!.x + visible!.width + 1)
        if (mode === 'fullscreen') {
          await expect(first).toHaveAttribute('aria-selected', 'true')
          await tap(0)
        }
        await expect(last).toHaveAttribute('aria-selected', 'true')
        await expect(right).toBeDisabled()
        await expect(page.getByRole('region', { name: 'Disabled provider settings' })).toBeVisible()
        await expect(page.getByText('The package could not be loaded.')).toBeVisible()
        await page.screenshot({ path: info.outputPath(`${mode}-plugin-overflow.png`) })
        await last.focus()
        await page.keyboard.press('Home')
        if (mode === 'fullscreen') await tap(0)
        await expect(first).toHaveAttribute('aria-selected', 'true')
        await expect(left).toBeDisabled()
        await expect(page.getByLabel('Community provider 01 Language', { exact: true })).toHaveValue(
          'Draft language',
        )
        await expect(page.getByLabel('Community provider 01 API key', { exact: true })).toHaveValue('')
        if (mode === 'fullscreen') {
          await first.focus()
          await tap(13)
          await expect(
            page.getByRole('checkbox', { name: 'Disable plugin: Community provider 01' }),
          ).toBeFocused()
        } else {
          await first.focus()
          await page.keyboard.press('ArrowRight')
          await expect(page.getByRole('tab', { name: 'Community provider 02' })).toHaveAttribute(
            'aria-selected',
            'true',
          )
        }
      } finally {
        await closeFixture(application, directory)
      }
    })
  test(`${mode} resize alone hides unused tab arrows and keeps the selected tab visible when narrowed again`, async () => {
    const plugins = [1, 2].map((index) => ({
      ...artwork,
      id: `artwork-${index}`,
      name: `Community provider ${String(index).padStart(2, '0')}`,
    }))
    const { application, page, directory } = await launch(mode, plugins)
    const resize = (width: number) =>
      application.evaluate(({ BrowserWindow }, width) => {
        const window = BrowserWindow.getAllWindows()[0]
        window.setMinimumSize(640, 500)
        window.setContentSize(width, 1000)
      }, width)
    try {
      await resize(mode === 'desktop' ? 640 : 900)
      await page.getByRole('button', { name: 'Plugins', exact: true }).click()
      const next = page.getByRole('button', { name: 'Scroll plugin tabs right' })
      await expect(page.getByRole('tab', { name: 'Community provider 01' })).toBeVisible()
      const tabWidths = await page
        .getByRole('tablist', { name: 'Plugin settings' })
        .evaluate((element) => ({ width: element.clientWidth, extent: element.scrollWidth }))
      expect(tabWidths.extent, JSON.stringify(tabWidths)).toBeGreaterThan(tabWidths.width)
      await expect(next).toBeVisible()
      await page.getByRole('tab', { name: 'Manage plugins' }).click()
      await resize(mode === 'desktop' ? 1600 : 3000)
      await expect(next).not.toBeVisible()
      await expect(page.getByRole('tab', { name: 'Manage plugins' })).toHaveAttribute('aria-selected', 'true')
      await resize(mode === 'desktop' ? 640 : 900)
      await expect(next).toBeVisible()
      const strip = page.getByRole('tablist', { name: 'Plugin settings' }),
        selected = page.getByRole('tab', { name: 'Manage plugins' })
      await expect
        .poll(async () => {
          const parent = (await strip.boundingBox())!,
            child = (await selected.boundingBox())!
          return child.x >= parent.x - 1 && child.x + child.width <= parent.x + parent.width + 1
        })
        .toBe(true)
    } finally {
      await closeFixture(application, directory)
    }
  })
  test(`${mode} plugin device sign-in exposes code and URL, cancels on departure and publishes connected state`, async () => {
    const { application, page, directory } = await launch(mode)
    try {
      await page.getByRole('button', { name: 'Plugins', exact: true }).click()
      await page.getByRole('tab', { name: 'Xbox' }).click()
      await page.getByRole('button', { name: 'Sign in to Xbox' }).click()
      await expect(page.getByLabel('Sign-in code: ABCD-EFGH', { exact: true })).toBeVisible()
      await expect(page.getByText('https://login.example.com/device')).toBeVisible()
      await expect(page.getByLabel('Xbox Scan installed games', { exact: true })).toBeDisabled()
      await page.getByRole('tab', { name: 'Manage plugins' }).click()
      await expect
        .poll(
          async () =>
            (await calls(application)).filter((call) => call.path === '/xbox/sign-in/cancel').length,
        )
        .toBe(1)
      await page.getByRole('tab', { name: 'Xbox' }).click()
      await application.evaluate(() => {
        ;(globalThis as unknown as { __pluginFixture: Fixture }).__pluginFixture.connected = true
      })
      await page.getByRole('button', { name: 'Sign in to Xbox' }).click()
      const signout = page.getByRole('button', { name: 'Sign out of Xbox' })
      await expect(signout).toBeEnabled()
      await expect(page.getByText('Signed in. Refresh queued.')).toBeVisible()
      await signout.click()
      await expect(page.getByText('Signed out. Imported games remain in your library.')).toBeVisible()
      await expect(page.getByRole('button', { name: 'Sign in to Xbox' })).toBeEnabled()
    } finally {
      await closeFixture(application, directory)
    }
  })
}
