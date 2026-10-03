import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Locator,
  type Page,
} from '@playwright/test'
import { build } from 'esbuild'
import { mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

let output: string, application: ElectronApplication | undefined, page: Page
const errors: string[] = []

test.beforeAll(async () => {
  output = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-choice-menu-build-'))
  await build({
    entryPoints: [resolve('tests/electron/choice-menu-renderer.tsx')],
    outfile: join(output, 'renderer.js'),
    bundle: true,
    platform: 'browser',
    format: 'iife',
    jsx: 'automatic',
    define: { 'process.env.NODE_ENV': '"production"' },
    loader: { '.ttf': 'file', '.woff': 'file', '.woff2': 'file', '.svg': 'dataurl' },
    logLevel: 'silent',
    plugins: [
      {
        name: 'theme-source-text',
        setup(bundle) {
          bundle.onResolve({ filter: /\?raw$/ }, (args) => ({
            path: resolve(args.resolveDir, args.path.slice(0, -4)),
            namespace: 'theme-source-text',
          }))
          bundle.onLoad({ filter: /.*/, namespace: 'theme-source-text' }, async (args) => ({
            contents: await readFile(args.path, 'utf8'),
            loader: 'text',
          }))
        },
      },
    ],
  })
  await writeFile(
    join(output, 'index.html'),
    '<!doctype html><html><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src \'self\'; style-src \'self\' \'unsafe-inline\'; img-src \'self\' data: blob:; font-src \'self\'"><link rel="stylesheet" href="renderer.css"></head><body><div id="root"></div><script src="renderer.js"></script></body></html>',
  )
})

test.afterEach(async ({}, info) => {
  try {
    if (page && !page.isClosed() && info.status !== info.expectedStatus)
      await page.screenshot({ path: info.outputPath('choice-menu-failure.png') })
  } finally {
    await closeFixture(application)
    application = undefined
  }
  expect(errors).toEqual([])
})

async function start(theme: string, mode: 'desktop' | 'fullscreen') {
  errors.length = 0
  const data = await mkdtemp(join(resolve('../..', '.tmp'), `winnow-choice-menu-${theme}-${mode}-`))
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('tests/electron/actions-probe-main.mjs'), '--data-dir', data, join(output, 'index.html')],
    env: Object.fromEntries(
      Object.entries(process.env).filter(
        ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
      ),
    ) as Record<string, string>,
    chromiumSandbox: true,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await application.evaluate(({ BrowserWindow }) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setMinimumSize(0, 0)
    window.setContentSize(1280, 800)
    window.focus()
  })
  await expect(page.getByRole('heading', { name: 'Choice menu fixture ready' })).toBeVisible()
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await page.evaluate(
    ({ theme, mode }) =>
      (
        window as unknown as {
          choiceMenuProbe: {
            configure(configuration: { theme: string; mode: 'desktop' | 'fullscreen'; zoom: number }): void
          }
        }
      ).choiceMenuProbe.configure({ theme, mode, zoom: 1.25 }),
    { theme, mode },
  )
  await expect(
    page.locator(`[data-fixture-theme="${theme}"][data-fixture-mode="${mode}"][data-fixture-zoom="1.25"]`),
  ).toBeVisible()
  await page.evaluate(() => document.fonts.ready)
}

async function assertBounded(menu: Locator) {
  const geometry = await menu.evaluate((element) => {
    const bounds = element.getBoundingClientRect()
    const style = getComputedStyle(element)
    return {
      left: bounds.left,
      top: bounds.top,
      right: bounds.right,
      bottom: bounds.bottom,
      viewportWidth: innerWidth,
      viewportHeight: innerHeight,
      scrollHeight: element.scrollHeight,
      clientHeight: element.clientHeight,
      scrollWidth: element.scrollWidth,
      clientWidth: element.clientWidth,
      popover: element.matches(':popover-open'),
      zoom: Number.parseFloat(getComputedStyle(document.body).zoom),
      background: style.backgroundColor,
      raised: style.getPropertyValue('--raised').trim(),
    }
  })
  expect(geometry.zoom).toBe(1.25)
  expect(geometry.popover).toBe(true)
  expect(geometry.left).toBeGreaterThanOrEqual(0)
  expect(geometry.top).toBeGreaterThanOrEqual(0)
  expect(geometry.right).toBeLessThanOrEqual(geometry.viewportWidth)
  expect(geometry.bottom).toBeLessThanOrEqual(geometry.viewportHeight)
  expect(geometry.scrollHeight).toBeGreaterThan(geometry.clientHeight)
  expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.clientWidth + 1)
  expect(geometry.background).not.toBe('rgba(0, 0, 0, 0)')
  expect(geometry.raised).not.toBe('')
  return geometry
}

