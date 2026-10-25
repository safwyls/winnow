import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

for (const restoreDuringRead of [false, true])
  test(`a cold background launch prepares with restoration during read ${restoreDuringRead}`, async () => {
    const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-startup-background-'))
    const application = await electron.launch({
      executablePath: electronPath as unknown as string,
      args: [
        resolve('tests/electron/startup-main.mjs'),
        '--data-dir',
        directory,
        '--seed-sample',
        '--no-sync',
        '--background',
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
        loading = page.locator('.startup-presentation')
      await expect(loading).toHaveAttribute('data-phase', 'loading')
      expect(await page.evaluate(() => window.winnow.presentationVisible?.())).toBe(false)
      expect(
        await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].isVisible()),
      ).toBe(false)
      await expect(page.locator('.startup-dragon')).toHaveAttribute('data-tracing', 'false')
      if (restoreDuringRead) {
        await application.evaluate(({ BrowserWindow }) => {
          BrowserWindow.getAllWindows()[0].show()
          BrowserWindow.getAllWindows()[0].focus()
        })
        await expect(page.locator('.startup-dragon')).toHaveAttribute('data-worker', 'true')
        await expect(loading).toHaveAttribute('data-phase', 'loading')
      }
      await application.evaluate(() => (globalThis as any).__startup.release())
      await expect(loading).toHaveCount(0)
      expect(
        await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].isVisible()),
      ).toBe(restoreDuringRead)
      await application.evaluate(({ BrowserWindow }) => {
        BrowserWindow.getAllWindows()[0].show()
        BrowserWindow.getAllWindows()[0].focus()
      })
      await expect(page.getByRole('navigation', { name: 'Main navigation' })).toBeVisible()
      await expect(page.getByRole('button', { name: 'For you', exact: true })).toHaveAttribute(
        'aria-current',
        'page',
      )
    } finally {
      await closeFixture(application, directory)
    }
  })

