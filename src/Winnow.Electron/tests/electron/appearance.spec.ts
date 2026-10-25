import { chooseOption, choosePalette, paletteSwatch } from './choice-test-utils'
import { closeFixture } from './fixture-cleanup'
import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { access, mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { prebuiltActivationHelper, prebuiltBackend } from './prebuilt-backend'

let application: ElectronApplication
let page: Page
let directory: string
const errors: string[] = []
test.beforeAll(async () => {
  const root = resolve('../..', '.tmp')
  await access(root)
  directory = await mkdtemp(join(root, 'winnow-electron-appearance-'))
  const environment = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('.'), '--data-dir', directory, '--seed-sample', '--no-sync'],
    env: {
      ...environment,
      WINNOW_BACKEND_PATH: prebuiltBackend,
      WINNOW_ACTIVATION_HELPER_PATH: prebuiltActivationHelper,
    },
    chromiumSandbox: true,
    timeout: 60000,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.locator('.avalon-shell')).toBeVisible()
})

test('desktop material requests preserve opaque covers and one coat per pane, with flush/floating geometry', async () => {
  await application.evaluate(({ BrowserWindow }) => {
    const window = BrowserWindow.getAllWindows()[0]
    window.setFullScreen(false)
    window.setContentSize(1440, 1000)
    window.webContents.send('winnow:fullscreen:changed', false)
  })
  await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
  const slider = page.getByRole('slider', { name: 'Transparency', exact: true })
  await expect(slider).toBeEnabled()
  await slider.fill('100')
  await expect(slider).toHaveValue('100')
  await chooseOption(page.getByRole('button', { name: 'Backdrop', exact: true }), 'acrylic')
  const result = await page.evaluate(() =>
    window.winnow.windowAppearance!({ enabled: true, material: 'acrylic', background: '#0F1C1E' }),
  )
  await expect(page.locator('html')).toHaveAttribute('data-avalon-material', result.requested)
  const shell = page.locator('.avalon-shell')
  if (result.requested !== 'none') {
    await expect(shell).toHaveCSS('--avalon-shell-ground', '#040C0D26')
    await expect(shell).toHaveCSS('--avalon-pane-ground', '#0F1C1E96')
    await expect(page.locator('html')).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)')
  } else {
    await expect(shell).toHaveCSS('--avalon-pane-ground', '#0F1C1EFF')
  }
  expect(
    await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].getOpacity()),
  ).toBe(1)
  await chooseOption(page.getByRole('button', { name: 'Pane layout', exact: true }), 'floating')
  await expect(shell).toHaveAttribute('data-pane-layout', 'floating')
  await page.getByRole('button', { name: 'Winnow home', exact: true }).click()
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
  const floating = await page.evaluate(() => {
    const rail = document.querySelector('.avalon-rail')!.getBoundingClientRect()
    const pane = document.querySelector('.avalon-content')!.getBoundingClientRect()
    return {
      gap: pane.left - rail.right,
      cover: getComputedStyle(document.querySelector('.avalon-cover')!).backgroundColor,
    }
  })
  expect(floating.gap).toBeGreaterThan(0)
  expect(floating.cover).toMatch(/^rgb\(/)
  await page.screenshot({ path: join(directory, 'desktop-floating-acrylic.png') })
  await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
  await chooseOption(page.getByRole('button', { name: 'Pane layout', exact: true }), 'flush')
  await expect(shell).toHaveAttribute('data-pane-layout', 'flush')
  await chooseOption(page.getByRole('button', { name: 'Backdrop', exact: true }), 'mica')
  await expect(page.locator('html')).toHaveAttribute(
    'data-avalon-material',
    result.supported ? 'mica' : 'none',
  )
  await page.getByRole('button', { name: 'Winnow home', exact: true }).click()
  const flushGap = await page.evaluate(
    () =>
      document.querySelector('.avalon-content')!.getBoundingClientRect().left -
      document.querySelector('.avalon-rail')!.getBoundingClientRect().right,
  )
  expect(flushGap).toBeCloseTo(0, 1)
  await page.screenshot({ path: join(directory, 'desktop-flush-mica.png') })
})

