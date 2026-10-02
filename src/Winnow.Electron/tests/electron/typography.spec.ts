import { closeFixture } from './fixture-cleanup'
import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { access, mkdtemp, mkdir, readFile, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { prebuiltActivationHelper, prebuiltBackend } from './prebuilt-backend'
import { resolvedTypography, type ThemeProfile } from '../../src/shared/theme'

let application: ElectronApplication
let page: Page
let directory: string
const errors: string[] = []
test.beforeAll(async () => {
  const root = resolve('../..', '.tmp')
  await access(root)
  await Promise.all([access(prebuiltBackend), access(prebuiltActivationHelper)])
  directory = await mkdtemp(join(root, 'winnow-electron-typography-'))
  const environment = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('.'), '--data-dir', directory, '--seed-sample', '--no-sync', '--force-color-profile=srgb'],
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
test.afterAll(async () => closeFixture(application, directory))
test.afterEach(async () => {
  if (test.info().status !== test.info().expectedStatus && page && !page.isClosed())
    await page.screenshot({ path: test.info().outputPath('before-teardown.png') })
})

async function studio() {
  await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Theme typography', exact: true })).toBeVisible()
}
async function textSize(value: number) {
  await page.getByRole('slider', { name: 'Theme text size', exact: true }).fill(String(value))
  await expect(page.locator('html')).toHaveCSS('--theme-text-scale', String(value / 100))
}
async function homeMetrics() {
  await page.getByRole('button', { name: 'Winnow home', exact: true }).click()
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
  await page.evaluate(async () => {
    await document.fonts.ready
    await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
  })
  return page.evaluate(() => {
    const cover = document.querySelector('.avalon-cover')!.getBoundingClientRect()
    const brand = document.querySelector('.avalon-brand')!
    const icon = document.querySelector('.avalon-utilities svg')!.getBoundingClientRect()
    return {
      coverWidth: cover.width,
      coverHeight: cover.height,
      brandFont: parseFloat(getComputedStyle(brand).fontSize),
      iconWidth: icon.width,
      iconHeight: icon.height,
      homeLayout: Object.fromEntries(
        [
          '.avalon-home',
          '.avalon-home-hero',
          '.avalon-home-shelf > h2',
          '.avalon-hero-actions',
          '.avalon-shell',
          '.avalon-header',
          '.avalon-footer',
        ].map((selector) => {
          const node = document.querySelector<HTMLElement>(selector)
          if (!node) return [selector, null]
          const style = getComputedStyle(node),
            bounds = node.getBoundingClientRect()
          return [
            selector,
            {
              clientHeight: node.clientHeight,
              offsetHeight: node.offsetHeight,
              width: bounds.width,
              height: bounds.height,
              fontSize: style.fontSize,
              lineHeight: style.lineHeight,
              minHeight: style.minHeight,
              gridRows: style.gridTemplateRows,
              padding: style.padding,
              rowHeight: style.getPropertyValue('--home-row-height'),
              rowWidth: style.getPropertyValue('--home-row-width'),
              bodySize: style.getPropertyValue('--fullscreen-body-size'),
            },
          ]
        }),
      ),
      actions: [...document.querySelectorAll<HTMLElement>('.avalon-hero-actions button')].map((node) => ({
        text: node.textContent,
        bounds: node.getBoundingClientRect().toJSON(),
        font: getComputedStyle(node).fontSize,
      })),
    }
  })
}

for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} role fonts and text size scale text without enlarging icons or covers`, async () => {
    await application.evaluate(({ BrowserWindow }, mode) => {
      const window = BrowserWindow.getAllWindows()[0]
      window.setFullScreen(false)
      window.setMinimumSize(0, 0)
      window.setContentSize(mode === 'desktop' ? 1000 : 1920, mode === 'desktop' ? 720 : 1080)
      window.isFullScreen = () => mode === 'fullscreen'
      window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
    }, mode)
    await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
    await studio()
    await page.getByRole('button', { name: 'Reset theme typography' }).click()
    const baseline = await homeMetrics()
    await studio()
    await page.getByRole('combobox', { name: 'Heading font', exact: true }).fill('IBM Plex Mono')
    await page.getByRole('combobox', { name: 'Interface font', exact: true }).fill('Bricolage Grotesque')
    await page.getByRole('combobox', { name: 'Data font', exact: true }).fill('Plus Jakarta Sans')
    await textSize(115)
    await page.getByRole('combobox', { name: /^Avalon palette/ }).selectOption('nightshift')
    await expect(page.getByRole('slider', { name: 'Theme text size', exact: true })).toHaveValue('100')
    await page.getByRole('combobox', { name: /^Avalon palette/ }).selectOption('winnow')
    await expect(page.getByRole('slider', { name: 'Theme text size', exact: true })).toHaveValue('115')
    await expect(page.getByRole('combobox', { name: 'Interface font', exact: true })).toHaveValue(
      'Bricolage Grotesque',
    )
    await textSize(120)
    for (const name of [
      'Heading font',
      'Interface font',
      'Data font',
      'Theme text size',
      'Reset theme typography',
    ]) {
      const control = page.getByRole(
        name === 'Theme text size' ? 'slider' : name.startsWith('Reset') ? 'button' : 'combobox',
        { name, exact: true },
      )
      await control.scrollIntoViewIfNeeded()
      await control.focus()
      await expect(control).toBeFocused()
      const bounds = await control.boundingBox()
      expect(bounds!.x).toBeGreaterThanOrEqual(-1)
      expect(bounds!.y).toBeGreaterThanOrEqual(-1)
      expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(mode === 'desktop' ? 1001 : 1921)
      expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(mode === 'desktop' ? 721 : 1081)
    }
    const scaled = await homeMetrics()
    await page.screenshot({ path: test.info().outputPath(`${mode}-home-typography-120.png`) })
    await test.info().attach(`${mode}-text-and-geometry`, {
      body: JSON.stringify({ baseline, scaled }),
      contentType: 'application/json',
    })
    expect(scaled.brandFont / baseline.brandFont).toBeCloseTo(1.2, 2)
    expect(scaled.iconWidth).toBeCloseTo(baseline.iconWidth, 1)
    expect(scaled.iconHeight).toBeCloseTo(baseline.iconHeight, 1)
    expect(scaled.coverWidth).toBeCloseTo(baseline.coverWidth, 1)
    expect(scaled.coverHeight).toBeCloseTo(baseline.coverHeight, 1)
    expect(
      await page.locator('.avalon-brand').evaluate((element) => getComputedStyle(element).fontFamily),
    ).toContain('Avalon Data')
    await studio()
    await page.getByRole('combobox', { name: /^Avalon palette/ }).selectOption('nightshift')
    await expect(page.getByRole('slider', { name: 'Theme text size', exact: true })).toHaveValue('100')
    await page.getByRole('combobox', { name: /^Avalon palette/ }).selectOption('winnow')
    await expect(page.getByRole('slider', { name: 'Theme text size', exact: true })).toHaveValue('120')
    await expect(page.getByRole('combobox', { name: 'Heading font', exact: true })).toHaveValue(
      'IBM Plex Mono',
    )
    await textSize(80)
    await textSize(100)
    const reset = await homeMetrics()
    expect(reset.brandFont).toBeCloseTo(baseline.brandFont, 2)
    await studio()
    await page.getByRole('button', { name: 'Reset theme typography' }).click()
    await page.screenshot({ path: test.info().outputPath(`${mode}-typography-reset.png`) })
  })
}

test('fullscreen combines page and theme text scales without scaling the chrome or icons twice', async () => {
  const pageTextSize = async (value: string) => {
    await page.evaluate(async (value) => {
      const result = await window.winnow.request({
        route: 'preferences.presentation.put',
        params: { preference: 'FullscreenTextScale' },
        body: { value },
      })
      if (!result.ok) throw new Error('Could not save the isolated fullscreen text preference')
    }, value)
    await expect(page.locator('html')).toHaveCSS('--fullscreen-text-scale', value)
  }
  await application.evaluate(({ BrowserWindow }) => {
    const window = BrowserWindow.getAllWindows()[0]
    window.setFullScreen(false)
    window.setContentSize(1920, 1080)
    window.isFullScreen = () => true
    window.webContents.send('winnow:fullscreen:changed', true)
  })
  await expect(page.locator('.avalon-shell')).toHaveClass(/fullscreen/)
  await studio()
  await page.getByRole('button', { name: 'Reset theme typography' }).click()
  await pageTextSize('1')
  await page.getByRole('button', { name: 'Winnow home', exact: true }).click()
  await expect(page.locator('.avalon-home-hero h1')).toBeVisible()
  const metric = () =>
    page.evaluate(() => {
      const size = (selector: string) =>
        parseFloat(getComputedStyle(document.querySelector(selector)!).fontSize)
      return {
        heading: size('.avalon-home-hero h1'),
        copy: size('.avalon-home-hero > p'),
        action: size('.avalon-hero-actions > button'),
        clock: size('.avalon-clock'),
        chrome: size('.avalon-brand'),
        icon: document.querySelector('.avalon-utilities svg')!.getBoundingClientRect().width,
      }
    })
  const baseline = await metric()
  expect(baseline.heading).toBeCloseTo(64, 1)
  expect(baseline.clock).toBeCloseTo(24, 1)
  expect(baseline.copy).toBeCloseTo(24, 1)
  expect(baseline.action).toBeCloseTo(24, 1)
  await pageTextSize('1.4')
  const pageScale = await metric()
  expect(pageScale.copy / baseline.copy).toBeCloseTo(1.4, 2)
  expect(pageScale.action / baseline.action).toBeCloseTo(1.4, 2)
  expect(pageScale.clock).toBeCloseTo(baseline.clock, 2)
  expect(pageScale.heading).toBeCloseTo(baseline.heading, 2)
  expect(pageScale.chrome).toBeCloseTo(baseline.chrome, 2)
  const measurements = []
  for (const percent of [120, 80, 100, 120]) {
    await studio()
    await textSize(percent)
    await page.getByRole('button', { name: 'Winnow home', exact: true }).click()
    await expect(page.locator('.avalon-home-hero h1')).toBeVisible()
    const both = await metric()
    const factor = percent / 100
    expect(both.copy / baseline.copy).toBeCloseTo(1.4 * factor, 2)
    expect(both.action / baseline.action).toBeCloseTo(1.4 * factor, 2)
    expect(both.clock).toBeCloseTo(24 * factor, 1)
    expect(both.heading / baseline.heading).toBeCloseTo(factor, 2)
    expect(both.chrome / baseline.chrome).toBeCloseTo(factor, 2)
    expect(both.icon).toBeCloseTo(baseline.icon, 2)
    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'Settings', exact: true })
      .click()
    await page
      .getByRole('navigation', { name: 'Settings section' })
      .getByRole('button', { name: 'Platforms', exact: true })
      .click()
    if (await page.getByRole('button', { name: 'Steam', exact: true }).count())
      await page.getByRole('button', { name: 'Steam', exact: true }).click()
    await page.getByRole('button', { name: 'Sign in to Steam', exact: true }).click()
    const consent = page.getByRole('dialog', { name: 'Before you sign in', exact: true })
    await expect(consent).toBeVisible()
    const consentMetrics = await consent.evaluate((node) => {
      const metric = (selector: string) => {
        const element = node.querySelector(selector)!
        const s = getComputedStyle(element)
        return { size: parseFloat(s.fontSize), line: parseFloat(s.lineHeight) }
      }
      return {
        copy: metric('.reading-prose'),
        action: metric('.form-actions button'),
        hints: metric('.platform-controller-hints'),
      }
    })
    expect(consentMetrics.copy.size).toBeCloseTo(28 * factor, 1)
    expect(consentMetrics.copy.line).toBeCloseTo(42 * factor, 1)
    expect(consentMetrics.action.size).toBeCloseTo(28 * factor, 1)
    expect(consentMetrics.hints.size).toBeCloseTo(24 * factor, 1)
    if (percent === 120)
      await page.screenshot({ path: test.info().outputPath('actual-steam-consent-120.png') })
    await consent.getByRole('button', { name: 'Cancel sign-in', exact: true }).click()
    await expect(consent).toHaveCount(0)
    measurements.push({ percent, ...both, consent: consentMetrics })
  }
  await test.info().attach('combined-live-typography', {
    body: JSON.stringify({ baseline, measurements }),
    contentType: 'application/json',
  })
  await page.getByRole('button', { name: 'Winnow home', exact: true }).click()
  await page.screenshot({ path: test.info().outputPath('fullscreen-combined-120-140.png') })
  await pageTextSize('1')
  await studio()
  await page.getByRole('button', { name: 'Reset theme typography' }).click()
})

test('native installed-font enumeration supplies usable family choices', async () => {
  await studio()
  await page.getByRole('button', { name: 'Reset theme typography', exact: true }).click()
  await page.getByRole('button', { name: 'Find installed fonts', exact: true }).click()
  await expect(page.getByText(/installed font families available\./)).toBeVisible()
  const choices = await page
    .locator('datalist')
    .first()
    .locator('option')
    .evaluateAll((options) => options.map((option) => (option as HTMLOptionElement).value))
  expect(choices.length).toBeGreaterThan(3)
  if (process.platform === 'win32') expect(choices).toContain('Segoe UI')
  const cdp = await page.context().newCDPSession(page)
  await cdp.send('DOM.enable')
  await cdp.send('CSS.enable')
  const actualFonts = async () => {
    await page.evaluate(() => document.fonts.ready)
    const { root } = await cdp.send('DOM.getDocument')
    const result: Record<string, unknown> = {}
    for (const [role, selector] of [
      ['heading', '#avalon-typography-heading'],
      ['interface', '#avalon-typography-heading + p'],
      ['data', '#avalon-typography-heading ~ .studio-field-grid .studio-value'],
    ]) {
      const { nodeId } = await cdp.send('DOM.querySelector', { nodeId: root.nodeId, selector: selector! })
      expect(nodeId, `${role} font sample`).not.toBe(0)
      const { fonts } = await cdp.send('CSS.getPlatformFontsForNode', { nodeId })
      expect(fonts.length, `${role} rendered glyphs`).toBeGreaterThan(0)
      result[role!] = fonts.map(({ familyName, postScriptName, isCustomFont }) => ({
        familyName,
        postScriptName,
        isCustomFont,
      }))
    }
    return result
  }
  const baseline = await actualFonts()
  for (const [label, missing] of [
    ['Heading font', 'Winnow missing heading 913872'],
    ['Interface font', 'Winnow missing interface 913872'],
    ['Data font', 'Winnow missing data 913872'],
  ])
    await page.getByRole('combobox', { name: label!, exact: true }).fill(missing!)
  await expect.poll(actualFonts).toEqual(baseline)
  await test.info().attach('actual-role-font-fallback', {
    body: JSON.stringify({ baseline, missing: await actualFonts(), installedFamilyCount: choices.length }),
    contentType: 'application/json',
  })
  await page.screenshot({ path: test.info().outputPath('missing-role-fonts-fallback.png') })
  await page.getByRole('button', { name: 'Reset theme typography', exact: true }).click()
  await cdp.detach()
})

test('desktop maximum theme size keeps every control inside its scroller and keyboard reset restores authored IBM 105 defaults', async () => {
  await application.evaluate(({ BrowserWindow }) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setContentSize(1000, 720)
    window.isFullScreen = () => false
    window.webContents.send('winnow:fullscreen:changed', false)
  })
  await expect(page.locator('.avalon-shell.desktop')).toBeVisible()
  const authored = JSON.parse(
    await readFile(resolve('src/renderer/themes/avalon/assets/rose-pine.json'), 'utf8'),
  )
  authored.id = 'typography-authored'
  authored.name = 'Typography authored fixture'
  authored.typography = {
    headingFont: 'IBM Plex Mono',
    interfaceFont: 'Plus Jakarta Sans',
    dataFont: 'IBM Plex Mono',
    sizePercent: 105,
  }
  await mkdir(join(directory, 'themes'), { recursive: true })
  await writeFile(join(directory, 'themes/typography-authored.json'), JSON.stringify(authored))
  await studio()
  await page.getByRole('button', { name: 'Reload authored palettes', exact: true }).click()
  const palette = page.getByRole('combobox', { name: /^Avalon palette/ })
  await expect(palette.locator('option[value="typography-authored"]')).toHaveCount(1)
  await palette.selectOption('typography-authored')
  await expect(page.getByRole('combobox', { name: 'Heading font', exact: true })).toHaveValue('IBM Plex Mono')
  await expect(page.getByRole('slider', { name: 'Theme text size', exact: true })).toHaveValue('105')
  await textSize(120)
  const bounds = []
  for (const name of ['Heading font', 'Interface font', 'Data font', 'Theme text size']) {
    const control = page.getByRole(name === 'Theme text size' ? 'slider' : 'combobox', { name, exact: true })
    await control.scrollIntoViewIfNeeded()
    await control.focus()
    await expect(control).toBeFocused()
    const geometry = await control.evaluate((node) => {
      let scroll = node.parentElement
      while (
        scroll &&
        !(
          /(auto|scroll)/.test(getComputedStyle(scroll).overflowY) &&
          scroll.scrollHeight > scroll.clientHeight
        )
      )
        scroll = scroll.parentElement
      if (!scroll) throw Error('Typography control has no actual scrolling ancestor')
      const item = node.getBoundingClientRect(),
        clip = scroll.getBoundingClientRect()
      return {
        item: { x: item.x, y: item.y, right: item.right, bottom: item.bottom },
        clip: { x: clip.x, y: clip.y, right: clip.right, bottom: clip.bottom },
      }
    })
    expect(geometry.item.x).toBeGreaterThanOrEqual(geometry.clip.x - 1)
    expect(geometry.item.y).toBeGreaterThanOrEqual(geometry.clip.y - 1)
    expect(geometry.item.right).toBeLessThanOrEqual(geometry.clip.right + 1)
    expect(geometry.item.bottom).toBeLessThanOrEqual(geometry.clip.bottom + 1)
    bounds.push({ name, ...geometry })
  }
  await page.screenshot({ path: test.info().outputPath('desktop-source-maximum-typography.png') })
  const reset = page.getByRole('button', { name: 'Reset theme typography', exact: true })
  await reset.scrollIntoViewIfNeeded()
  await reset.focus()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('slider', { name: 'Theme text size', exact: true })).toHaveValue('105')
  await expect(page.getByRole('combobox', { name: 'Heading font', exact: true })).toHaveValue('IBM Plex Mono')
  await test.info().attach('authored-reset-and-containment', {
    body: JSON.stringify({ authored: authored.typography, bounds }),
    contentType: 'application/json',
  })
  await palette.selectOption('winnow')
})

test('fullscreen Theme Studio and Settings Appearance separately retain TV typography font selection and keyboard focus', async () => {
  await application.evaluate(({ BrowserWindow }) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setContentSize(1920, 1080)
    window.isFullScreen = () => true
    window.webContents.send('winnow:fullscreen:changed', true)
  })
  await expect(page.locator('.avalon-shell.fullscreen')).toBeVisible()
  await studio()
  await textSize(120)
  const studioMetrics = await page.locator('.theme-studio').evaluate((node) => {
    const size = (selector: string) => {
      const element = node.querySelector(selector)
      if (!element) throw Error(`Missing Studio role ${selector}`)
      return Number.parseFloat(getComputedStyle(element).fontSize)
    }
    return {
      heading: size('.studio-panel h2'),
      field: size('.studio-field'),
      input: size('.studio-field input'),
      action: size('.inline-actions button'),
      prose: size('.studio-panel > p'),
    }
  })
  expect(studioMetrics.heading).toBeCloseTo(18 * 1.2, 1)
  expect(studioMetrics.field).toBeCloseTo(28 * 1.2, 1)
  expect(studioMetrics.input).toBeCloseTo(28 * 1.2, 1)
  expect(studioMetrics.action).toBeCloseTo(28 * 1.2, 1)
  expect(studioMetrics.prose).toBeCloseTo(24 * 1.2, 1)
  await page.screenshot({ path: test.info().outputPath('fullscreen-studio-typography-120.png') })
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Settings', exact: true })
    .click()
  await page
    .getByRole('navigation', { name: 'Settings section' })
    .getByRole('button', { name: 'Appearance', exact: true })
    .click()
  const appearance = page.getByRole('region', { name: 'Fullscreen appearance', exact: true })
  const size = appearance.getByRole('button', { name: 'Theme text size', exact: true })
  await size.scrollIntoViewIfNeeded()
  await size.focus()
  await page.keyboard.press('ArrowRight')
  await expect(size).toBeFocused()
  await expect(size.locator('.fullscreen-setting-value [id]')).toHaveText('120%')
  const heading = appearance.getByRole('button', { name: 'Heading font', exact: true })
  await heading.scrollIntoViewIfNeeded()
  await heading.focus()
  await page.keyboard.press('Enter')
  const picker = page.getByRole('dialog', { name: 'Heading font', exact: true })
  await expect(picker).toBeVisible()
  await picker.getByRole('button', { name: 'IBM Plex Mono', exact: true }).click()
  await expect(picker).toHaveCount(0)
  await expect(heading).toBeFocused()
  await expect(heading).toContainText('IBM Plex Mono')
  for (const name of [
    'Heading font',
    'Interface font',
    'Data font',
    'Theme text size',
    'Reset theme typography',
  ]) {
    const control = appearance.getByRole('button', { name, exact: true })
    await control.scrollIntoViewIfNeeded()
    await control.focus()
    const bounds = (await control.boundingBox())!
    expect(bounds.x).toBeGreaterThanOrEqual(0)
    expect(bounds.y).toBeGreaterThanOrEqual(0)
    expect(bounds.x + bounds.width).toBeLessThanOrEqual(1921)
    expect(bounds.y + bounds.height).toBeLessThanOrEqual(1081)
  }
  await page.screenshot({ path: test.info().outputPath('fullscreen-settings-typography-120.png') })
  await appearance.getByRole('button', { name: 'Reset theme typography', exact: true }).click()
  await expect
    .poll(
      async () =>
        resolvedTypography((await page.evaluate(() => window.winnow.loadPreferences())) as ThemeProfile)
          .sizePercent,
    )
    .toBe(100)
  await test.info().attach('studio-and-settings-typography', {
    body: JSON.stringify({ studioMetrics }),
    contentType: 'application/json',
  })
})

test('rendered typography reports no uncaught exceptions', async () => expect(errors).toEqual([]))