for (const reduced of [false, true])
  test(`startup preserves readiness retry reentry and worker rendering with reduced motion ${reduced}`, async ({}, info) => {
    const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-startup-'))
    const application = await electron.launch({
      executablePath: electronPath as unknown as string,
      args: [
        resolve('tests/electron/startup-main.mjs'),
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
      await application.evaluate(({ BrowserWindow }) => {
        const window = BrowserWindow.getAllWindows()[0]
        window.setContentSize(1280, 720)
        window.show()
        window.focus()
      })
      await page.bringToFront()
      page.on('console', (message) => {
        if (message.type() === 'error') errors.push(message.text())
      })
      const loading = page.locator('.startup-presentation'),
        mark = page.locator('.startup-dragon')
      await page.evaluate(() => {
        const state = { pressed: [] as number[] }
        Object.assign(window, { startupPad: state })
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
      async function tap(buttons: number[]) {
        await page.evaluate(async () => {
          ;(window as any).startupPad.pressed = []
          const start = performance.now()
          while (performance.now() - start < 200)
            await new Promise<void>((done) => requestAnimationFrame(() => done()))
        })
        for (const pressed of [buttons, []])
          await page.evaluate(async (pressed) => {
            ;(window as any).startupPad.pressed = pressed
            await new Promise<void>((done) =>
              requestAnimationFrame(() => requestAnimationFrame(() => done())),
            )
          }, pressed)
      }
      await expect(loading).toHaveAttribute('data-phase', 'loading')
      await expect(page.getByRole('status').filter({ hasText: 'Preparing your library' })).toBeVisible()
      await expect(mark).toHaveCSS('width', '144px')
      await expect(page.locator('.startup-wordmark')).toHaveCSS('font-size', '60px')
      expect(await loading.boundingBox()).toEqual({ x: 0, y: 0, width: 1280, height: 720 })
      await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toHaveCount(0)
      await expect(mark).toHaveAttribute('data-worker', 'true')
      await expect.poll(() => mark.getAttribute('data-elapsed').then(Number)).toBeGreaterThanOrEqual(1800)
      const framesDuringBlock = await page.evaluate(async () => {
        const mark = document.querySelector<HTMLElement>('.startup-dragon')!
        const before = Number(mark.dataset.frames),
          start = performance.now()
        while (performance.now() - start < 600) {
          /* exercise blocked UI publication */
        }
        await new Promise<void>((resolve) =>
          requestAnimationFrame(() => requestAnimationFrame(() => resolve())),
        )
        return Number(mark.dataset.frames) - before
      })
      expect(framesDuringBlock).toBeGreaterThan(15)
      await expect(loading).toHaveAttribute('data-phase', 'loading')
      await page.screenshot({ path: info.outputPath('desktop-startup.png') })
      const reads = await application.evaluate(() => (globalThis as any).__startup.state.reads)
      const firstLibrary = reads.find((read: any) => read.route === 'library.get')
      expect(firstLibrary.phase).not.toBeNull()
      expect(
        firstLibrary.painted || firstLibrary.visibility === 'hidden',
        JSON.stringify({
          firstLibrary,
          current: await page.evaluate(() => performance.getEntriesByType('paint')),
        }),
      ).toBe(true)
      await application.evaluate(() => (globalThis as any).__startup.release())
      await expect(loading).toHaveCount(0)
      await page.getByRole('button', { name: 'Library', exact: true }).click()
      await expect
        .poll(() => application.evaluate(() => (globalThis as any).__startup.state.activeFeeds))
        .toBe(0)

      await application.evaluate(({ BrowserWindow }, reduced) => {
        Object.assign((globalThis as any).__startup.state, { holdFeed: true, reduced, scale: 1.4 })
        BrowserWindow.getAllWindows()[0].webContents.send('winnow:fullscreen:changed', true)
      }, reduced)
      await expect(loading).toHaveAttribute('data-phase', 'loading')
      await expect(page.getByRole('button', { name: 'Back to desktop', exact: true })).toBeFocused()
      await expect(page.locator('.startup-stack p')).toHaveCSS('font-size', '39.2px')
      await expect(mark).toHaveCSS('width', '160px')
      await expect(mark).toHaveAttribute('data-tracing', String(!reduced))
      await tap([2, 5, 9])
      await expect(page.getByRole('dialog')).toHaveCount(1)
      await expect(loading).toHaveAttribute('data-phase', 'loading')
      await expect(page.getByRole('button', { name: 'Back to desktop', exact: true })).toBeFocused()
      await page.screenshot({ path: info.outputPath(`fullscreen-startup-${reduced}.png`) })
      // Exit and reenter while the primary read is still owned by the prior presentation.
      const count = await application.evaluate(
        () =>
          (globalThis as any).__startup.state.reads.filter((read: any) => read.route === 'feed.get').length,
      )
      if (reduced) await page.keyboard.press('Escape')
      else await tap([1])
      await expect(loading).toHaveClass(/desktop/)
      await expect(loading).toHaveAttribute('data-phase', 'loading')
      await application.evaluate(({ BrowserWindow }) =>
        BrowserWindow.getAllWindows()[0].webContents.send('winnow:fullscreen:changed', true),
      )
      await expect(loading).toHaveClass(/fullscreen/)
      await expect(loading).toHaveAttribute('data-phase', 'loading')
      await page.keyboard.press('Space')
      await expect(loading).toHaveClass(/desktop/)
      await expect(loading).toHaveAttribute('data-phase', 'loading')
      await application.evaluate(({ BrowserWindow }) =>
        BrowserWindow.getAllWindows()[0].webContents.send('winnow:fullscreen:changed', true),
      )
      await expect(loading).toHaveClass(/fullscreen/)
      await expect(loading).toHaveAttribute('data-phase', 'loading')
      await application.evaluate(() => (globalThis as any).__startup.release())
      await expect(loading).toHaveCount(0)
      expect(
        await application.evaluate(
          () =>
            (globalThis as any).__startup.state.reads.filter((read: any) => read.route === 'feed.get').length,
        ),
        JSON.stringify(await application.evaluate(() => (globalThis as any).__startup.state)),
      ).toBe(count)
      await page.getByRole('button', { name: 'Library', exact: true }).click()

      // A new entry refreshes, stays covered on failure, and retries without losing that page.
      await application.evaluate(({ BrowserWindow }) =>
        BrowserWindow.getAllWindows()[0].webContents.send('winnow:fullscreen:changed', false),
      )
      await expect(loading).toHaveCount(0)
      await application.evaluate(({ BrowserWindow }) => {
        ;(globalThis as any).__startup.state.failFeed = true
        BrowserWindow.getAllWindows()[0].webContents.send('winnow:fullscreen:changed', true)
      })
      await expect(loading).toHaveAttribute('data-phase', 'failed')
      await expect(page.getByRole('button', { name: 'Try again', exact: true })).toBeFocused()
      await expect(page.getByRole('status')).toHaveText("Couldn't prepare fullscreen. Try again.")
      await page.screenshot({ path: info.outputPath(`fullscreen-startup-failure-${reduced}.png`) })
      if (reduced) await page.keyboard.press('Enter')
      else await tap([0])
      await expect(loading).toHaveCount(0)
      await expect(page.getByRole('button', { name: 'Library', exact: true })).toHaveAttribute(
        'aria-current',
        'page',
      )
      await application.evaluate(({ BrowserWindow }) => {
        ;(globalThis as any).__startup.state.failFeed = true
        BrowserWindow.getAllWindows()[0].webContents.send('winnow:fullscreen:changed', false)
      })
      await expect(loading).toHaveAttribute('data-phase', 'failed')
      await expect(page.getByRole('status')).toHaveText("Couldn't prepare your library. Try again.")
      expect(
        await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].isEnabled()),
      ).toBe(true)
      await page.screenshot({ path: info.outputPath('desktop-startup-failure.png') })
      await page.getByRole('button', { name: 'Try again', exact: true }).click()
      await expect(loading).toHaveCount(0)
      await expect(page.getByRole('button', { name: 'Library', exact: true })).toHaveAttribute(
        'aria-current',
        'page',
      )
      if (!reduced) {
        await application.evaluate(({ BrowserWindow }) => {
          ;(globalThis as any).__startup.state.holdFeed = true
          BrowserWindow.getAllWindows()[0].webContents.send('winnow:fullscreen:changed', true)
        })
        await expect(loading).toHaveAttribute('data-phase', 'loading')
        await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].hide())
        await expect.poll(() => page.evaluate(() => window.winnow.presentationVisible?.())).toBe(false)
        await application.evaluate(() => (globalThis as any).__startup.release())
        await expect(loading).toHaveCount(0)
        expect(
          await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].isVisible()),
        ).toBe(false)
        await application.evaluate(({ BrowserWindow }) => {
          const window = BrowserWindow.getAllWindows()[0]
          window.show()
          window.focus()
        })
        await expect(page.getByRole('button', { name: 'Library', exact: true })).toHaveAttribute(
          'aria-current',
          'page',
        )
      }
      expect(errors).toEqual([])
    } finally {
      await closeFixture(application, directory)
    }
  })
