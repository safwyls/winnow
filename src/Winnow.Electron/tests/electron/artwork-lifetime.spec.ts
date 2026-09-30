import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { build } from 'esbuild'
import { mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

type Probe = {
  requests: { width: number; requestId: string }[]
  cancels: string[]
  failNext: boolean
  hold: boolean
  warmSource: string
  release(): void
  state(): { live: number; pending: number; decoded: number }
}
let application: ElectronApplication, page: Page
test.beforeAll(async () => {
  const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-artwork-probe-'))
  await build({
    entryPoints: [resolve('tests/electron/artwork-probe-renderer.tsx')],
    outfile: join(directory, 'renderer.js'),
    bundle: true,
    platform: 'browser',
    format: 'iife',
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
    '<!doctype html><html><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src \'self\'; style-src \'self\' \'unsafe-inline\'; img-src \'self\' data: blob:; font-src \'self\'"><link rel="stylesheet" href="renderer.css"></head><body><div id="root"></div><script src="renderer.js"></script></body></html>',
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
async function surface(mode: 'desktop' | 'fullscreen') {
  await page.reload()
  await expect(page.getByRole('button', { name: 'Attach cover', exact: true })).toBeVisible()
  if (mode === 'fullscreen') await page.getByRole('button', { name: 'Change surface' }).click()
  await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
}
const read = () =>
  page.evaluate(() => {
    const probe = (window as unknown as { artworkProbe: Probe }).artworkProbe
    return { ...probe.state(), requests: probe.requests, cancels: probe.cancels, source: probe.warmSource }
  })
for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} first layout uses warm display pixels and fit, dormancy and selection preserve the image`, async ({}, info) => {
    await surface(mode)
    await page.getByRole('button', { name: 'Warm pixels', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Pixels warm', exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Attach cover', exact: true }).click()
    const image = page.locator('.avalon-cover img'),
      tile = page.locator('.avalon-cover')
    await expect(image).toHaveClass('art-ready')
    const geometry = await tile.boundingBox(),
      source = await image.getAttribute('src')
    expect(geometry?.width).toBe(400)
    expect((await read()).requests.map((request) => request.width)).toEqual([480])
    expect(source).toBe((await read()).source)
    await image.evaluate((element) => {
      Object.assign(window, { retainedCoverImage: element })
    })
    for (const [button, fit] of [
      ['Fill', 'cover'],
      ['Fit', 'contain'],
    ]) {
      await page.getByRole('button', { name: button, exact: true }).click()
      await expect(image).toHaveCSS('object-fit', fit)
      expect(await tile.boundingBox()).toEqual(geometry)
      await expect(image).toHaveAttribute('src', source!)
    }
    const originalFilter = await tile
      .locator('.artwork')
      .evaluate((element) => getComputedStyle(element).filter)
    await page.getByRole('button', { name: 'Change dormancy' }).click()
    await expect(tile.locator('.artwork')).toHaveCSS('filter', 'none')
    await page.getByRole('button', { name: 'Change dormancy' }).click()
    await expect
      .poll(() => tile.locator('.artwork').evaluate((element) => getComputedStyle(element).filter))
      .toBe(originalFilter)
    await page.getByRole('button', { name: 'Change selection' }).click()
    await page.getByRole('button', { name: 'Change selection' }).click()
    expect(
      await image.evaluate(
        (element) => element === (window as unknown as { retainedCoverImage: Element }).retainedCoverImage,
      ),
    ).toBe(true)
    expect((await read()).requests).toHaveLength(1)
    await page.screenshot({ path: info.outputPath('warm-cover.png') })
    await page.getByRole('button', { name: 'Detach cover' }).click()
    expect(
      await page.evaluate(() =>
        (window as unknown as { retainedCoverImage: HTMLImageElement }).retainedCoverImage.getAttribute(
          'src',
        ),
      ),
    ).toBeNull()
    expect((await read()).live).toBe(0)
    await page.getByRole('button', { name: 'Attach cover' }).click()
    await expect(image).toHaveClass('art-ready')
    expect((await read()).requests).toHaveLength(1)
    await page.getByRole('button', { name: 'Detach cover' }).click()
    expect((await read()).live).toBe(0)
  })
  test(`${mode} reattaches after a transient image failure at the same display width`, async () => {
    await surface(mode)
    await page.evaluate(() => {
      ;(window as unknown as { artworkProbe: Probe }).artworkProbe.failNext = true
    })
    await page.getByRole('button', { name: 'Attach cover' }).click()
    await expect(page.locator('.artwork')).toHaveAttribute('data-state', 'error')
    await page.getByRole('button', { name: 'Detach cover' }).click()
    await page.getByRole('button', { name: 'Attach cover' }).click()
    await expect(page.locator('.avalon-cover img')).toHaveClass('art-ready')
    expect((await read()).requests.map((request) => request.width)).toEqual([480, 480])
  })
  test(`${mode} canceled image work retires before reattachment starts its replacement`, async () => {
    await surface(mode)
    await page.evaluate(() => {
      ;(window as unknown as { artworkProbe: Probe }).artworkProbe.hold = true
    })
    await page.getByRole('button', { name: 'Attach cover' }).click()
    await expect.poll(async () => (await read()).requests.length).toBe(1)
    const request = (await read()).requests[0]!
    await page.getByRole('button', { name: 'Detach cover' }).click()
    expect((await read()).cancels).toContain(request.requestId)
    await page.getByRole('button', { name: 'Attach cover' }).click()
    expect((await read()).requests).toHaveLength(1)
    await page.evaluate(() => {
      const probe = (window as unknown as { artworkProbe: Probe }).artworkProbe
      probe.hold = false
      probe.release()
    })
    await expect(page.locator('.avalon-cover img')).toHaveClass('art-ready')
    expect((await read()).requests).toHaveLength(2)
    expect((await read()).pending).toBe(0)
    await page.getByRole('button', { name: 'Detach cover' }).click()
    expect((await read()).live).toBe(0)
  })
}
