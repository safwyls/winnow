import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import { fillLibrarySearch } from './library-controls'

for (const [mode, scale] of [
  ['desktop', 1],
  ['fullscreen', 1],
  ['fullscreen', 1.4],
] as const)
  test(`${mode} update flags retain partial saves retry remaining releases and restore both editions at text ${scale}`, async ({}, info) => {
    const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-update-flags-'))
    const application = await electron.launch({
      executablePath: electronPath as unknown as string,
      args: [
        resolve('tests/electron/update-flags-main.mjs'),
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
      await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
      await application.evaluate(({ BrowserWindow }, mode) => {
        const window = BrowserWindow.getAllWindows()[0]!
        window.setFullScreen(false)
        window.setContentSize(mode === 'desktop' ? 1200 : 1920, mode === 'desktop' ? 800 : 1080)
        window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
      }, mode)
      await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
      if (mode === 'fullscreen') {
        await page.evaluate(async (scale) => {
          const result = await window.winnow.request({
            route: 'preferences.presentation.put',
            params: { preference: 'FullscreenTextScale' },
            body: { value: String(scale) },
          })
          if (!result.ok) throw Error(result.message)
        }, scale)
        await expect
          .poll(() =>
            page.evaluate(() =>
              getComputedStyle(document.documentElement).getPropertyValue('--fullscreen-text-scale').trim(),
            ),
          )
          .toBe(String(scale))
      }
      await page
        .getByRole('navigation', { name: 'Main navigation' })
        .getByRole('button', { name: 'Library', exact: true })
        .click()
      await fillLibrarySearch(page, 'Update Flag Fixture')
      await page.locator('.avalon-cover').first().click()
      const details = page.locator('.avalon-details')
      await details.getByRole('tab', { name: /^Updates/ }).click()
      await expect(details.locator('.update-row[data-unread="true"]')).toHaveCount(4)
      await expect(details.getByText('1 update landed while you were away.', { exact: true })).toBeVisible()
      await details.getByRole('button', { name: 'Mark as read', exact: true }).click()
      await expect(
        details.getByText("Couldn't mark every patch read. Try again.", { exact: true }),
      ).toBeVisible()
      await expect(details.locator('.update-row[data-unread="true"]')).toHaveCount(2)
      await expect(details.getByRole('button', { name: 'Mark as read', exact: true })).toBeEnabled()
      await expect(details.getByRole('button', { name: 'Show it again', exact: true })).toHaveCount(0)
      const errorBounds = await details
        .getByText("Couldn't mark every patch read. Try again.", { exact: true })
        .boundingBox()
      const readingBounds = await details.locator('.avalon-details-reading').boundingBox()
      expect(errorBounds!.y).toBeGreaterThanOrEqual(readingBounds!.y)
      expect(errorBounds!.y + errorBounds!.height).toBeLessThanOrEqual(
        readingBounds!.y + readingBounds!.height,
      )
      await expect(details.getByRole('button', { name: 'Mark as read', exact: true })).toBeFocused()
      await page.screenshot({ path: info.outputPath(`${mode}-${scale}-partial.png`) })
      await application.evaluate(() => {
        ;(globalThis as any).__updateFlagsFixture.fail = false
      })
      await details.getByRole('button', { name: 'Mark as read', exact: true }).click()
      const undo = details.getByRole('button', { name: 'Show it again', exact: true })
      await expect(undo).toBeEnabled()
      await expect(details.locator('.update-row[data-unread="true"]')).toHaveCount(0)
      await expect(
        details.getByText("1 update landed while you were away. You've marked it read.", { exact: true }),
      ).toBeVisible()
      const calls = await application.evaluate(() => {
        const state = (globalThis as any).__updateFlagsFixture
        return { releases: state.releases, calls: state.calls }
      })
      expect(calls.calls.map((call: any) => call.releaseId)).toEqual([
        calls.releases[1],
        calls.releases[0],
        calls.releases[1],
      ])
      expect(calls.calls[2].body.observedEventIds).toEqual([9003, 9002])
      await undo.scrollIntoViewIfNeeded()
      const bounds = await undo.boundingBox()
      expect(bounds!.y).toBeGreaterThanOrEqual(0)
      expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(mode === 'desktop' ? 800 : 1080)
      await page.screenshot({ path: info.outputPath(`${mode}-${scale}-read.png`) })
      await undo.click()
      await expect(details.locator('.update-row[data-unread="true"]')).toHaveCount(4)
      await expect(details.getByRole('button', { name: 'Mark as read', exact: true })).toBeEnabled()
      expect(errors).toEqual([])
    } finally {
      await closeFixture(application, directory)
    }
  })
