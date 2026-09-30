import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { build } from 'esbuild'
import { mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

type Options = {
  view: 'padding' | 'cover' | 'backdrop' | 'empty'
  width: number
  height: number
  source: 'vertical' | 'horizontal'
  clear: boolean
  mode: 'desktop' | 'fullscreen'
  cinematic: boolean
  workId: number
  dimmed: boolean
}
type Probe = {
  configure(value: Partial<Options>): void
  fit(value: boolean): void
  freeze: boolean
  step(time: number): void
  pendingFrames: number
  requests: { id: string; width: number }[]
}
let application: ElectronApplication, page: Page
test.beforeAll(async () => {
  const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-fullscreen-artwork-'))
  await build({
    entryPoints: [resolve('tests/electron/fullscreen-artwork-renderer.tsx')],
    outfile: join(directory, 'renderer.js'),
    bundle: true,
    platform: 'browser',
    format: 'esm',
    jsx: 'automatic',
    loader: { '.ttf': 'file', '.woff2': 'file', '.svg': 'dataurl' },
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
    join(directory, 'index.html'),
    '<!doctype html><html><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src \'self\'; style-src \'self\' \'unsafe-inline\'; img-src \'self\' data: blob:; font-src \'self\'"><link rel="stylesheet" href="renderer.css"></head><body><div id="root"></div><script type="module" src="renderer.js"></script></body></html>',
  )
  const env = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/actions-probe-main.mjs'),
      '--data-dir',
      directory,
      join(directory, 'index.html'),
    ],
    env,
    chromiumSandbox: true,
  })
  page = await application.firstWindow()
})
test.afterAll(async () => closeFixture(application))
test.beforeEach(async () => {
  await page.reload()
  await expect(page.locator('[data-padding-fixture] canvas')).toBeVisible()
})
async function configure(options: Partial<Options>) {
  await page.evaluate(
    (value) => (window as unknown as { fullscreenArtwork: Probe }).fullscreenArtwork.configure(value),
    options,
  )
}
async function fit(value: boolean) {
  await page.evaluate(
    (value) => (window as unknown as { fullscreenArtwork: Probe }).fullscreenArtwork.fit(value),
    value,
  )
}
async function canvasPixels() {
  return page.locator('.cover-padding').evaluate((node) => {
    const canvas = node as HTMLCanvasElement
    return {
      width: canvas.width,
      height: canvas.height,
      pixels: Array.from(canvas.getContext('2d')!.getImageData(0, 0, canvas.width, canvas.height).data),
    }
  })
}
function pixel(value: { width: number; pixels: number[] }, x: number, y: number) {
  const offset = (y * value.width + x) * 4
  return value.pixels.slice(offset, offset + 4)
}

test('fullscreen padding keeps separate top and bottom artwork edges, clears on Fill and release, and restores on Fit', async () => {
  await expect.poll(async () => pixel(await canvasPixels(), 10, 0)).toEqual([240, 0, 0, 255])
  const original = await canvasPixels()
  expect([original.width, original.height]).toEqual([20, 60])
  expect(pixel(original, 10, 59)).toEqual([0, 0, 220, 255])
  expect(pixel(original, 10, 30)[3]).toBe(0)
  await fit(false)
  await expect.poll(async () => (await canvasPixels()).pixels.every((value) => value === 0)).toBe(true)
  await fit(true)
  await expect.poll(canvasPixels).toEqual(original)
  await configure({ clear: true })
  await expect.poll(async () => (await canvasPixels()).pixels.every((value) => value === 0)).toBe(true)
})

test('fullscreen padding fills tall artwork side gaps without painting the image center', async () => {
  await configure({ width: 60, height: 20, source: 'horizontal' })
  await expect.poll(async () => pixel(await canvasPixels(), 0, 10)).toEqual([0, 180, 0, 255])
  const value = await canvasPixels()
  expect([value.width, value.height]).toEqual([60, 20])
  expect(pixel(value, 59, 10)).toEqual([240, 0, 0, 255])
  expect(pixel(value, 30, 10)[3]).toBe(0)
})

