import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { build } from 'esbuild'
import { mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

type ScrollState = {
  selected: number
  requested: number
  imageReads: number
  imageQueries: number
  canceled: number
  deliveredLate: number
  decodeStarted: number
  decodeRetired: number
  decodeActive: number
  activeDecodes: { sourceAttribute: string | null; sourceRevoked: boolean }[]
  completedDecodedBytes: number
  completedRevoked: number
  bridgePending: number
  live: number
  pending: number
  decodedBytes: number
  maxPending: number
  maxLive: number
  mounted: number
  visible: { id: number; state?: string; decoded: boolean; source: string | null }[]
}
type Probe = {
  start(mode: 'desktop' | 'fullscreen'): void
  state(): ScrollState
  refreshSelections(): Promise<void>
}
let output: string, application: ElectronApplication | undefined, page: Page
const errors: string[] = []
const read = () =>
  page.evaluate(() => (window as unknown as { artworkScrollProbe: Probe }).artworkScrollProbe.state())
async function waitForSettled() {
  await expect
    .poll(
      async () => {
        const state = await read()
        return (
          state.visible.length > 0 &&
          state.visible.every((image) => image.state === 'ready' && image.decoded) &&
          state.pending === 0 &&
          // Chromium may leave an aborted decode promise pending. Its image and
          // blob must be released, and it must no longer own a cache work slot.
          state.activeDecodes.every((decode) => !decode.sourceAttribute && decode.sourceRevoked) &&
          state.bridgePending === 0
        )
      },
      {
        timeout: 15000,
        message:
          'The stopped library must load every visible cover without another scroll, resize or Details',
      },
    )
    .toBe(true)
}
async function lap(mode: 'desktop' | 'fullscreen', samples: ScrollState[]) {
  for (let step = 0; step < 45; step++) {
    if (mode === 'desktop')
      await page.locator('.avalon-library-scroll').evaluate((scroll) => {
        const row = scroll.querySelector<HTMLElement>('.avalon-grid-row')!
        scroll.scrollTop += row.getBoundingClientRect().height * 2
      })
    else await page.keyboard.press('PageDown')
    if (step % 7 === 3)
      await page.evaluate(() => {
        void (window as unknown as { artworkScrollProbe: Probe }).artworkScrollProbe.refreshSelections()
      })
    await page.evaluate(async () => {
      await new Promise<void>((resolve) =>
        requestAnimationFrame(() => requestAnimationFrame(() => resolve())),
      )
      await new Promise((resolve) => setTimeout(resolve, 25))
    })
    // Stop periodically so successful decodes and LRU eviction do not depend on
    // how much work a particular CI machine completes during uninterrupted churn.
    if (step % 5 === 4) await waitForSettled()
    samples.push(await read())
  }
  await waitForSettled()
}
test.beforeAll(async () => {
  output = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-artwork-scroll-build-'))
  await build({
    entryPoints: [resolve('tests/electron/artwork-scroll-renderer.tsx')],
    outfile: join(output, 'renderer.js'),
    bundle: true,
    platform: 'browser',
    format: 'iife',
    jsx: 'automatic',
    define: { 'process.env.NODE_ENV': '"production"' },
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
    join(output, 'index.html'),
    '<!doctype html><html><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src \'self\'; style-src \'self\' \'unsafe-inline\'; img-src \'self\' data: blob:; font-src \'self\'"><link rel="stylesheet" href="renderer.css"></head><body><div id="root"></div><script src="renderer.js"></script></body></html>',
  )
})
test.afterEach(async ({}, info) => {
  try {
    if (page && !page.isClosed()) {
      await info.attach('scroll-artwork-state', {
        body: JSON.stringify(await read(), null, 2),
        contentType: 'application/json',
      })
      await page.screenshot({ path: info.outputPath('scroll-stopped.png') })
    }
  } finally {
    await closeFixture(application)
    application = undefined
  }
  expect(errors).toEqual([])
})

for (const mode of ['desktop', 'fullscreen'] as const)
  test(`${mode} sustained scrolling through 1045 games retires canceled reads and loads the final visible covers`, async () => {
    test.setTimeout(90000)
    errors.length = 0
    const data = await mkdtemp(join(resolve('../..', '.tmp'), `winnow-artwork-scroll-${mode}-`))
    application = await electron.launch({
      executablePath: electronPath as unknown as string,
      args: [
        resolve('tests/electron/actions-probe-main.mjs'),
        '--data-dir',
        data,
        join(output, 'index.html'),
      ],
      env: Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ) as Record<string, string>,
      chromiumSandbox: true,
    })
    page = await application.firstWindow()
    page.on('pageerror', (error) => errors.push(error.message))
    await expect(page.getByText('Artwork scroll fixture ready')).toBeVisible()
    await page.clock.install()
    // Recreate the module-owned cache after installing the clock: its freshness
    // policy captures Date.now when the cache is constructed.
    await page.reload()
    await expect(page.getByText('Artwork scroll fixture ready')).toBeVisible()
    await page.evaluate(
      (mode) => (window as unknown as { artworkScrollProbe: Probe }).artworkScrollProbe.start(mode),
      mode,
    )
    await expect(page.locator('.avalon-library [data-avalon-game]').first()).toBeVisible()
    await expect
      .poll(async () => (await read()).visible.filter((image) => image.state === 'ready').length)
      .toBeGreaterThan(0)
    const samples: ScrollState[] = []
    if (mode === 'fullscreen')
      await page.locator('.avalon-retained-row[data-row-active="true"] [data-avalon-game]').first().focus()
    await test.step('First scrolling lap with selection refreshes and periodic stops', () =>
      lap(mode, samples))
    const churn = await read()
    expect(churn.selected).toBeGreaterThan(400)
    expect(churn.requested).toBeGreaterThan(400)
    expect(churn.canceled).toBeGreaterThan(0)
    expect(churn.completedDecodedBytes).toBeGreaterThan(32 * 1024 * 1024)
    expect(churn.completedRevoked).toBeGreaterThan(0)
    await test.step('Refresh visible artwork after the two-minute freshness limit', async () => {
      const before = await read()
      await page.clock.fastForward(121_000)
      await page.evaluate(() =>
        (window as unknown as { artworkScrollProbe: Probe }).artworkScrollProbe.refreshSelections(),
      )
      await expect.poll(async () => (await read()).imageReads).toBeGreaterThan(before.imageReads)
      await waitForSettled()
      const previous = new Map(before.visible.map((image) => [image.id, image.source]))
      for (const image of (await read()).visible) expect(image.source).not.toBe(previous.get(image.id))
    })
    await test.step('Collect inactive caches after five minutes and scroll another lap', async () => {
      const before = await read()
      await page.clock.fastForward(301_000)
      expect((await read()).imageQueries).toBeLessThan(before.imageQueries)
      if (mode === 'desktop')
        await page.locator('.avalon-library-scroll').evaluate((scroll) => {
          scroll.scrollTop = 0
        })
      else await page.keyboard.press('Control+Home')
      await waitForSettled()
      await lap(mode, samples)
      expect((await read()).imageReads).toBeGreaterThan(before.imageReads + 400)
    })
    const settled = await read()
    expect(settled.deliveredLate).toBeGreaterThan(0)
    expect(settled.completedDecodedBytes).toBeGreaterThan(32 * 1024 * 1024)
    expect(settled.completedRevoked).toBeGreaterThan(0)
    expect(settled.maxPending).toBeLessThanOrEqual(128)
    expect(settled.maxLive).toBeLessThanOrEqual(192)
    expect(settled.live).toBeLessThanOrEqual(settled.mounted)
    expect(settled.decodedBytes).toBeLessThanOrEqual(32 * 1024 * 1024)
    expect(await page.getByRole('dialog').count()).toBe(0)
    await test
      .info()
      .attach('scroll-samples', { body: JSON.stringify(samples, null, 2), contentType: 'application/json' })
  })
