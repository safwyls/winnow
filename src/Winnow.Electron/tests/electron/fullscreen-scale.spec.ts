import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import electronPath from 'electron'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { closeFixture } from './fixture-cleanup'

let app: ElectronApplication, page: Page, directory: string
const errors: string[] = []

test.beforeEach(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-fullscreen-scale-'))
  errors.length = 0
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/fullscreen-lifecycle-main.mjs'),
      '--data-dir',
      directory,
      '--seed-sample',
      '--no-sync',
    ],
    env: Object.fromEntries(
      Object.entries(process.env).filter(
        ([key, value]) =>
          key !== 'ELECTRON_RUN_AS_NODE' && key !== 'WINNOW_LIFECYCLE_HOLD' && value !== undefined,
      ),
    ) as Record<string, string>,
    chromiumSandbox: true,
  })
  page = await app.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await ready('desktop')
})

test.afterEach(async ({}, info) => {
  if (info.status !== info.expectedStatus)
    await info.attach('scale-failure', { body: await page.screenshot(), contentType: 'image/png' })
  await closeFixture(app, directory)
  expect(errors).toEqual([])
})

async function ready(mode: 'desktop' | 'fullscreen') {
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
}

async function preference(preference: string, value: string) {
  await page.evaluate(
    async ({ preference, value }) => {
      const result = await window.winnow.request({
        route: 'preferences.presentation.put',
        params: { preference },
        body: { value },
      })
      if (!result.ok) throw Error(result.message)
    },
    { preference, value },
  )
}

async function scale(value: string, expected = value) {
  await preference('FullscreenInterfaceScale', value)
  await expect(page.locator('html')).toHaveCSS('--fullscreen-interface-scale', expected)
  await page.evaluate(async () => {
    await document.fonts.ready
    await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
  })
}

async function fullscreen(width: number, height: number, wide: boolean) {
  await app.evaluate(
    ({ BrowserWindow }, size) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setContentSize(size.width, size.height)
      window.webContents.send('winnow:fullscreen:changed', true)
      window.focus()
    },
    { width, height },
  )
  await ready('fullscreen')
  await preference('FullscreenFitUltrawide', String(wide))
  await preference('FullscreenSafeMargin', '5')
  await preference('FullscreenReducedMotion', 'true')
  await expect(page.locator('html')).toHaveAttribute('data-fit-ultrawide', String(wide))
  await expect(page.locator('html')).toHaveCSS('--fullscreen-safe-margin', '5%')
  await expect.poll(() => page.evaluate(() => [innerWidth, innerHeight])).toEqual([width, height])
  await scale('1')
}

async function geometry() {
  return page.evaluate(() => {
    const header = document.querySelector('.avalon-header')!.getBoundingClientRect()
    const navigationElement = document.querySelector('[aria-label="Main navigation"]')!
    const navigation = navigationElement.getBoundingClientRect()
    const shell = getComputedStyle(document.querySelector('.avalon-shell')!)
    const backdrop = document.querySelector('.avalon-home-backdrop')!.getBoundingClientRect()
    const covers = document.querySelectorAll('[data-row-active="true"] .avalon-cover')
    return {
      zoom: Number(getComputedStyle(document.body).zoom),
      navigationWidth: navigation.width,
      navigationScale: navigation.width / parseFloat(getComputedStyle(navigationElement).width),
      canvasWidth: parseFloat(shell.width),
      canvasHeight: parseFloat(shell.height),
      coverCount: covers.length,
      coverHeight: covers[0]!.getBoundingClientRect().height,
      heroHeight: document.querySelector('.avalon-home-hero')!.getBoundingClientRect().height,
      left: header.left,
      top: header.top,
      backdrop: { x: backdrop.x, y: backdrop.y, width: backdrop.width, height: backdrop.height },
    }
  })
}

async function installGamepad() {
  await page.evaluate(() => {
    Object.assign(window, { scalePad: [] })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          connected: true,
          mapping: 'standard',
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, index) => ({
            pressed: (window as any).scalePad.includes(index),
            value: (window as any).scalePad.includes(index) ? 1 : 0,
          })),
        },
      ],
    })
  })
}