test('real fullscreen cover composes edge padding with dormancy and desktop retains its original surface', async ({}, info) => {
  await configure({ view: 'cover', width: 120, height: 180 })
  const art = page.locator('.avalon-cover > .artwork'),
    image = art.locator('img')
  await expect(image).toHaveClass('art-ready')
  await expect(art.locator('.cover-padding')).toBeVisible()
  await expect.poll(async () => pixel(await canvasPixels(), 20, 0)).toEqual([240, 0, 0, 255])
  const source = await image.getAttribute('src')
  await configure({ dimmed: true })
  await expect(art).not.toHaveCSS('filter', 'none')
  await expect(art.locator('.cover-padding')).toHaveCSS('filter', 'none')
  await fit(false)
  await expect(image).toHaveCSS('object-fit', 'cover')
  await expect.poll(async () => (await canvasPixels()).pixels.every((value) => value === 0)).toBe(true)
  await fit(true)
  await expect.poll(async () => pixel(await canvasPixels(), 20, 0)).toEqual([240, 0, 0, 255])
  await expect(image).toHaveAttribute('src', source!)
  await page.screenshot({ path: info.outputPath('fullscreen-cover-edge-padding.png') })
  await configure({ mode: 'desktop' })
  await expect(image).toHaveClass('art-ready')
  await expect(art.locator('.cover-padding')).toHaveCount(0)
  await page.screenshot({ path: info.outputPath('desktop-cover-original-padding.png') })
})

async function size(width: number) {
  await application.evaluate(
    ({ BrowserWindow }, width) => BrowserWindow.getAllWindows()[0]!.setContentSize(width, 1080),
    width,
  )
  await expect.poll(() => page.evaluate(() => innerWidth)).toBe(width)
}
const layers = () =>
  page.locator('.avalon-backdrop-art').evaluateAll((nodes) =>
    nodes.map((node) => {
      const box = node.getBoundingClientRect(),
        image = node.querySelector('img')!,
        parent = node.parentElement!
      return {
        width: box.width,
        height: box.height,
        left: box.left,
        top: box.top,
        opacity: Number(getComputedStyle(parent).opacity),
        outgoing: parent.hasAttribute('data-outgoing'),
        fitted: node.hasAttribute('data-fitted'),
        mask: getComputedStyle(image).maskImage,
        veil: getComputedStyle(node.querySelector('.avalon-backdrop-veil')!).backgroundImage,
      }
    }),
  )
