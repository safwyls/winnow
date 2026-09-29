import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { access, mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'

let application: ElectronApplication
let page: Page
let directory: string
const errors: string[] = []
test.beforeAll(async () => {
  const root = resolve('../..', '.tmp')
  await access(root)
  directory = await mkdtemp(join(root, 'winnow-electron-typography-'))
  const environment = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('.'), '--data-dir', directory, '--seed-sample', '--no-sync'],
    env: environment,
    chromiumSandbox: true,
    timeout: 60000,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.locator('.avalon-shell')).toBeVisible()
})
test.afterAll(async () => {
  if (application) {
    let stop: ReturnType<typeof setTimeout> | undefined
    try {
      await Promise.race([
        application.close(),
        new Promise<void>((done) => {
          stop = setTimeout(() => {
            application.process().kill()
            done()
          }, 5000)
        }),
      ])
    } finally {
      clearTimeout(stop)
    }
  }
  if (!directory) return
  try {
    const endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
    if (new URL(endpoint.address).hostname !== '127.0.0.1') throw Error('Unexpected test backend address')
    await fetch(new URL('/api/v1/lifecycle/shutdown', endpoint.address), {
      method: 'POST',
      headers: { Authorization: `Bearer ${endpoint.token}` },
      signal: AbortSignal.timeout(5000),
    })
  } catch {
    /* A failed startup may not publish discovery. Preserve test artifacts. */
  }
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
  await page.evaluate(() => document.fonts.ready)
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
    }
  })
}

for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} role fonts and text size scale text without enlarging icons or covers`, async () => {
    await application.evaluate(({ BrowserWindow }, mode) => {
      const window = BrowserWindow.getAllWindows()[0]
      window.setFullScreen(false)
      window.setContentSize(1280, 720)
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
      expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(1281)
      expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(721)
    }
    const scaled = await homeMetrics()
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
  })
}

test('fullscreen combines page and theme text scales without scaling the chrome or icons twice', async () => {
  await page.getByRole('button', { name: 'Winnow home', exact: true }).click()
  const metric = () => page.evaluate(() => {
    const size = (selector: string) => parseFloat(getComputedStyle(document.querySelector(selector)!).fontSize)
    return { heading: size('.avalon-home-hero h1'), copy: size('.avalon-home-hero > p'), chrome: size('.avalon-brand'), icon: document.querySelector('.avalon-utilities svg')!.getBoundingClientRect().width }
  })
  const baseline = await metric()
  await page.evaluate(() => document.documentElement.style.setProperty('--fullscreen-text-scale', '1.4'))
  const pageScale = await metric()
  expect(pageScale.copy / baseline.copy).toBeCloseTo(1.4, 2)
  expect(pageScale.heading).toBeCloseTo(baseline.heading, 2)
  expect(pageScale.chrome).toBeCloseTo(baseline.chrome, 2)
  await studio()
  await textSize(120)
  await page.getByRole('button', { name: 'Winnow home', exact: true }).click()
  const both = await metric()
  expect(both.copy / baseline.copy).toBeCloseTo(1.4 * 1.2, 2)
  expect(both.heading / baseline.heading).toBeCloseTo(1.2, 2)
  expect(both.chrome / baseline.chrome).toBeCloseTo(1.2, 2)
  expect(both.icon).toBeCloseTo(baseline.icon, 2)
  await page.evaluate(() => document.documentElement.style.setProperty('--fullscreen-text-scale', '1'))
  await studio()
  await page.getByRole('button', { name: 'Reset theme typography' }).click()
})

test('native installed-font enumeration supplies usable family choices', async () => {
  await studio()
  await page.getByRole('button', { name: 'Find installed fonts', exact: true }).click()
  await expect(page.getByText(/installed font families available\./)).toBeVisible()
  const choices = await page
    .locator('datalist')
    .first()
    .locator('option')
    .evaluateAll((options) => options.map((option) => (option as HTMLOptionElement).value))
  expect(choices.length).toBeGreaterThan(3)
  if (process.platform === 'win32') expect(choices).toContain('Segoe UI')
})

test('rendered typography reports no uncaught exceptions', async () => expect(errors).toEqual([]))
