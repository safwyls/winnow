import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { build } from 'esbuild'
import { mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

type Read = {
  id: number
  kind: 'selection' | 'bytes'
  width?: number
  phase: 'library' | 'details'
  visible: number[]
  started: number
  finished: boolean
  canceled: boolean
}
type State = {
  visible: number[]
  mounted: number[]
  background: Read[]
  phase: string
  pending: number
  artworkPending: number
  maxBackgroundPending: number
  coverPixels: number
  retainedWorkIds: number[]
  readyVisible: number[]
  heldCover: { id: number; width: number; requestId: string; finished: boolean; canceled: boolean } | null
}
type FixtureOptions = { normalMotion?: boolean; fullShell?: boolean }
type Probe = {
  start(theme: string, mode: 'desktop' | 'fullscreen', options?: FixtureOptions): void
  holdOneOverscanCover(): void
  scrollOnce(): void
  state(): State
  filter(query: string): void
  hold(hold: boolean): void
  hidden(hidden: boolean): void
  churn(
    kind: 'scroll' | 'filter',
  ): Promise<{ first: number; last: number; visible: number[]; mounted: number[] }>
}
let output: string, application: ElectronApplication | undefined, page: Page
const errors: string[] = []
const read = () =>
  page.evaluate(() => (window as unknown as { backgroundPreloadProbe: Probe }).backgroundPreloadProbe.state())
async function settled() {
  await expect
    .poll(
      async () => {
        const state = await read()
        return (
          state.visible.length > 0 &&
          state.pending === 0 &&
          state.artworkPending === 0 &&
          state.visible.every((id) =>
            state.background.some(
              (entry) => entry.id === id && entry.kind === 'bytes' && entry.finished && !entry.canceled,
            ),
          )
        )
      },
      { timeout: 20000, message: 'Every actually visible title should warm after browsing settles' },
    )
    .toBe(true)
}

test.beforeAll(async () => {
  output = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-background-preload-build-'))
  await build({
    entryPoints: [resolve('tests/electron/background-preload-renderer.tsx')],
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
      const statePath = info.outputPath('background-preload-state.json')
      await writeFile(statePath, JSON.stringify(await read(), null, 2))
      await info.attach('background-preload-state', {
        path: statePath,
        contentType: 'application/json',
      })
      await page.screenshot({ path: info.outputPath('background-preload.png') })
    }
  } finally {
    await closeFixture(application)
    application = undefined
  }
  expect(errors).toEqual([])
})
async function start(theme: string, mode: 'desktop' | 'fullscreen', options: FixtureOptions = {}) {
  errors.length = 0
  const data = await mkdtemp(join(resolve('../..', '.tmp'), `winnow-background-preload-${theme}-${mode}-`))
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
    window.setContentSize(1280, 800)
    window.focus()
  })
  await expect(page.getByText('Background preload fixture ready')).toBeVisible()
  await page.emulateMedia({ reducedMotion: options.normalMotion ? 'no-preference' : 'reduce' })
  await page.mouse.move(0, 0)
  await page.evaluate(
    ({ theme, mode, options }) =>
      (window as unknown as { backgroundPreloadProbe: Probe }).backgroundPreloadProbe.start(
        theme,
        mode,
        options,
      ),
    { theme, mode, options },
  )
  await expect.poll(async () => (await read()).visible.length).toBeGreaterThan(0)
}

for (const theme of ['avalon', 'afterglow', 'rift', 'catalogue'])
  for (const mode of ['desktop', 'fullscreen'] as const)
    test(`${theme} ${mode}: visible backgrounds wait for browsing, exclude overscan, cancel and reuse warmed bytes`, async () => {
      test.setTimeout(90000)
      await start(theme, mode)
      await test.step('Only visible titles warm despite mounted overscan', async () => {
        await settled()
        await expect
          .poll(
            async () => {
              const state = await read()
              return state.visible.filter((id) => !state.retainedWorkIds.includes(id))
            },
            {
              message:
                'Every visible background in this small PNG fixture should remain available for Details',
            },
          )
          .toEqual([])
        const state = await read()
        expect(state.mounted.filter((id) => !state.visible.includes(id)).length).toBeGreaterThan(0)
        expect(state.background.length).toBeGreaterThan(0)
        for (const entry of state.background)
          expect(entry.visible, `Work ${entry.id} ${entry.kind} was outside the viewport`).toContain(entry.id)
        expect(state.maxBackgroundPending).toBeLessThanOrEqual(2)
      })
      for (const kind of ['scroll', 'filter'] as const)
        await test.step(`Continuous ${kind} activity does not start background work`, async () => {
          const churn = await page.evaluate(
            (kind) =>
              (window as unknown as { backgroundPreloadProbe: Probe }).backgroundPreloadProbe.churn(kind),
            kind,
          )
          expect(churn.last).toBe(churn.first)
          await settled()
        })
      await test.step('Scrolling cancels held obsolete reads', async () => {
        await page.evaluate(() => {
          const probe = (window as unknown as { backgroundPreloadProbe: Probe }).backgroundPreloadProbe
          probe.hold(true)
          probe.filter('Game 06')
        })
        await expect
          .poll(
            async () =>
              (await read()).background.filter((entry) => entry.kind === 'bytes' && !entry.finished).length,
          )
          .toBeGreaterThan(0)
        const before = await read()
        const churn = await page.evaluate(() =>
          (window as unknown as { backgroundPreloadProbe: Probe }).backgroundPreloadProbe.churn('scroll'),
        )
        expect(churn.last).toBe(churn.first)
        const after = await read()
        expect(after.background.filter((entry) => entry.canceled).length).toBeGreaterThan(
          before.background.filter((entry) => entry.canceled).length,
        )
        expect(after.pending).toBe(0)
        await page.evaluate(() => {
          const probe = (window as unknown as { backgroundPreloadProbe: Probe }).backgroundPreloadProbe
          probe.hold(false)
          probe.filter('Game 04')
          probe.hidden(true)
        })
        const hiddenStart = (await read()).background.length
        // This delay crosses the documented idle threshold while the retained pane is hidden.
        await page.waitForTimeout(650)
        expect((await read()).background.length).toBe(hiddenStart)
        await page.evaluate(() =>
          (window as unknown as { backgroundPreloadProbe: Probe }).backgroundPreloadProbe.hidden(false),
        )
        await settled()
      })
      await test.step('Opening a warmed title decodes cached background bytes', async () => {
        const before = await read(),
          id = before.visible[0]
        const reads = before.background.filter((entry) => entry.id === id && entry.kind === 'bytes').length
        await page
          .locator(
            `.background-probe-library [data-work-id="${id}"], .background-probe-library [data-rift-game="${id}"]`,
          )
          .first()
          .click()
        await expect(page.locator(`[data-fixture-details="${id}"]`)).toBeVisible()
        const image = page
          .locator(theme === 'avalon' ? '.avalon-detail-backdrop img' : '.detail-hero img')
          .first()
        await expect
          .poll(() =>
            image
              .evaluate(
                (image) =>
                  (image as HTMLImageElement).complete && (image as HTMLImageElement).naturalWidth > 0,
              )
              .catch(() => false),
          )
          .toBe(true)
        expect(
          (await read()).background.filter((entry) => entry.id === id && entry.kind === 'bytes').length,
        ).toBe(reads)
        expect((await read()).maxBackgroundPending).toBeLessThanOrEqual(2)
      })
    })