async function step(time: number) {
  await page.evaluate(
    (time) => (window as unknown as { fullscreenArtwork: Probe }).fullscreenArtwork.step(time),
    time,
  )
}
async function renderedPixel(x: number, y: number) {
  const png = await page.screenshot({
    omitBackground: true,
    clip: { x: Math.floor(x), y: Math.floor(y), width: 1, height: 1 },
  })
  return application.evaluate(
    ({ nativeImage }, data) =>
      Array.from(nativeImage.createFromDataURL(`data:image/png;base64,${data}`).toBitmap()),
    png.toString('base64'),
  )
}
for (const cinematic of [false, true])
  for (const width of [1920, 2520]) {
    test(`fullscreen Steam hero preserves pixel fades and independent transition geometry with cinematic ${cinematic} at ${width}`, async ({}, info) => {
      await size(width)
      await configure({ view: 'backdrop', cinematic })
      const image = page.locator('.avalon-backdrop img')
      await expect(image).toBeVisible()
      await expect.poll(async () => (await layers())[0]?.height).toBe(width === 2520 ? 813.75 : 1080)
      let layer = (await layers())[0]
      expect(layer).toMatchObject({ width, left: 0, top: 0, fitted: width === 2520 })
      await expect(image).toHaveCSS('object-fit', 'cover')
      expect(
        await page.evaluate(() =>
          (window as unknown as { fullscreenArtwork: Probe }).fullscreenArtwork.requests.at(-1),
        ),
      ).toEqual({ id: 'hero', width: width === 2520 ? 2560 : 3840 })
      expect(layer.veil).toContain(width === 2520 ? '98%' : cinematic ? '55%' : '85%')
      await page.screenshot({ path: info.outputPath(`hero-${cinematic}-${width}.png`) })
      if (width === 2520) {
        const withoutReading = await page.addStyleTag({
          content: '.avalon-backdrop-reading-veil{display:none!important}',
        })
        const ground = await renderedPixel(width / 2, layer.height - 2)
        for (const [channel, expected] of [26, 27, 7].entries())
          expect(Math.abs(ground[channel] - expected)).toBeLessThanOrEqual(2)
        expect(ground[3]).toBe(255)
        await withoutReading.evaluate((node) => node.parentNode!.removeChild(node))
        await image.evaluate((image) => {
          const clone = image.cloneNode() as HTMLImageElement,
            bounds = image.getBoundingClientRect()
          clone.dataset.maskProbe = 'true'
          Object.assign(clone.style, {
            position: 'fixed',
            left: '0',
            top: '0',
            width: `${bounds.width}px`,
            height: `${bounds.height}px`,
            objectFit: 'cover',
            maskImage: getComputedStyle(image).maskImage,
            visibility: 'visible',
          })
          document.body.append(clone)
        })
        const transparent = await page.addStyleTag({
          content:
            'html,body{background:transparent!important}body>*{visibility:hidden}img[data-mask-probe]{visibility:visible}',
        })
        expect(await renderedPixel(width / 2, 0)).toEqual([255, 255, 255, 255])
        expect((await renderedPixel(width / 2, layer.height - 2))[3]).toBe(0)
        await page.locator('[data-mask-probe]').evaluate((node) => node.remove())
        await transparent.evaluate((node) => node.parentNode!.removeChild(node))
      } else expect(layer.mask).toBe('none')
      await page.evaluate(() => {
        ;(window as unknown as { fullscreenArtwork: Probe }).fullscreenArtwork.freeze = true
      })
      await configure({ workId: 2 })
      await expect(page.locator('.avalon-backdrop-art')).toHaveCount(2)
      await step(1000)
      await step(1090)
      await expect.poll(async () => (await layers())[1]?.opacity).toBe(0.5)
      expect((await layers()).map(({ height, outgoing }) => ({ height, outgoing }))).toEqual([
        { height: width === 2520 ? 813.75 : 1080, outgoing: true },
        { height: 1080, outgoing: false },
      ])
      await step(1180)
      await configure({ workId: 3 })
      await expect(page.locator('.avalon-backdrop-art')).toHaveCount(2)
      await step(2000)
      await step(2090)
      expect((await layers()).map(({ height, outgoing }) => ({ height, outgoing }))).toEqual([
        { height: 1080, outgoing: true },
        { height: width === 2520 ? 813.75 : 1080, outgoing: false },
      ])
      await size(3840)
      await expect.poll(async () => (await layers()).at(-1)?.height).toBe(1080)
      layer = (await layers()).at(-1)!
      expect(layer.width).toBeCloseTo(3344.516129, 1)
      expect(layer.left).toBeCloseTo(247.741935, 1)
      expect(layer.veil).toContain('65%')
      expect(layer.veil).toContain('98%')
      await expect
        .poll(() =>
          page.evaluate(
            () =>
              (window as unknown as { fullscreenArtwork: Probe }).fullscreenArtwork.requests.at(-1)?.width,
          ),
        )
        .toBe(3840)
      await size(1920)
      await expect.poll(async () => (await layers()).at(-1)?.width).toBe(1920)
      for (const row of await layers())
        expect(row).toMatchObject({ width: 1920, height: 1080, left: 0, top: 0, mask: 'none' })
      expect((await layers()).at(-1)!.veil).toContain(cinematic ? '55%' : '85%')
      await configure({ view: 'empty' })
      await expect(page.locator('.avalon-backdrop')).toHaveCount(0)
      expect(
        await page.evaluate(
          () => (window as unknown as { fullscreenArtwork: Probe }).fullscreenArtwork.pendingFrames,
        ),
      ).toBe(0)
    })
  }