async function exerciseChoice(label: string, owner?: Locator) {
  const trigger = page.getByRole('button', { name: label, exact: true })
  await trigger.focus()
  await page.keyboard.press('ArrowDown')
  const menu = page.getByRole('menu', { name: label, exact: true })
  await expect(menu).toBeVisible()
  const geometry = await assertBounded(menu)
  await expect(menu.getByRole('menuitemradio', { name: 'Steam', exact: true })).toBeFocused()
  await expect(
    menu.getByRole('menuitemradio', { name: 'PlayStation (unavailable)', exact: true }),
  ).toBeDisabled()
  if (owner) {
    expect(
      await menu.evaluate((element) => element.closest('[role="dialog"]')?.getAttribute('aria-labelledby')),
    ).toBe(await owner.getAttribute('aria-labelledby'))
    // Radix must not pull focus out of a menu placed in its dialog's top layer.
    await expect(owner.locator(':focus')).toHaveCount(1)
  }
  await page.keyboard.press('ArrowDown')
  await expect(menu.getByRole('menuitemradio', { name: 'GOG', exact: true })).toBeFocused()
  await page.keyboard.press('Enter')
  await expect(menu).toHaveCount(0)
  await expect(trigger).toBeFocused()
  await expect(trigger).toHaveJSProperty('value', 'gog')
  await expect(page.getByRole('status', { name: `${label} saved value` })).toHaveText('gog')
  await page.keyboard.press('Enter')
  await expect(menu.getByRole('menuitemradio', { name: 'GOG', exact: true })).toHaveAttribute(
    'aria-checked',
    'true',
  )
  await page.keyboard.press('End')
  const last = menu.getByRole('menuitemradio', { name: 'Z-store', exact: true })
  await expect(last).toBeFocused()
  await expect(last).toBeInViewport()
  await page.keyboard.press('Escape')
  await expect(menu).toHaveCount(0)
  await expect(trigger).toBeFocused()
  await expect(trigger).toHaveJSProperty('value', 'gog')
  if (owner) await expect(owner).toBeVisible()
  await page.keyboard.press('Enter')
  await expect(menu).toBeVisible()
  await page.keyboard.press('Tab')
  await expect(menu).toHaveCount(0)
  await expect(page.getByRole('button', { name: `After ${label}`, exact: true })).toBeFocused()
  return geometry
}

for (const theme of ['avalon', 'afterglow', 'rift', 'catalogue'])
  for (const mode of ['desktop', 'fullscreen'] as const)
    test(`${theme} ${mode}: choices stay themed, bounded and keyboard accessible through nested dialogs at 125% zoom`, async ({}, info) => {
      await start(theme, mode)
      const geometry: Awaited<ReturnType<typeof assertBounded>>[] = []
      await test.step('Clipped library toolbar, long options and disabled choice', async () => {
        geometry.push(await exerciseChoice('Library store'))
      })
      await test.step('Owning dialog and fullscreen action panel keep focus and value', async () => {
        await page.getByRole('button', { name: 'Open preferences', exact: true }).click()
        const owner = page.getByRole('dialog', { name: 'Preferences', exact: true })
        await expect(owner).toBeVisible()
        if (mode === 'fullscreen') await expect(owner).toHaveClass(/avalon-actions-panel/)
        geometry.push(await exerciseChoice('Dialog store', owner))
        await page.getByRole('button', { name: 'Dialog store', exact: true }).click()
        const menu = page.getByRole('menu', { name: 'Dialog store', exact: true })
        await expect(menu).toBeVisible()
        const rows = await menu.getByRole('menuitemradio').evaluateAll((elements) =>
          elements.slice(0, 3).map((element) => {
            const style = getComputedStyle(element)
            const bounds = element.getBoundingClientRect()
            return {
              fontSize: Number.parseFloat(style.fontSize),
              height: bounds.height,
              display: style.display,
              textAlign: style.textAlign,
            }
          }),
        )
        for (const row of rows) {
          expect(row.display).toBe('flex')
          expect(row.textAlign).toBe('left')
          expect(row.fontSize).toBeGreaterThanOrEqual(12)
          expect(row.height).toBeGreaterThanOrEqual(30)
          expect(row.height).toBeLessThan(90)
        }
        await page.screenshot({ path: info.outputPath(`${theme}-${mode}-dialog-choice.png`) })
        await page.keyboard.press('Escape')
        await expect(owner).toBeVisible()
      })
      await test.step('Nested Radix dialog owns its menu and the first Escape', async () => {
        await page.getByRole('button', { name: 'Open nested dialog', exact: true }).click()
        const nested = page.getByRole('dialog', { name: 'Nested preferences', exact: true })
        await expect(nested).toBeVisible()
        geometry.push(await exerciseChoice('Nested store', nested))
        await page.keyboard.press('Escape')
        await expect(nested).toHaveCount(0)
        await expect(page.getByRole('dialog', { name: 'Preferences', exact: true })).toBeVisible()
        await expect(page.getByRole('button', { name: 'Open nested dialog', exact: true })).toBeFocused()
        await page.keyboard.press('Escape')
        await expect(page.getByRole('dialog')).toHaveCount(0)
        await expect(page.getByRole('button', { name: 'Open preferences', exact: true })).toBeFocused()
      })
      await info.attach('choice-menu-geometry', {
        body: JSON.stringify(geometry, null, 2),
        contentType: 'application/json',
      })
    })