async function tap(button: number) {
  for (const pressed of [[], [button], []])
    await page.evaluate(async (pressed) => {
      ;(window as any).scalePad = pressed
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
}

for (const [width, height, wide] of [
  [1920, 1080, false],
  [3440, 1440, true],
  [3840, 2160, false],
] as const)
  test(`fullscreen interface scale preserves physical safe margins and a full backdrop at ${width}x${height}`, async ({}, info) => {
    await fullscreen(1920, 1080, false)
    const reference = await geometry()
    await fullscreen(width, height, wide)
    await expect(page.locator('.avalon-home-backdrop')).toBeVisible()
    const baseline = await geometry()
    const viewportScale = height / 1080
    expect(baseline.zoom).toBeCloseTo(0.85 * viewportScale, 5)
    if (width === 3840) {
      expect(baseline.coverCount).toBe(reference.coverCount)
      expect(baseline.coverHeight / reference.coverHeight).toBeCloseTo(2, 2)
      expect(baseline.heroHeight / reference.heroHeight).toBeCloseTo(2, 2)
    }
    expect(
      Math.abs(baseline.navigationWidth - reference.navigationWidth * viewportScale),
    ).toBeLessThanOrEqual(1)
    for (const value of [0.8, 1.2, 1]) {
      await scale(String(value))
      const current = await geometry()
      const expectedScale = 0.85 * value * viewportScale
      expect(current.zoom).toBeCloseTo(expectedScale, 5)
      expect(Math.abs(current.navigationScale - expectedScale) * current.canvasHeight).toBeLessThanOrEqual(1)
      expect(Math.abs(current.canvasHeight - 1080 / (0.85 * value))).toBeLessThanOrEqual(1)
      expect(
        Math.abs(current.canvasWidth - (wide ? (1080 * width) / height : 1920) / (0.85 * value)),
      ).toBeLessThanOrEqual(1)
      expect(Math.abs(current.navigationWidth - baseline.navigationWidth * value)).toBeLessThanOrEqual(1)
      expect(Math.abs(current.left - width * 0.05)).toBeLessThanOrEqual(1)
      expect(Math.abs(current.top - height * 0.05)).toBeLessThanOrEqual(1)
      expect(Math.abs(current.backdrop.x)).toBeLessThanOrEqual(1)
      expect(Math.abs(current.backdrop.y)).toBeLessThanOrEqual(1)
      expect(Math.abs(current.backdrop.width - width)).toBeLessThanOrEqual(1)
      expect(Math.abs(current.backdrop.height - height)).toBeLessThanOrEqual(1)
    }
    await page.screenshot({ path: info.outputPath(`scale-${width}-baseline.png`) })
    await scale('1.2')
    await installGamepad()
    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'Library', exact: true })
      .click()
    await tap(8)
    const search = page.getByRole('searchbox', { name: 'Search games' })
    await search.focus()
    await tap(3)
    const keyboard = page.getByRole('dialog', { name: 'Enter text' })
    await expect(keyboard).toBeVisible()
    const bounds = (await keyboard.boundingBox())!
    expect(bounds.x).toBeGreaterThanOrEqual(0)
    expect(bounds.y).toBeGreaterThanOrEqual(0)
    expect(bounds.x + bounds.width).toBeLessThanOrEqual(width + 1)
    expect(bounds.y + bounds.height).toBeLessThanOrEqual(height + 1)
    await page.screenshot({ path: info.outputPath(`scale-${width}-keyboard.png`) })
    await tap(1)
    await expect(keyboard).toHaveCount(0)
    await expect(search).toBeFocused()
    await app.evaluate(({ BrowserWindow }) => {
      BrowserWindow.getAllWindows()[0]!.webContents.send('winnow:fullscreen:changed', false)
    })
    await ready('desktop')
    await expect(page.locator('body')).toHaveCSS('zoom', '1')
    await expect(page.locator('html')).toHaveCSS('--fullscreen-interface-scale', '1')
    const saved = await page.evaluate(async () => {
      const result = await window.winnow.request({ route: 'preferences.presentation.get' })
      if (!result.ok) throw Error(result.message)
      return result.data as { preference: string; value: string | null }[]
    })
    expect(saved.find((row) => row.preference === 'FullscreenInterfaceScale')?.value).toBe('1.2')
    expect(saved.find((row) => row.preference === 'FullscreenSafeMargin')?.value).toBe('5')
    expect(saved.find((row) => row.preference === 'FullscreenFitUltrawide')?.value).toBe(String(wide))
  })

test('empty saved interface scales use the baseline while preserving independent text size', async () => {
  await fullscreen(1920, 1080, false)
  await preference('FullscreenTextScale', '1.3')
  await expect(page.locator('html')).toHaveCSS('--fullscreen-text-scale', '1.3')
  for (const empty of ['', '   ']) {
    await scale('1.2')
    await scale(empty, '1')
    await expect(page.locator('body')).toHaveCSS('zoom', '0.85')
    await expect(page.locator('html')).toHaveCSS('--fullscreen-text-scale', '1.3')
  }
})