for (const mode of ['desktop', 'fullscreen'] as const)
  test(`avalon ${mode}: a slow overscan cover cannot starve newly scrolled backgrounds with normal motion`, async ({}, info) => {
    await start('avalon', mode, { normalMotion: true, fullShell: true })
    await settled()
    const initial = await read()
    await page.evaluate(() => {
      const probe = (window as unknown as { backgroundPreloadProbe: Probe }).backgroundPreloadProbe
      probe.holdOneOverscanCover()
      probe.scrollOnce()
    })
    if (mode === 'fullscreen') {
      // The first wheel selects the second visible row; the next one advances the two-row window.
      await expect(page.locator('.avalon-fullscreen-grid')).not.toHaveAttribute(
        'data-selected-id',
        String(initial.visible[0]),
      )
      await page.evaluate(() =>
        (window as unknown as { backgroundPreloadProbe: Probe }).backgroundPreloadProbe.scrollOnce(),
      )
    }
    await expect
      .poll(
        async () => {
          const state = await read()
          return (
            state.heldCover &&
            !state.heldCover.finished &&
            state.mounted.includes(state.heldCover.id) &&
            !state.visible.includes(state.heldCover.id)
          )
        },
        { message: 'One newly mounted offscreen cover stays pending in the real library' },
      )
      .toBeTruthy()
    await expect
      .poll(
        async () => {
          const state = await read()
          return (
            state.visible.length > 0 &&
            state.visible.every((id) => state.readyVisible.includes(id)) &&
            state.artworkPending === 1
          )
        },
        { message: 'Visible covers and the selected backdrop finish with only the overscan cover pending' },
      )
      .toBe(true)
    const scrolled = await read()
    const newlyVisible = scrolled.visible.filter((id) => !initial.visible.includes(id))
    expect(newlyVisible.length).toBeGreaterThan(0)
    expect(scrolled.artworkPending).toBe(1)
    await writeFile(info.outputPath('slow-cover-before-warm.json'), JSON.stringify(scrolled, null, 2))
    await expect
      .poll(
        async () => {
          const state = await read()
          return newlyVisible.every((id) => state.retainedWorkIds.includes(id))
        },
        {
          timeout: 10000,
          message: 'Newly scrolled visible backgrounds must warm without waiting for one offscreen cover',
        },
      )
      .toBe(true)
    const warmed = await read()
    expect(warmed.heldCover?.finished).toBe(false)
    expect(warmed.visible).not.toContain(warmed.heldCover?.id)
    expect(warmed.artworkPending).toBe(1)
    // The cached background must come from this browse stop, not the initial visible set.
    const id = newlyVisible.find((id) => !initial.retainedWorkIds.includes(id))!
    expect(id).toBeDefined()
    const reads = warmed.background.filter((entry) => entry.id === id && entry.kind === 'bytes').length
    await page.locator(`.background-probe-library [data-work-id="${id}"]`).click()
    await expect(page.locator(`[data-fixture-details="${id}"]`)).toBeVisible()
    await expect
      .poll(() =>
        page
          .locator('[data-fixture-details] .avalon-detail-backdrop img')
          .first()
          .evaluate(
            (image) => (image as HTMLImageElement).complete && (image as HTMLImageElement).naturalWidth > 0,
          )
          .catch(() => false),
      )
      .toBe(true)
    expect(
      (await read()).background.filter((entry) => entry.id === id && entry.kind === 'bytes').length,
    ).toBe(reads)
  })
