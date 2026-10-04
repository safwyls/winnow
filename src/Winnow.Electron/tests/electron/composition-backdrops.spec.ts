import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import type { ThemeProfile } from '../../src/shared/theme'

let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
type Fixture = {
  kind: string
  delay: boolean
  pending: (() => void)[]
  requests: { id: string; width: number }[]
}
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-composition-backdrops-'))
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/backdrop-main.mjs'),
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
    timeout: 60000,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
})
test.afterAll(async () => closeFixture(application, directory))
async function choose(kind: string, delay = false) {
  await application.evaluate(
    ({ BrowserWindow }, { kind, delay }) => {
      const fixture = (globalThis as unknown as { __backdropFixture: Fixture }).__backdropFixture
      fixture.kind = kind
      fixture.delay = delay
      BrowserWindow.getAllWindows()[0]!.webContents.send('winnow:event', {
        kind: 'preferences.changed',
        resource: 'ArtworkSourceOrder',
        epoch: 'composition-backdrop',
        sequence: Date.now(),
      })
    },
    { kind, delay },
  )
}
for (const theme of ['avalon', 'afterglow', 'rift', 'catalogue'])
  for (const mode of ['desktop', 'fullscreen'] as const)
    test(
      theme + ' ' + mode + ': backgrounds share fallback, live updates and missing-art behavior',
      async ({}, info) => {
        await choose('fallback')
        await page.evaluate(async (theme) => {
          const profile = (await window.winnow.loadPreferences()) as ThemeProfile
          profile.themeId = theme
          profile.appearance.reducedMotion = true
          await window.winnow.savePreferences(profile)
        }, theme)
        await page.reload()
        await expect(page.locator('.' + theme + '-shell')).toBeVisible()
        await application.evaluate(({ BrowserWindow }, mode) => {
          const window = BrowserWindow.getAllWindows()[0]!
          window.setFullScreen(false)
          window.setContentSize(1440, 1000)
          window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
        }, mode)
        await expect(page.locator('.' + theme + '-shell')).toHaveClass(new RegExp(mode))
        await page
          .getByRole('navigation', { name: 'Main navigation' })
          .getByRole('button', { name: 'Library', exact: true })
          .click()
        await page.locator('.avalon-cover, .game-card, [data-rift-game]').first().click()
        const backdrop = page.locator(theme === 'avalon' ? '.avalon-detail-backdrop' : '.detail-hero')
        await expect(backdrop.locator('[data-key="igdb-backdrop:landscape"] img')).toBeVisible()
        await expect(backdrop).toHaveAttribute('data-state', 'ready')
        expect(
          await application.evaluate(() =>
            (globalThis as unknown as { __backdropFixture: Fixture }).__backdropFixture.requests.some(
              (request) => request.id === 'missing-highres',
            ),
          ),
        ).toBe(true)
        await expect(backdrop.locator('.artwork-placeholder')).toHaveCount(0)
        await page.screenshot({ path: info.outputPath(theme + '-' + mode + '-automatic.png') })
        await choose('hero', true)
        await expect
          .poll(() =>
            application.evaluate(
              () =>
                (globalThis as unknown as { __backdropFixture: Fixture }).__backdropFixture.pending.length,
            ),
          )
          .toBeGreaterThan(0)
        await expect(backdrop.locator('[data-key="igdb-backdrop:landscape"] img')).toBeVisible()
        await application.evaluate(() => {
          const fixture = (globalThis as unknown as { __backdropFixture: Fixture }).__backdropFixture
          fixture.delay = false
          fixture.pending.splice(0).forEach((finish) => finish())
        })
        await expect(backdrop.locator('[data-key="steam-hero:hero"] img')).toBeVisible()
        await choose('cover-only')
        await expect(backdrop.locator('[data-fallback] img')).toBeVisible()
        await choose('missing')
        await expect(backdrop).toHaveAttribute('data-state', 'missing')
        await expect(backdrop.locator('img')).toHaveCount(0)
        await expect(backdrop.locator('[data-placeholder-scene="' + theme + '"]')).toBeVisible()
        await page.screenshot({ path: info.outputPath(theme + '-' + mode + '-missing.png') })
        expect(errors).toEqual([])
      },
    )
