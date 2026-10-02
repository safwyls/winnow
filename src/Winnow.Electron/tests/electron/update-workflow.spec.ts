import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtemp, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import type { ApplicationUpdateSnapshot } from '../../src/shared/bridge'
import { prebuiltActivationHelper, prebuiltBackend } from './prebuilt-backend'

for (const [mode, scale] of [
  ['desktop', 1],
  ['fullscreen', 1],
  ['fullscreen', 1.4],
] as const) {
  test(`${mode} update caption settings and controller actions retain source geometry and lifecycle at text ${scale}`, async ({}, info) => {
    const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-update-workflow-'))
    const application = await electron.launch({
      executablePath: electronPath as unknown as string,
      args: [
        resolve('tests/electron/update-workflow-main.mjs'),
        '--data-dir',
        directory,
        '--seed-sample',
        '--no-sync',
      ],
      env: {
        ...(Object.fromEntries(
          Object.entries(process.env).filter(
            ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
          ),
        ) as Record<string, string>),
        WINNOW_BACKEND_PATH: prebuiltBackend,
        WINNOW_ACTIVATION_HELPER_PATH: prebuiltActivationHelper,
      },
      chromiumSandbox: true,
    })
    try {
      const page = await application.firstWindow(),
        errors: string[] = []
      page.on('pageerror', (error) => errors.push(error.message))
      await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
      if (await page.getByRole('dialog', { name: 'Winnow setup', exact: true }).count())
        await page.getByRole('button', { name: 'Skip setup', exact: true }).click()
      await expect
        .poll(() => page.evaluate(() => window.winnow.connection()), { timeout: 45_000 })
        .toMatchObject({ connected: true })
      await application.evaluate(({ BrowserWindow }, mode) => {
        const window = BrowserWindow.getAllWindows()[0]
        window.setFullScreen(false)
        window.setContentSize(mode === 'desktop' ? 1200 : 1280, mode === 'desktop' ? 900 : 720)
        window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
        window.focus()
      }, mode)
      await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
      await expect(page.locator('.startup-presentation')).toHaveCount(0)
      if (mode === 'fullscreen')
        await page.evaluate(async (scale) => {
          const result = await window.winnow.request({
            route: 'preferences.presentation.put',
            params: { preference: 'FullscreenTextScale' },
            body: { value: String(scale) },
          })
          if (!result.ok) throw Error('Fixture scale failed')
        }, scale)
      await expect
        .poll(() =>
          page.evaluate(() =>
            getComputedStyle(document.documentElement).getPropertyValue('--fullscreen-text-scale').trim(),
          ),
        )
        .toBe(String(scale))
      await page.evaluate(() => {
        const state = { pressed: [] as number[] }
        Object.assign(window, { updatePad: state })
        Object.defineProperty(navigator, 'getGamepads', {
          configurable: true,
          value: () => [
            {
              index: 0,
              connected: true,
              mapping: 'standard',
              axes: [0, 0, 0, 0],
              buttons: Array.from({ length: 17 }, (_, index) => ({ pressed: state.pressed.includes(index) })),
            },
          ],
        })
      })
      async function tap(button: number) {
        // Directional input repeats at 180ms; leave a full neutral interval between taps.
        await page.evaluate(async () => {
          ;(window as any).updatePad.pressed = []
          const start = performance.now()
          while (performance.now() - start < 200)
            await new Promise<void>((done) => requestAnimationFrame(() => done()))
        })
        for (const pressed of [[], [button], []])
          await page.evaluate(async (pressed) => {
            ;(window as any).updatePad.pressed = pressed
            await new Promise<void>((done) =>
              requestAnimationFrame(() => requestAnimationFrame(() => done())),
            )
          }, pressed)
      }
      const publish = (patch: Partial<ApplicationUpdateSnapshot>) =>
        application.evaluate((_electron, patch) => (globalThis as any).__updates.publish(patch), patch)
      const state = () => application.evaluate(() => (globalThis as any).__updates.state)
      const caption = page.locator('.update-caption')
      await expect(caption).toHaveCount(0)
      await publish({
        canDownload: true,
        availableVersion: '2.0.0',
        status: 'A new version is available.',
        releaseUrl: 'https://github.com/safwyls/winnow/releases/tag/v2.0.0',
        downloadUrl:
          'https://github.com/safwyls/winnow/releases/download/v2.0.0/Winnow-2.0.0-win-x64-setup.exe',
      })
      await expect(caption).toBeVisible()
      if (mode === 'fullscreen')
        await expect(caption).toHaveCSS('font-size', `${Number((24 * scale).toFixed(1))}px`)
      if (mode === 'fullscreen') await expect(caption).toHaveCSS('white-space', 'nowrap')
      const before = await caption.boundingBox()
      if (mode === 'fullscreen') {
        const navigation = await page.getByRole('navigation', { name: 'Main navigation' }).boundingBox()
        expect(navigation!.x + navigation!.width).toBeLessThan(before!.x)
        expect(before!.x + before!.width).toBeLessThanOrEqual(1280)
      }
      if (mode === 'desktop') {
        const header = await page.locator('.avalon-header').boundingBox()
        expect(Math.abs(before!.y + before!.height / 2 - header!.y - header!.height / 2)).toBeLessThan(1)
        await caption.getByRole('button', { name: 'Update and restart' }).focus()
        await page.keyboard.press('Enter')
      } else {
        await expect(caption).toHaveText('Update available · Menu')
        await tap(9)
        const menu = page.getByRole('dialog', { name: 'Quick menu' })
        await expect(menu).toBeVisible()
        await expect(menu.getByRole('button', { name: 'Resume', exact: true })).toBeFocused()
        await tap(13)
        await expect(menu.getByRole('button', { name: 'Update and restart', exact: true })).toBeFocused()
        await tap(0)
        await expect(menu).toHaveCount(0)
      }
      await expect(caption.getByRole('progressbar')).toHaveAttribute('value', '34')
      const progress = await caption.boundingBox()
      expect(progress!.width).toBe(before!.width)
      expect(progress!.x).toBe(before!.x)
      expect((await state()).restarts).toBe(0)
      await publish({ progress: 78 })
      await expect(caption.getByRole('progressbar')).toHaveAttribute('value', '78')
      await page.screenshot({ path: info.outputPath(`${mode}-updating.png`) })
      await page.getByRole('button', { name: 'Settings', exact: true }).click()
      await page.getByRole('button', { name: 'Application', exact: true }).click()
      const panel = page.locator('.application-updates')
      await panel.getByRole('button', { name: 'Cancel download', exact: true }).click()
      await expect(panel.getByRole('button', { name: 'Download update', exact: true })).toBeEnabled()
      expect((await state()).downloads).toBe(1)
      expect((await state()).restarts).toBe(0)
      if (mode === 'fullscreen') {
        await panel.getByRole('button', { name: 'Check for updates', exact: true }).focus()
        for (const label of [
          'Download update',
          'Update and restart',
          'Release notes',
          'Download in browser',
        ]) {
          await tap(13)
          const action = panel.getByRole('button', { name: label, exact: true })
          await expect(action).toBeFocused()
          const bounds = await action.boundingBox()
          expect(bounds!.y).toBeGreaterThanOrEqual(0)
          expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(721)
        }
        for (const label of ['Release notes', 'Update and restart', 'Download update', 'Check for updates']) {
          await tap(12)
          await expect(panel.getByRole('button', { name: label, exact: true })).toBeFocused()
        }
      }
      await panel.getByRole('button', { name: 'Release notes', exact: true }).click()
      await panel.getByRole('button', { name: 'Download in browser', exact: true }).click()
      await expect.poll(async () => (await state()).links).toHaveLength(2)
      await panel.getByRole('button', { name: 'Download update', exact: true }).focus()
      await page.keyboard.press('Enter')
      await expect(panel.getByRole('button', { name: 'Cancel download', exact: true })).toBeEnabled()
      await expect(panel.getByRole('button', { name: 'Cancel download', exact: true })).toBeFocused()
      await application.evaluate(() => (globalThis as any).__updates.finish('failure'))
      await expect(panel).toContainText('The update could not be downloaded or verified. Try again.')
      await expect(panel.getByRole('button', { name: 'Download update', exact: true })).toBeEnabled()
      expect((await state()).restarts).toBe(0)
      await panel.getByRole('button', { name: 'Download update', exact: true }).click()
      await expect(panel.getByRole('button', { name: 'Cancel download', exact: true })).toBeEnabled()
      await application.evaluate(() => (globalThis as any).__updates.finish(true))
      await expect(panel.getByRole('button', { name: 'Restart to update', exact: true })).toBeEnabled()
      await expect(panel.locator(':focus')).toBeVisible()
      await expect(panel.locator(':focus')).toBeEnabled()
      expect((await state()).restarts).toBe(0)
      const restart = panel.getByRole('button', { name: 'Restart to update', exact: true })
      if (mode === 'fullscreen') {
        for (
          let count = 0;
          count < 5 && !(await restart.evaluate((node) => node === document.activeElement));
          count++
        ) {
          const direction = await panel.evaluate((element) => {
            const controls = [...element.querySelectorAll('button:not(:disabled), input:not(:disabled)')]
            return controls.findIndex((control) => control.textContent === 'Restart to update') >
              controls.indexOf(document.activeElement!)
              ? 13
              : 12
          })
          await tap(direction)
        }
        await expect(restart).toBeFocused()
      } else await restart.focus()
      await page.keyboard.press('Enter')
      await expect.poll(async () => (await state()).restarts).toBe(1)
      await publish({ status: 'Checking for updates…', busy: true })
      const recovery = panel.getByRole('status', { name: 'Update recovery' })
      await expect(recovery).toContainText('Your library is unchanged')
      await expect(panel.getByRole('alert')).toHaveCount(0)
      await recovery.scrollIntoViewIfNeeded()
      await page.screenshot({ path: info.outputPath(`${mode}-recovery.png`) })
      expect((await state()).downloads).toBe(3)
      expect(errors).toEqual([])
    } finally {
      const state = await application.evaluate(() => (globalThis as any).__updates.state).catch(() => null)
      await writeFile(
        info.outputPath('update-workflow-ledger.json'),
        JSON.stringify(
          {
            mode,
            scale,
            directory,
            prebuiltBackend,
            prebuiltActivationHelper,
            state,
            adapter:
              'Actual renderer/preload/main/backend with controlled update snapshot/action handlers; no installer execution. Controller input is simulated.',
          },
          null,
          2,
        ),
      )
      await closeFixture(application, directory)
    }
  })
}
