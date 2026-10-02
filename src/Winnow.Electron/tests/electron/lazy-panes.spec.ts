import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import electronPath from 'electron'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { closeFixture } from './fixture-cleanup'
import { fillLibrarySearch } from './library-controls'
import { mergeFixture } from '../parity-merge-fixtures'
import type { MergeReview } from '../../src/renderer/features/parity-merge-model'
import type { LibraryResponse } from '../../src/renderer/api/types'

type FixtureState = { requests: string[]; detailWorkIds: number[]; review: MergeReview | null }
let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-lazy-panes-'))
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/lazy-panes-main.mjs'),
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
  await expect(
    page
      .getByRole('button', { name: 'Skip setup', exact: true })
      .or(page.getByRole('button', { name: 'Winnow home', exact: true })),
  ).toBeVisible()
  if (await page.getByRole('button', { name: 'Skip setup', exact: true }).isVisible())
    await page.getByRole('button', { name: 'Skip setup', exact: true }).click()
})
test.afterAll(async () => closeFixture(application, directory))
test.afterEach(async ({}, info) => {
  if (info.status !== info.expectedStatus)
    await info.attach('lazy-pane-failure', { body: await page.screenshot(), contentType: 'image/png' })
  expect(errors).toEqual([])
})
const navigate = (name: string) =>
  (name === 'Settings' ? page : page.getByRole('navigation', { name: 'Main navigation' }))
    .getByRole('button', { name, exact: true })
    .click()
const section = (name: string) =>
  page
    .getByRole('navigation', { name: 'Settings section' })
    .getByRole('button', { name, exact: true })
    .click()
async function state() {
  return application.evaluate(() => (globalThis as unknown as { __lazyPanes: FixtureState }).__lazyPanes)
}
async function noDetailsOrLightbox() {
  await expect(page.locator('.avalon-details')).toHaveCount(0)
  await expect(page.locator('.screenshot-dialog')).toHaveCount(0)
}

for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} mounts panes only on entry, stretches Merges, retains Appearance and opens bound Stardew details`, async ({}, info) => {
    await page.evaluate(async () => {
      await window.winnow.request({
        route: 'preferences.presentation.put',
        params: { preference: 'FullscreenReducedMotion' },
        body: { value: 'false' },
      })
    })
    await page.reload()
    await expect(page.locator('.avalon-shell')).toBeVisible()
    await application.evaluate(
      ({ BrowserWindow }, { mode, review }) => {
        const fixture = (globalThis as unknown as { __lazyPanes: FixtureState }).__lazyPanes
        fixture.requests = []
        fixture.detailWorkIds = []
        fixture.review = review
        const window = BrowserWindow.getAllWindows()[0]!
        window.setFullScreen(false)
        // The desktop size matches frozen MainWindow; fullscreen uses its 1920×1080 design reference.
        window.setContentSize(mode === 'desktop' ? 1280 : 1920, mode === 'desktop' ? 820 : 1080)
        window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
        window.focus()
      },
      { mode, review: mergeFixture() },
    )
    await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
    await expect(page.locator('.startup-presentation')).toHaveCount(0)
    await navigate('Library')
    await expect(page.locator('[data-avalon-game]').first()).toBeVisible()
    await noDetailsOrLightbox()
    for (const selector of ['.merge-queue', '.account-statistics', '.platform-settings', '.settings-page'])
      await expect(page.locator(selector)).toHaveCount(0)
    expect((await state()).detailWorkIds).toEqual([])
    expect((await state()).requests).not.toContain('/api/v1/identity/review/')

    if (mode === 'desktop') await navigate('Merges')
    else {
      await navigate('Settings')
      await section('Library')
      await page.getByRole('button', { name: 'Library tools', exact: true }).click()
      await page.getByRole('button', { name: 'Identity review', exact: true }).click()
    }
    const queue = page.locator('.merge-queue')
    await expect(queue).toHaveCount(1)
    await expect(queue.locator('.merge-card').first()).toBeVisible()
    const bounds = await queue.evaluate((element) => {
      const pane = element.closest<HTMLElement>('.feature-page')!
      const box = pane.getBoundingClientRect()
      return { width: box.width, height: box.height }
    })
    expect(bounds.width).toBeGreaterThan(900)
    expect(bounds.height).toBeGreaterThan(600)
    await expect(page.locator('.account-statistics')).toHaveCount(0)
    await noDetailsOrLightbox()
    await info.attach(`${mode}-merge-pane-bounds`, {
      body: Buffer.from(JSON.stringify(bounds)),
      contentType: 'application/json',
    })
    await info.attach(`${mode}-lazy-merge`, { body: await page.screenshot(), contentType: 'image/png' })

    if (mode === 'desktop') await navigate('Settings')
    await section('Appearance')
    for (const selector of ['.merge-queue', '.account-statistics', '.platform-settings', '.library-tools'])
      await expect(page.locator(selector)).toHaveCount(0)
    const motion = page.getByRole(mode === 'desktop' ? 'checkbox' : 'switch', {
      name: 'Reduce motion',
      exact: true,
    })
    await motion.click()
    await expect(motion).toBeChecked()
    await expect(motion).toBeEnabled()
    await navigate('Library')
    await expect(page.locator('.settings-page')).toHaveCount(0)
    await noDetailsOrLightbox()
    await navigate('Settings')
    await expect(
      page
        .getByRole('navigation', { name: 'Settings section' })
        .getByRole('button', { name: 'Appearance', exact: true }),
    ).toHaveAttribute('aria-pressed', 'true')
    await expect(motion).toBeChecked()
    await expect(page.locator('.settings-page')).toHaveCount(1)
    await info.attach(`${mode}-lazy-appearance`, { body: await page.screenshot(), contentType: 'image/png' })

    await navigate('Library')
    await fillLibrarySearch(page, 'Stardew Valley')
    const tile = page.getByRole('button', { name: /^View Stardew Valley/ })
    await expect(tile).toHaveCount(1)
    await expect(tile).toBeVisible()
    const workId = Number(await tile.getAttribute('data-work-id'))
    const snapshot = await page.evaluate(async () => {
      const response = await window.winnow.request({ route: 'library.get' })
      if (!response.ok) throw Error(response.message)
      return response.data as LibraryResponse
    })
    expect(snapshot.games.find((game) => game.workId === workId)?.title).toBe('Stardew Valley')
    await tile.focus()
    await noDetailsOrLightbox()
    await tile.press('Enter')
    await expect(page.locator('.avalon-details')).toHaveCount(1)
    await expect(
      page.locator('.avalon-details').getByRole('heading', { name: 'Stardew Valley', exact: true }),
    ).toBeVisible()
    await expect.poll(async () => (await state()).detailWorkIds).toEqual([workId])
    await page.getByRole('tab', { name: 'Journal', exact: true }).click()
    await expect(page.getByText(`Journal for selected work ${workId}.`, { exact: true })).toBeVisible()
    await info.attach(`${mode}-lazy-stardew-details`, {
      body: await page.screenshot(),
      contentType: 'image/png',
    })
    await page
      .getByRole('button', {
        name: mode === 'desktop' ? 'Close game details' : 'B · Back to Library',
        exact: true,
      })
      .click()
    await noDetailsOrLightbox()
    await expect(tile).toBeFocused()
  })
}
