import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { build } from 'esbuild'
import { mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

let application: ElectronApplication, page: Page
test.beforeAll(async () => {
  const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-artwork-placeholders-'))
  await build({
    entryPoints: [resolve('tests/electron/artwork-placeholders-renderer.tsx')],
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
  await expect(page.getByRole('button', { name: 'Open A distant island' })).toBeVisible()
  await expect
    .poll(() =>
      page.evaluate(
        () =>
          typeof (window as unknown as { placeholderFixture?: { configure?: unknown } }).placeholderFixture
            ?.configure,
      ),
    )
    .toBe('function')
})
test.afterAll(async () => closeFixture(application))

for (const theme of ['avalon', 'afterglow', 'rift', 'catalogue'])
  for (const mode of ['desktop', 'fullscreen'] as const) {
    test(`${theme} ${mode}: unavailable artwork has a static composition illustration on every shared surface`, async ({}, info) => {
      await page.evaluate(
        ({ theme, mode }) =>
          (
            window as unknown as {
              placeholderFixture: { configure(theme: string, mode: 'desktop' | 'fullscreen'): void }
            }
          ).placeholderFixture.configure(theme, mode),
        { theme, mode },
      )
      await expect(page.locator('.artwork[data-state="missing"]')).toHaveCount(2)
      await expect(page.locator('.avalon-backdrop[data-state="missing"]')).toHaveCount(1)
      const illustrations = page.locator('.artwork-placeholder-image')
      await expect(illustrations).toHaveCount(6)
      for (const image of await illustrations.all()) {
        await expect(image).toBeVisible()
        const visibleScenes = await image
          .locator('[data-placeholder-scene]')
          .evaluateAll((nodes) =>
            nodes
              .filter((node) => getComputedStyle(node).display !== 'none')
              .map((node) => node.getAttribute('data-placeholder-scene')),
          )
        expect(visibleScenes).toEqual([theme])
        expect(await image.evaluate((node) => node.getAnimations({ subtree: true }).length)).toBe(0)
        const geometry = await image.boundingBox()
        expect(geometry?.width).toBeGreaterThan(0)
        expect(geometry?.height).toBeGreaterThan(0)
      }
      const game = page.getByRole('button', { name: 'Open A distant island' })
      await game.focus()
      await expect(game).toBeFocused()
      await expect(game.getByText('A distant island')).toBeVisible()
      await expect(page.getByText('Screenshot unavailable')).toBeVisible()
      await expect(page.getByText('Preview unavailable')).toBeVisible()
      await page.emulateMedia({ reducedMotion: 'reduce' })
      expect(
        await illustrations.evaluateAll(
          (nodes) => nodes.flatMap((node) => node.getAnimations({ subtree: true })).length,
        ),
      ).toBe(0)
      await page.screenshot({ path: info.outputPath(`${theme}-${mode}.png`) })
    })
  }
