import { _electron as electron, expect, test } from '@playwright/test'
import { build } from 'esbuild'
import { mkdirSync, mkdtempSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { join } from 'node:path'
const root = fileURLToPath(new URL('../../../../.tmp/', import.meta.url)),
  harness = join(root, 'steam-policy-harness.cjs')
test.beforeAll(async () => {
  mkdirSync(root, { recursive: true })
  await build({
    entryPoints: [fileURLToPath(new URL('./steam-policy-main.ts', import.meta.url))],
    outfile: harness,
    bundle: true,
    platform: 'node',
    format: 'cjs',
    external: ['electron'],
    logLevel: 'silent',
  })
})
for (const mode of ['desktop', 'fullscreen'])
  test(`${mode} Steam provider popups remain isolated and normalized pagination captures only the two account pages`, async () => {
    const directory = mkdtempSync(join(root, 'winnow-electron-steam-policy-'))
    const instance = await electron.launch({
      args: [harness, '--data-dir', directory, ...(mode === 'fullscreen' ? ['--fullscreen'] : [])],
      env: { ...process.env, ELECTRON_DISABLE_SECURITY_WARNINGS: 'true' },
    })
    try {
      await expect
        .poll(() =>
          instance
            .context()
            .pages()
            .some((page) => page.url() === 'https://store.steampowered.com/login/'),
        )
        .toBe(true)
      const page = instance
        .context()
        .pages()
        .find((page) => page.url() === 'https://store.steampowered.com/login/')!
      await expect(page.getByRole('heading', { name: 'Steam policy fixture' })).toBeVisible()
      expect(await page.evaluate(() => typeof window.winnow)).toBe('undefined')
      const windows = await instance.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows().length)
      await page.getByRole('link', { name: 'Valve help' }).click()
      await expect(page).toHaveURL('https://help.steampowered.com/en/')
      await page.getByRole('link', { name: 'External help' }).click()
      await expect
        .poll(() => instance.evaluate(() => (globalThis as any).steamPolicyTest.state.external))
        .toEqual(['https://example.com/support'])
      for (const name of ['Application URL', 'Local service', 'Launcher command'])
        await page.getByRole('link', { name, exact: true }).click()
      await expect(page).toHaveURL('https://help.steampowered.com/en/')
      expect(await instance.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows().length)).toBe(
        windows,
      )
      expect(await instance.evaluate(() => (globalThis as any).steamPolicyTest.state.external)).toEqual([
        'https://example.com/support',
      ])
      const result = await instance.evaluate(() => (globalThis as any).steamPolicyTest.capture())
      expect(result).toMatchObject({
        captureOutcome: 'captured',
        licensesPagesWalked: 1,
        loadMoreClicks: 0,
        licensesStoppedBecause: 'exhausted',
        historyStoppedBecause: 'exhausted',
        licensesTruncated: false,
        historyTruncated: false,
      })
      expect(result.pages.additionalLicensesHtml).toHaveLength(1)
      expect(result.pages.source).toBe(0)
      expect(result.pages.steamId).toBe('76561198000000001')
      expect(result.pages.licensesHtml).toContain('Fixture one')
      expect(result.pages.additionalLicensesHtml[0]).toContain('Fixture two')
      expect(result.pages.historyHtml).toContain('Fixture history')
      expect(JSON.stringify(result)).not.toContain('private-fixture-token')
      expect(await instance.evaluate(() => (globalThis as any).steamPolicyTest.state.forbidden)).toEqual([])
    } finally {
      await instance.close()
    }
  })
