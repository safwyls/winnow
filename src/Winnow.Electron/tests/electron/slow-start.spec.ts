import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

for (const mode of ['desktop', 'fullscreen'] as const)
  test(`${mode} keeps a slow first connection in preparation and reveals the library after attachment`, async () => {
    const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-slow-start-'))
    const application = await electron.launch({
      executablePath: electronPath as unknown as string,
      args: [
        resolve('tests/electron/slow-start-main.mjs'),
        '--data-dir',
        directory,
        '--seed-sample',
        '--no-sync',
      ],
      env: Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ) as Record<string, string>,
      chromiumSandbox: true,
    })
    try {
      const page = await application.firstWindow(),
        errors: string[] = []
      page.on('pageerror', (error) => errors.push(error.message))
      await application.evaluate(({ BrowserWindow }, mode) => {
        const window = BrowserWindow.getAllWindows()[0]!
        window.setContentSize(1280, 720)
        window.setFullScreen(mode === 'fullscreen')
      }, mode)
      const loading = page.locator('.startup-presentation')
      await expect(loading).toHaveAttribute('data-phase', 'loading')
      await expect(loading).toHaveClass(new RegExp(mode))
      // Cross the former request deadline while keeping the actual SSE handshake pending.
      await page.waitForTimeout(13000)
      await expect(loading).toHaveAttribute('data-phase', 'loading')
      expect((await page.evaluate(() => window.winnow.connection())).connected).toBe(false)
      await application.evaluate(() => (globalThis as any).__slowConnection.release())
      await expect(loading).toHaveCount(0, { timeout: 30000 })
      await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
      await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
      expect(errors).toEqual([])
    } finally {
      await closeFixture(application, directory)
    }
  })
