import { _electron as electron, expect, test } from '@playwright/test'
import { build } from 'esbuild'
import { mkdirSync, mkdtempSync, rmSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { join } from 'node:path'
const root = fileURLToPath(new URL('../../../../.tmp/', import.meta.url)),
  harness = join(root, 'link-browser-harness.cjs')
test.beforeAll(async () => {
  mkdirSync(root, { recursive: true })
  await build({
    entryPoints: [fileURLToPath(new URL('./link-browser-main.ts', import.meta.url))],
    outfile: harness,
    bundle: true,
    platform: 'node',
    format: 'cjs',
    external: ['electron'],
    logLevel: 'silent',
  })
})

for (const mode of ['desktop', 'fullscreen'])
  test(`${mode} isolated reader keeps controls outside website and blocks application navigation`, async () => {
    const dataDirectory = mkdtempSync(join(root, 'winnow-electron-link-reader-'))
    const instance = await electron.launch({
      args: [harness, '--data-dir', dataDirectory, ...(mode === 'fullscreen' ? ['--fullscreen-test'] : [])],
      env: { ...process.env, ELECTRON_DISABLE_SECURITY_WARNINGS: 'true' },
    })
    try {
      await expect
        .poll(async () => instance.evaluate(() => !!(globalThis as any).linkBrowserTest?.reader()))
        .toBe(true)
      const controls =
        instance.windows().find((page) => page.url() === 'about:blank') ??
        (await instance.waitForEvent('window', { predicate: (page) => page.url() === 'about:blank' }))
      expect(controls).toBeTruthy()
      await expect(controls.getByLabel('Current address')).toHaveText('http://reading.example.test/article')
      const page = () =>
        instance
          .context()
          .pages()
          .find(
            (page) =>
              page.url().startsWith('http://reading.example.test') ||
              page.url().startsWith('https://reading.example.test'),
          )
      await expect.poll(() => !!page()).toBe(true)
      const article = page()!
      await expect(article.getByRole('heading', { name: 'Reading article' })).toBeVisible()
      await article.getByRole('link', { name: 'Next article' }).click()
      await expect(article).toHaveURL('https://reading.example.test/next')
      await expect(controls.getByLabel('Current address')).toHaveText('https://reading.example.test/next')
      await controls.getByRole('link', { name: 'Back', exact: true }).click({ noWaitAfter: true })
      await expect(article).toHaveURL('http://reading.example.test/article')
      await controls.getByRole('link', { name: 'Open in browser' }).click({ noWaitAfter: true })
      await expect
        .poll(() => instance.evaluate(() => (globalThis as any).linkBrowserTest.state.external))
        .toEqual(['http://reading.example.test/article'])
      if (mode === 'fullscreen') {
        expect(
          await instance.evaluate(() => (globalThis as any).linkBrowserTest.reader().isFullScreen()),
        ).toBe(true)
        await article.getByRole('button', { name: 'Controller action' }).click()
        await expect(article.locator('output')).toHaveText('1')
        await instance.evaluate(() => {
          ;(globalThis as any).linkBrowserTest.state.pressed = []
        })
        await controls.waitForTimeout(150)
        await instance.evaluate(() => {
          ;(globalThis as any).linkBrowserTest.state.pressed = [0]
        })
        await expect(article.locator('output')).toHaveText('2')
        await instance.evaluate(() => {
          ;(globalThis as any).linkBrowserTest.state.pressed = []
        })
      }
      expect(
        await article.evaluate(() => ({
          node: typeof (window as any).require,
          bridge: typeof (window as any).winnow,
        })),
      ).toEqual({ node: 'undefined', bridge: 'undefined' })
      await article.evaluate(() => document.getElementById('app')!.click())
      expect(article.url()).toBe('http://reading.example.test/article')
      await article.evaluate(() => document.getElementById('toolbar')!.click())
      expect(article.url()).toBe('http://reading.example.test/article')
      expect(await instance.evaluate(() => (globalThis as any).linkBrowserTest.state.external)).toEqual([
        'http://reading.example.test/article',
      ])
      expect(await instance.evaluate(() => (globalThis as any).linkBrowserTest.state.forbidden)).toEqual([])
      await controls.screenshot({ path: join(root, `link-reader-${mode}.png`) })
      await controls.getByRole('link', { name: 'Close', exact: true }).click({ noWaitAfter: true })
      await expect
        .poll(() => instance.evaluate(() => !!(globalThis as any).linkBrowserTest.reader()))
        .toBe(false)
    } finally {
      await instance.close()
      rmSync(dataDirectory, { recursive: true, force: true })
    }
  })