test('fullscreen and disabled transparency restore solid surfaces while saved material and reach survive reload', async () => {
  await application.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0].webContents.send('winnow:fullscreen:changed', true),
  )
  await expect(page.locator('.avalon-shell')).toHaveClass(/fullscreen/)
  await expect(page.locator('html')).toHaveAttribute('data-avalon-material', 'none')
  await expect(page.locator('.avalon-shell')).toHaveCSS('--avalon-pane-ground', '#0F1C1EFF')
  expect(
    await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].getOpacity()),
  ).toBe(1)
  await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
  await expect(page.getByRole('slider', { name: 'Transparency', exact: true })).toHaveValue('100')
  await expect(page.getByRole('button', { name: 'Backdrop', exact: true })).toHaveJSProperty('value', 'mica')
  await page.getByRole('slider', { name: 'Transparency', exact: true }).fill('0')
  await expect(page.getByRole('slider', { name: 'Transparency', exact: true })).toHaveValue('0')
  await page.getByRole('checkbox', { name: /Include content panes/ }).click()
  await expect(page.getByRole('checkbox', { name: /Include content panes/ })).not.toBeChecked()
  await page.reload()
  await expect(page.locator('.avalon-shell')).toBeVisible()
  await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
  await expect(page.getByRole('slider', { name: 'Transparency', exact: true })).toHaveValue('0')
  await expect(page.getByRole('button', { name: 'Backdrop', exact: true })).toHaveJSProperty('value', 'mica')
  await expect(page.getByRole('button', { name: 'Pane layout', exact: true })).toHaveJSProperty(
    'value',
    'flush',
  )
  await expect(page.getByRole('checkbox', { name: /Include content panes/ })).not.toBeChecked()
  await expect(page.locator('html')).toHaveAttribute('data-avalon-material', 'none')
  expect(errors).toEqual([])
})
for (const fullscreen of [false, true])
  test(`${fullscreen ? 'fullscreen' : 'desktop'} transparency follows a continuous pointer drag and persists the keyboard-adjusted value`, async () => {
    await application.evaluate(({ BrowserWindow }, fullscreen) => {
      const window = BrowserWindow.getAllWindows()[0]
      window.setFullScreen(false)
      window.setContentSize(1440, 1000)
      window.webContents.send('winnow:fullscreen:changed', fullscreen)
    }, fullscreen)
    await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
    const slider = page.getByRole('slider', { name: 'Transparency', exact: true })
    await slider.fill('20')
    await slider.scrollIntoViewIfNeeded()
    const bounds = (await slider.boundingBox())!
    const point = (percent: number) => bounds.x + 8 + ((bounds.width - 16) * percent) / 100
    await page.mouse.move(point(20), bounds.y + bounds.height / 2)
    await page.mouse.down()
    try {
      for (const percent of [35, 55, 80]) {
        await page.mouse.move(point(percent), bounds.y + bounds.height / 2, { steps: 8 })
        await expect(slider).toBeEnabled()
        await expect.poll(async () => Number(await slider.inputValue())).toBeGreaterThan(percent - 5)
      }
    } finally {
      await page.mouse.up()
    }
    await slider.focus()
    await page.keyboard.press('Home')
    await page.keyboard.press('ArrowRight')
    await page.keyboard.press('ArrowRight')
    await expect(slider).toHaveValue('2')
    await expect
      .poll(() =>
        page.evaluate(async () => {
          const response = await window.winnow.request({ route: 'preferences.presentation.get' })
          if (!response.ok) throw Error('Preference read failed')
          return (response.data as { preference: string; value: string }[]).find(
            (row) => row.preference === 'Transparency',
          )?.value
        }),
      )
      .toBe('2')
    await page.reload()
    await expect(page.locator('.avalon-shell')).toBeVisible()
    await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
    await expect(slider).toHaveValue('2')
  })

test('the native caption follows every composition and an Avalon light palette without taking fullscreen space', async () => {
  test.skip(process.platform !== 'win32' && process.platform !== 'linux')
  await application.evaluate(({ BrowserWindow }) => {
    const window = BrowserWindow.getAllWindows()[0]
    window.setFullScreen(false)
    window.webContents.send('winnow:fullscreen:changed', false)
    const original = window.setTitleBarOverlay.bind(window)
    window.setTitleBarOverlay = (options) => {
      ;(globalThis as typeof globalThis & { latestTitlebar?: unknown }).latestTitlebar = options
      return original(options)
    }
  })
  await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
  for (const composition of ['Afterglow', 'Rift', 'Catalogue', 'Avalon']) {
    await page
      .locator('.studio-theme-option')
      .filter({ has: page.locator('strong', { hasText: new RegExp(`^${composition}$`) }) })
      .click()
    await expect(page.locator('html')).toHaveAttribute('data-theme', composition.toLowerCase())
    const caption = page.getByLabel('Window title bar', { exact: true })
    await expect(caption).toBeVisible()
    const colors = await caption.evaluate((element) => {
      const root = getComputedStyle(document.documentElement)
      const style = getComputedStyle(element)
      return {
        text: root.getPropertyValue('--text').trim(),
        foreground: style.color,
        background: style.backgroundColor,
        drag: style.getPropertyValue('-webkit-app-region'),
        height: element.getBoundingClientRect().height,
      }
    })
    expect(colors.drag).toBe('drag')
    expect(colors.height).toBeCloseTo(32, 0)
    await expect
      .poll(() =>
        application.evaluate(
          () => (globalThis as typeof globalThis & { latestTitlebar?: unknown }).latestTitlebar,
        ),
      )
      .toEqual({ symbolColor: colors.text })
    const overlay = await page.evaluate(() => {
      const overlay = (
        navigator as Navigator & {
          windowControlsOverlay: { visible: boolean; getTitlebarAreaRect(): DOMRect }
        }
      ).windowControlsOverlay
      return { visible: overlay.visible, width: overlay.getTitlebarAreaRect().width, viewport: innerWidth }
    })
    expect(overlay.visible).toBe(true)
    expect(overlay.width).toBeGreaterThan(0)
    expect(overlay.width).toBeLessThan(overlay.viewport)
  }
  await choosePalette(page, 'rose-pine-dawn')
  await expect
    .poll(() =>
      application.evaluate(
        () => (globalThis as typeof globalThis & { latestTitlebar?: unknown }).latestTitlebar,
      ),
    )
    .toEqual({ symbolColor: '#464261' })
  await page.screenshot({ path: test.info().outputPath('themed-light-caption.png') })
  await application.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0].webContents.send('winnow:fullscreen:changed', true),
  )
  await expect(page.getByLabel('Window title bar', { exact: true })).toHaveCount(0)
  await expect(page.locator('#root')).toHaveCSS('padding-top', '0px')
  expect(errors).toEqual([])
})

