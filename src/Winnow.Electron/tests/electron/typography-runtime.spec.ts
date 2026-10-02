import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Locator,
  type Page,
} from '@playwright/test'
import electronPath from 'electron'
import { build } from 'esbuild'
import { mkdir, mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { closeFixture } from './fixture-cleanup'

const output = resolve('../..', '.tmp/typography-runtime-probe')
let app: ElectronApplication | undefined
let page: Page | undefined
const measurements: unknown[] = []
const errors: string[] = []
test.beforeAll(async () => {
  await mkdir(output, { recursive: true })
  await build({
    entryPoints: [resolve('tests/electron/typography-runtime-probe.tsx')],
    outfile: join(output, 'probe.js'),
    bundle: true,
    platform: 'browser',
    format: 'iife',
    jsx: 'automatic',
    define: { 'process.env.NODE_ENV': '"production"' },
    loader: { '.ttf': 'file', '.woff': 'file', '.woff2': 'file', '.svg': 'dataurl' },
    plugins: [
      {
        name: 'source-assets',
        setup(bundle) {
          bundle.onResolve({ filter: /\?raw$/ }, (args) => ({
            path: resolve(args.resolveDir, args.path.slice(0, -4)),
            namespace: 'raw',
          }))
          bundle.onLoad({ filter: /.*/, namespace: 'raw' }, async (args) => ({
            contents: await readFile(args.path, 'utf8'),
            loader: 'text',
          }))
        },
      },
    ],
    logLevel: 'silent',
  })
  await writeFile(
    join(output, 'index.html'),
    '<!doctype html><html><head><meta charset="utf-8"><link rel="stylesheet" href="probe.css"></head><body><div id="root"></div><script src="probe.js"></script></body></html>',
  )
})
test.beforeEach(() => {
  app = undefined
  page = undefined
  measurements.length = 0
  errors.length = 0
})
test.afterEach(async () => {
  try {
    await test.info().attach('native-typography-and-pixels', {
      body: JSON.stringify(measurements),
      contentType: 'application/json',
    })
    if (page && !page.isClosed() && test.info().status !== test.info().expectedStatus)
      await page.screenshot({ path: test.info().outputPath('before-teardown.png') })
  } finally {
    await closeFixture(app)
  }
  expect(errors).toEqual([])
})
async function start(kind: string, width: number, height: number, palette = 'winnow') {
  const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-typography-runtime-'))
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/typography-runtime-main.mjs'),
      '--data-dir',
      directory,
      join(output, 'index.html'),
      kind,
      palette,
      '--force-color-profile=srgb',
    ],
    env: Object.fromEntries(
      Object.entries(process.env).filter(
        ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
      ),
    ) as Record<string, string>,
    chromiumSandbox: true,
  })
  page = await app.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await app.evaluate(
    ({ BrowserWindow }, size) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setMinimumSize(0, 0)
      window.setContentSize(size.width, size.height)
      window.focus()
    },
    { width, height },
  )
  await expect(page.locator('main')).toBeVisible()
  await frame()
}
async function frame() {
  await page!.evaluate(async () => {
    await document.fonts.ready
    await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
  })
}
async function size(percent: number) {
  await page!.evaluate((value) => (window as any).typographyRuntimeProbe.size(value), percent)
  await expect(page!.locator('html')).toHaveCSS('--theme-text-scale', String(percent / 100))
  await frame()
}
async function metric(node: Locator) {
  return node.evaluate((node) => {
    const style = getComputedStyle(node),
      box = node.getBoundingClientRect()
    return {
      size: Number.parseFloat(style.fontSize),
      line: Number.parseFloat(style.lineHeight),
      family: style.fontFamily,
      width: box.width,
      height: box.height,
      outlineWidth: Number.parseFloat(style.outlineWidth),
      outlineOffset: Number.parseFloat(style.outlineOffset),
      outlineColor: style.outlineColor,
    }
  })
}
test('desktop source text and reading line heights follow the repeated live scale without changing a fixed control width', async () => {
  await start('desktop', 640, 480)
  const heading = page!.getByRole('heading', { name: 'Your library', exact: true })
  const paragraph = page!.locator('.studio-panel > p')
  const compact = page!.locator('[data-compact]')
  const button = page!.getByRole('button', { name: 'Apply', exact: true })
  const baseline = await metric(button)
  for (const percent of [120, 80, 100, 120]) {
    await size(percent)
    const factor = percent / 100
    const values = {
      percent,
      heading: await metric(heading),
      paragraph: await metric(paragraph),
      compact: await metric(compact),
      button: await metric(button),
    }
    expect(values.heading.size).toBeCloseTo(22 * factor, 3)
    expect(values.paragraph.size).toBeCloseTo(12 * factor, 3)
    expect(values.paragraph.line).toBeCloseTo(18 * factor, 3)
    expect(values.compact.size).toBeCloseTo(11 * factor, 3)
    expect(values.button.size).toBeCloseTo(baseline.size * factor, 3)
    expect(values.button.width).toBe(120)
    expect(values.paragraph.family).toContain('Avalon Data')
    expect(values.heading.family).toContain('Avalon Display')
    measurements.push(values)
  }
  await page!.screenshot({ path: test.info().outputPath('desktop-source-typography-120.png') })
})
test('all six plain native input roles follow the source fourteen pixel font and exact live scale sequence', async () => {
  await start('inputs', 600, 600)
  const controls = [
    page!.getByRole('button', { name: 'Choose', exact: true }),
    page!.getByRole('textbox', { name: 'Name', exact: true }),
    page!.getByRole('combobox', { name: 'Choice', exact: true }),
    page!.getByRole('checkbox', { name: 'Enabled', exact: true }),
    page!.getByRole('switch', { name: 'On', exact: true }),
    page!.getByRole('spinbutton', { name: 'Number', exact: true }),
  ]
  for (const percent of [100, 120, 80, 100]) {
    await size(percent)
    const values = []
    for (const control of controls) {
      const value = await metric(control)
      expect(value.size).toBeCloseTo((14 * percent) / 100, 3)
      expect(value.family).toContain('Avalon Data')
      values.push(value)
    }
    measurements.push({ percent, values })
  }
  await page!.screenshot({ path: test.info().outputPath('six-plain-native-inputs.png') })
})
test('source fullscreen page roles combine theme and page factors once while the shell clock and fixed icon remain independent', async () => {
  await start('fullscreen', 1920, 1080)
  for (const percent of [120, 80, 100, 120]) {
    await size(percent)
    const factor = percent / 100
    const values = {
      percent,
      copy: await metric(page!.locator('[data-source-copy]')),
      action: await metric(page!.getByRole('button', { name: 'Open game' })),
      heading: await metric(page!.getByRole('heading')),
      clock: await metric(page!.locator('.avalon-clock')),
      icon: await metric(page!.locator('[data-source-icon]')),
    }
    expect(values.copy.size).toBeCloseTo(28 * 1.4 * factor, 2)
    expect(values.action.size).toBeCloseTo(28 * 1.4 * factor, 2)
    expect(values.heading.size).toBeCloseTo(64 * factor, 2)
    expect(values.clock.size).toBeCloseTo(24 * factor, 2)
    expect(values.icon.width).toBe(32)
    expect(values.icon.height).toBe(32)
    measurements.push(values)
  }
  await page!.screenshot({ path: test.info().outputPath('fullscreen-source-120-page140.png') })
})
async function bitmap() {
  const result = await app!.evaluate(async ({ BrowserWindow }) => {
    const image = await BrowserWindow.getAllWindows()[0]!.webContents.capturePage()
    return { ...image.getSize(), pixels: image.toBitmap().toString('base64') }
  })
  return { ...result, pixels: Buffer.from(result.pixels, 'base64') }
}
async function bounds() {
  return page!.locator('[data-source-segments]').evaluate((node) => {
    const rect = (node: Element) => {
      const r = node.getBoundingClientRect()
      return { x: r.x, y: r.y, width: r.width, height: r.height }
    }
    return {
      group: rect(node),
      children: [...node.children].map(rect),
      border: Number.parseFloat(getComputedStyle(node).borderLeftWidth),
      radius: Number.parseFloat(getComputedStyle(node).borderTopLeftRadius),
    }
  })
}
for (const palette of ['winnow', 'rose-pine-dawn'])
  test(`${palette} actual segmented end states preserve all source rounded corner pixels and keep an inset focus ring`, async () => {
    await start('segments', 440, 160, palette)
    const group = page!.locator('[data-source-segments]')
    const buttons = group.getByRole('button')
    await page!.mouse.move(1, 1)
    await page!.evaluate(() => (document.activeElement as HTMLElement)?.blur())
    await frame()
    const initialBounds = await bounds()
    expect(initialBounds.group).toEqual({ x: 20, y: 20, width: 392, height: 120 })
    expect(initialBounds.border).toBe(4)
    expect(initialBounds.radius - initialBounds.border).toBe(12)
    expect(initialBounds.children.map(({ width, height }) => ({ width, height }))).toEqual(
      Array(3).fill({ width: 128, height: 112 }),
    )
    const baseline = await bitmap()
    expect([baseline.width, baseline.height]).toEqual([440, 160])
    const pixel = (image: typeof baseline, x: number, y: number) =>
      image.pixels.subarray((y * image.width + x) * 4, (y * image.width + x) * 4 + 4).toString('hex')
    await page!.screenshot({ path: test.info().outputPath(`${palette}-segments-baseline.png`) })
    const origin = { x: 24, y: 24, width: 384, height: 112 }
    for (const index of [0, 2]) {
      const button = buttons.nth(index)
      const check = async (state: string) => {
        await frame()
        expect(await bounds()).toEqual(initialBounds)
        const actual = await bitmap()
        const corners = []
        if (state === 'focus') {
          await expect(button).toBeFocused()
          const focus = await metric(button)
          expect(focus.outlineWidth).toBe(2)
          expect(focus.outlineOffset).toBe(-4)
          expect(focus.outlineColor).not.toBe('rgba(0, 0, 0, 0)')
          expect(actual.pixels.equals(baseline.pixels)).toBe(false)
        } else {
          if (state === 'selected') {
            const rect = initialBounds.children[index]!
            expect(pixel(actual, rect.x + 20, rect.y + 20)).not.toBe(
              pixel(baseline, rect.x + 20, rect.y + 20),
            )
          }
          for (const right of [false, true])
            for (const bottom of [false, true])
              for (const dx of [0, 1])
                for (const dy of [0, 1]) {
                  const x = origin.x + (right ? origin.width - 1 - dx : dx),
                    y = origin.y + (bottom ? origin.height - 1 - dy : dy)
                  expect(pixel(actual, x, y), `${palette}/${index}/${state} at${x},${y}`).toBe(
                    pixel(baseline, x, y),
                  )
                  corners.push({ x, y, pixel: pixel(actual, x, y) })
                }
        }
        measurements.push({ palette, index, state, corners, bounds: initialBounds })
        await page!.screenshot({ path: test.info().outputPath(`${palette}-segment-${index}-${state}.png`) })
      }
      await page!.evaluate((value) => (window as any).typographyRuntimeProbe.selected(value), index)
      await check('selected')
      if (index === 0) {
        await group.evaluate((node) => {
          ;(node as HTMLElement).style.overflow = 'visible'
        })
        await frame()
        const unclipped = await bitmap()
        expect(pixel(unclipped, origin.x + 1, origin.y + 1)).not.toBe(
          pixel(baseline, origin.x + 1, origin.y + 1),
        )
        await group.evaluate((node) => {
          ;(node as HTMLElement).style.removeProperty('overflow')
        })
        measurements.push({
          palette,
          negativeControl: 'overflow visible reveals square selected fill',
          pixel: pixel(unclipped, origin.x + 1, origin.y + 1),
        })
      }
      await page!.evaluate(() => (window as any).typographyRuntimeProbe.selected(null))
      await button.hover()
      await check('hover')
      await page!.mouse.down()
      await check('pressed')
      await page!.mouse.move(1, 1)
      await page!.mouse.up()
      await button.focus()
      await page!.keyboard.press('Shift+Tab')
      await page!.keyboard.press('Tab')
      await check('focus')
      await page!.evaluate(() => (document.activeElement as HTMLElement)?.blur())
    }
  })
