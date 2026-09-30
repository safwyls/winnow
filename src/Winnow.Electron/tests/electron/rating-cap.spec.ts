import { test, expect, _electron as electron, type Page } from '@playwright/test'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { DatabaseSync } from 'node:sqlite'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import type { ApiRequest } from '../../src/shared/bridge'
import type { LibraryPreferences, LibraryResponse, ManualGame } from '../../src/renderer/api/types'

async function api<T>(page: Page, input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const response = await window.winnow.request(input)
    if (!response.ok) throw Error(`${input.route}: ${response.status}`)
    return response.data
  }, input) as Promise<T>
}
for (const [mode, scale] of [
  ['desktop', 1],
  ['fullscreen', 1],
  ['fullscreen', 1.4],
] as const)
  test(`${mode} rating cap preserves six levels explicit-content composition counts and reload at text ${scale}`, async ({}, info) => {
    const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-rating-cap-'))
    const application = await electron.launch({
      executablePath: electronPath as unknown as string,
      args: [resolve('.'), '--data-dir', directory, '--no-sync'],
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
      await page.getByRole('button', { name: 'Skip setup', exact: true }).click()
      await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
      const adult = await api<ManualGame>(page, {
        route: 'manual.create',
        body: { title: 'Adults only fixture' },
      })
      const restricted = await api<ManualGame>(page, {
        route: 'manual.create',
        body: { title: '18 plus fixture' },
      })
      await api(page, { route: 'manual.create', body: { title: 'Unrated fixture' } })
      const database = new DatabaseSync(join(directory, 'winnow.db'))
      try {
        database.exec('PRAGMA busy_timeout=5000')
        const insert = database.prepare(
          'INSERT INTO work_maturity (work_id, source, ratings, descriptors, observed_at) VALUES (?, ?, ?, ?, ?)',
        )
        for (const [workId, rating] of [
          [adult.workId, 'esrb:ao'],
          [restricted.workId, 'pegi:18'],
        ] as const)
          insert.run(workId, 'igdb', rating, '', '2026-09-29 12:00:00')
      } finally {
        database.close()
      }
      await api(page, {
        route: 'preferences.library.put',
        body: { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'adults_only' },
      })
      await application.evaluate(
        ({ BrowserWindow }, { mode, scale }) => {
          const window = BrowserWindow.getAllWindows()[0]!
          window.setFullScreen(false)
          window.setContentSize(
            mode === 'desktop' ? 1200 : scale === 1 ? 1280 : 1920,
            mode === 'desktop' ? 800 : scale === 1 ? 720 : 1080,
          )
          window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
          window.focus()
        },
        { mode, scale },
      )
      await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
      if (mode === 'fullscreen') {
        await api(page, {
          route: 'preferences.presentation.put',
          params: { preference: 'FullscreenTextScale' },
          body: { value: String(scale) },
        })
        await expect
          .poll(() =>
            page.evaluate(() =>
              getComputedStyle(document.documentElement).getPropertyValue('--fullscreen-text-scale').trim(),
            ),
          )
          .toBe(String(scale))
      }
      const openControl = async () => {
        if (mode === 'desktop') {
          await page
            .getByRole('navigation', { name: 'Main navigation' })
            .getByRole('button', { name: 'Library', exact: true })
            .click()
          await page.getByRole('button', { name: 'Display preferences', exact: true }).click()
          await expect(page.getByRole('dialog', { name: 'Display preferences' })).toBeVisible()
        } else {
          await page
            .getByRole('navigation', { name: 'Main navigation' })
            .getByRole('button', { name: 'Settings', exact: true })
            .click()
          await page
            .getByRole('navigation', { name: 'Settings section' })
            .getByRole('button', { name: 'Library', exact: true })
            .click()
        }
      }
      await openControl()
      if (mode === 'desktop') {
        const fit = page.getByRole('combobox', { name: 'Cover art', exact: true })
        await expect(fit).toHaveValue('fit')
        for (const [value, expected] of [
          ['fill', 'cover'],
          ['fit', 'contain'],
        ]) {
          await fit.selectOption(value)
          await expect
            .poll(() =>
              page.evaluate(() =>
                getComputedStyle(document.documentElement).getPropertyValue('--cover-art-fit').trim(),
              ),
            )
            .toBe(expected)
        }
      }
      const slider = page.getByRole('slider', {
        name: mode === 'desktop' ? 'Rating cap' : 'Content age limit',
        exact: true,
      })
      const section = page.getByRole('region', { name: 'Rating cap preference', exact: true })
      await expect(slider).toHaveValue('5')
      await expect(slider).toHaveAttribute('min', '0')
      await expect(slider).toHaveAttribute('max', '5')
      await expect(slider).toHaveAttribute('step', '1')
      await expect(section.getByText(/Adults-only content is still hidden/)).toBeVisible()
      await slider.fill('2')
      await expect(slider).toBeEnabled()
      await expect(slider).toHaveAttribute('aria-valuetext', 'Teen')
      await expect(section.getByText('Hiding 1 title.', { exact: true })).toBeVisible()
      expect((await api<LibraryPreferences>(page, { route: 'preferences.library.get' })).maturityCap).toBe(
        'teen',
      )
      expect(
        (await api<LibraryResponse>(page, { route: 'library.get' })).games.map((game) => game.title),
      ).toEqual(['Unrated fixture'])
      if (mode === 'fullscreen') {
        await slider.focus()
        await page.evaluate(() => {
          const pad = { pressed: [] as number[] }
          Object.assign(window, { ratingPad: pad })
          Object.defineProperty(navigator, 'getGamepads', {
            configurable: true,
            value: () => [
              {
                index: 0,
                axes: [0, 0, 0, 0],
                buttons: Array.from({ length: 17 }, (_, i) => ({ pressed: pad.pressed.includes(i) })),
              },
            ],
          })
        })
        await page.waitForTimeout(200)
        for (const pressed of [[], [15], []])
          await page.evaluate(async (pressed) => {
            ;(window as any).ratingPad.pressed = pressed
            await new Promise<void>((done) =>
              requestAnimationFrame(() => requestAnimationFrame(() => done())),
            )
          }, pressed)
        await expect(slider).toHaveValue('3')
        await expect(slider).toHaveAttribute('aria-valuetext', 'Mature')
        await expect(slider).toBeEnabled()
        await expect(slider).toBeFocused()
        expect(
          await section
            .locator('p')
            .first()
            .evaluate((node) => parseFloat(getComputedStyle(node).fontSize)),
        ).toBeCloseTo(20 * scale)
      }
      await expect(slider).toBeEnabled()
      await slider.fill('5')
      await expect(section.getByText('No titles hidden.', { exact: true })).toBeVisible()
      await expect(slider).toBeEnabled()
      await section.scrollIntoViewIfNeeded()
      await page.screenshot({ path: info.outputPath(`${mode}-${scale}-rating-cap.png`) })
      if (mode === 'desktop') {
        const bounds = await page.getByRole('dialog', { name: 'Display preferences' }).boundingBox()
        expect(bounds!.width).toBeLessThanOrEqual(460)
        await page.getByRole('button', { name: 'Close display preferences' }).click()
        await expect(page.getByRole('button', { name: 'Display preferences', exact: true })).toBeFocused()
        await page.getByRole('button', { name: 'Settings', exact: true }).click()
        await page
          .getByRole('navigation', { name: 'Settings section' })
          .getByRole('button', { name: 'Library', exact: true })
          .click()
        await expect(page.getByRole('slider', { name: 'Rating cap', exact: true })).toHaveCount(0)
      }
      const explicit = page.getByRole('checkbox', { name: 'Show explicit content', exact: true })
      await expect(explicit).not.toBeChecked()
      await explicit.click()
      await expect(explicit).toBeChecked()
      await expect
        .poll(
          async () =>
            (await api<LibraryPreferences>(page, { route: 'preferences.library.get' })).showExplicitContent,
        )
        .toBe(true)
      await page.reload()
      await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
      await application.evaluate(({ BrowserWindow }, mode) => {
        BrowserWindow.getAllWindows()[0]!.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
      }, mode)
      await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
      await openControl()
      await expect(slider).toHaveValue('5')
      await expect(section.getByText(/Adults-only content is still hidden/)).toHaveCount(0)
      expect((await api<LibraryResponse>(page, { route: 'library.get' })).games).toHaveLength(3)
      expect(errors).toEqual([])
    } finally {
      await closeFixture(application, directory)
    }
  })
