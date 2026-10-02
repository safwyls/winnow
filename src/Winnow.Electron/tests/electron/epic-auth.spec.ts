import { _electron as electron, expect, test } from '@playwright/test'
import { build } from 'esbuild'
import { mkdirSync, mkdtempSync, rmSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { join } from 'node:path'
const root = fileURLToPath(new URL('../../../../.tmp/', import.meta.url)),
  harness = join(root, 'epic-auth-harness.cjs'),
  preload = join(root, 'epic-auth-preload.cjs')
test.beforeAll(async () => {
  mkdirSync(root, { recursive: true })
  for (const [input, output] of [
    ['./epic-auth-main.ts', harness],
    ['../../src/preload/epic.ts', preload],
  ])
    await build({
      entryPoints: [fileURLToPath(new URL(input, import.meta.url))],
      outfile: output,
      bundle: true,
      platform: 'node',
      format: 'cjs',
      external: ['electron'],
      logLevel: 'silent',
    })
})
for (const mode of ['desktop', 'fullscreen'])
  test(`${mode} Epic capture uses the early isolated bridge exact redirects JSON harvest and social return`, async () => {
    const directory = mkdtempSync(join(root, 'winnow-electron-epic-fixture-'))
    const instance = await electron.launch({
      args: [harness, '--data-dir', directory, '--epic-preload', preload, '--mode', mode],
      env: { ...process.env, ELECTRON_DISABLE_SECURITY_WARNINGS: 'true' },
    })
    try {
      await expect.poll(() => instance.evaluate(() => !!(globalThis as any).epicTest)).toBe(true)
      for (const [index, route] of ['bridge', 'redirect', 'JSON', 'social', 'harvest'].entries()) {
        await instance.evaluate(() => (globalThis as any).epicTest.start())
        await expect
          .poll(() =>
            instance
              .context()
              .pages()
              .some((page) => page.url().startsWith('https://epic.example.test/id/login')),
          )
          .toBe(true)
        const page = instance
          .context()
          .pages()
          .find((page) => page.url().startsWith('https://epic.example.test/id/login'))!
        await expect(page.getByRole('heading', { name: 'Epic sign-in fixture' })).toBeVisible()
        expect(await instance.evaluate(() => (globalThis as any).epicTest.state.preloadErrors)).toEqual([])
        expect(
          await page.evaluate(() => ({
            early: (window as any).firstBridge,
            application: (window as any).appBridge,
            node: typeof (window as any).require,
          })),
        ).toEqual({ early: 'object', application: 'undefined', node: 'undefined' })
        await expect.poll(() => page.frames().length).toBe(3)
        for (const frame of page.frames().filter((frame) => frame !== page.mainFrame())) {
          await expect
            .poll(() =>
              frame.evaluate(() => ({
                early: (window as any).firstBridge,
                application: (window as any).appBridge,
              })),
            )
            .toEqual({ early: 'undefined', application: 'undefined' })
        }
        if (route === 'bridge')
          await page.getByRole('button', { name: 'Complete bridge' }).click({ noWaitAfter: true })
        if (route === 'redirect')
          await page.getByRole('link', { name: 'Complete redirect' }).click({ noWaitAfter: true })
        if (route === 'JSON')
          await page.getByRole('link', { name: 'Complete JSON' }).click({ noWaitAfter: true })
        if (route === 'social') {
          await page.getByRole('link', { name: 'Use social provider' }).click()
          await expect(page.getByRole('heading', { name: 'Social fixture' })).toBeVisible()
          expect(await page.evaluate(() => typeof (window as any).ue)).toBe('undefined')
          await page.getByRole('link', { name: 'Complete social sign-in' }).click({ noWaitAfter: true })
        }
        if (route === 'harvest') await page.getByRole('button', { name: 'Authenticate for harvest' }).click()
        await expect
          .poll(() => instance.evaluate(() => (globalThis as any).epicTest.state.results.length), {
            timeout: 12000,
          })
          .toBe(index + 1)
        const state = await instance.evaluate(() => (globalThis as any).epicTest.state)
        expect(state.results[index]).toMatchObject({
          succeeded: true,
          persisted: true,
          captureRoute:
            route === 'bridge'
              ? 'launcher bridge'
              : route === 'redirect'
                ? 'redirect'
                : route === 'harvest'
                  ? 'session harvest'
                  : 'JSON body',
        })
        expect(state.completions[index]).toMatchObject({
          kind: route === 'bridge' ? 1 : 0,
          state: 'fixture-state',
        })
        expect(state.forbidden).toEqual([])
      }
    } finally {
      await instance.close()
      rmSync(directory, { recursive: true, force: true })
    }
  })