for (const fullscreen of [false, true]) {
  test(`${fullscreen ? 'fullscreen' : 'desktop'} appearance controls follow all four compositions`, async () => {
    await application.evaluate(({ BrowserWindow }, fullscreen) => {
      const window = BrowserWindow.getAllWindows()[0]
      window.setFullScreen(false)
      window.setContentSize(1440, 1000)
      window.webContents.send('winnow:fullscreen:changed', fullscreen)
    }, fullscreen)
    await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
    for (const [composition, controls] of [
      ['Avalon', []],
      ['Afterglow', ['Typography', 'Spacing', 'Navigation', 'Library cards', 'Game details']],
      ['Rift', ['Typography', 'Cover finish', 'Game details']],
      ['Catalogue', ['Typography', 'Cover finish', 'Library cards', 'Game details']],
    ] as const) {
      await page
        .locator('.studio-theme-option')
        .filter({ has: page.locator('strong', { hasText: new RegExp(`^${composition}$`) }) })
        .click()
      await expect(page.locator('html')).toHaveAttribute('data-theme', composition.toLowerCase())
      for (const name of [
        'Typography',
        'Spacing',
        'Cover finish',
        'Navigation',
        'Library cards',
        'Game details',
      ]) {
        await expect(page.getByRole('button', { name, exact: true }), `${composition}: ${name}`).toHaveCount(
          (controls as readonly string[]).includes(name) ? 1 : 0,
        )
      }
      await expect(page.getByRole('slider', { name: /Artwork shade/ })).toHaveCount(
        composition === 'Afterglow' ? 1 : 0,
      )
      await expect(page.getByRole('heading', { name: 'Artwork materials' })).toHaveCount(
        composition === 'Rift' || composition === 'Catalogue' ? 1 : 0,
      )
      await expect(page.getByRole('heading', { name: 'Arrange your space' })).toHaveCount(
        composition !== 'Avalon' ? 1 : 0,
      )
      const atmosphere = page.getByRole('region', { name: 'Atmosphere', exact: true })
      const palettes = atmosphere.getByRole('group', { name: 'Palette', exact: true })
      if (composition === 'Avalon') {
        await expect.poll(() => palettes.getByRole('button').count()).toBeGreaterThanOrEqual(10)
      } else {
        await expect(palettes.getByRole('button')).toHaveCount(5)
      }
      await expect(page.getByRole('button', { name: /^Avalon palette/ })).toHaveCount(0)
      if (composition === 'Avalon') {
        const night = paletteSwatch(page, 'nightshift')
        await night.scrollIntoViewIfNeeded()
        await night.focus()
        await page.keyboard.press('Space')
        await expect(night).toBeFocused()
        await expect(night).toHaveAttribute('aria-pressed', 'true')
        await expect(page.locator('html')).toHaveCSS('--bg', '#070A10')
        await choosePalette(page, 'winnow')
      }
      await atmosphere.scrollIntoViewIfNeeded()
      await atmosphere.screenshot({
        path: test.info().outputPath(`${composition.toLowerCase()}-atmosphere.png`),
      })
      await page.getByRole('button', { name: 'Settings', exact: true }).click()
      await page
        .getByRole('navigation', { name: 'Settings section' })
        .getByRole('button', { name: 'Appearance', exact: true })
        .click()
      const settings = page.locator('.settings-page')
      await expect(
        settings.getByRole(fullscreen ? 'switch' : 'checkbox', {
          name: 'Fit ultrawide displays',
          exact: true,
        }),
      ).toHaveCount(composition === 'Avalon' ? 1 : 0)
      if (fullscreen) {
        await expect(settings.getByRole('button', { name: 'Heading font', exact: true })).toHaveCount(
          composition === 'Avalon' ? 1 : 0,
        )
        await expect(settings.getByRole('heading', { name: 'Typography', exact: true })).toHaveCount(
          composition === 'Avalon' ? 1 : 0,
        )
      }
      await page.screenshot({ path: test.info().outputPath(`${composition.toLowerCase()}-settings.png`) })
      await page.getByRole('button', { name: 'Theme Studio', exact: true }).first().click()
    }
    // Leave the shared fixture in its original composition for subsequent appearance checks.
    await page
      .locator('.studio-theme-option')
      .filter({ has: page.locator('strong', { hasText: /^Avalon$/ }) })
      .click()
    expect(errors).toEqual([])
  })
}

test.afterAll(async () => closeFixture(application, directory))
