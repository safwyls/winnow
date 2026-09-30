import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-details-contracts-'))
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/details-contracts-main.mjs'),
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
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
})
test.afterAll(async () => closeFixture(application, directory))
test.afterEach(async ({}, info) => {
  if (info.status !== info.expectedStatus) await page.screenshot({ path: info.outputPath('failure.png') })
  expect(errors).toEqual([])
})
async function surface(mode: 'desktop' | 'fullscreen', options = { longTitle: false, child: false }) {
  await application.evaluate(({}, options) => {
    Object.assign((globalThis as any).detailsContractsFixture, { ...options, revision: 'Before', writes: [] })
    Object.assign(globalThis, { __galleryCount: 2 })
  }, options)
  await page.reload()
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
  await application.evaluate(({ BrowserWindow }, mode) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setContentSize(1920, 1080)
    window.focus()
    window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
  }, mode)
  await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
}
async function details(child = false) {
  await (
    child
      ? page.getByRole('button', { name: 'View Expansion', exact: true })
      : page.locator('.avalon-cover').first()
  ).click()
  await expect(page.locator('.avalon-details')).toBeVisible()
  return page.locator('.avalon-details')
}
async function controller(button: number) {
  await page.evaluate(async (button) => {
    const state = { pressed: -1 }
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          connected: true,
          mapping: 'standard',
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, index) => ({
            pressed: index === state.pressed,
            value: index === state.pressed ? 1 : 0,
          })),
        },
      ],
    })
    const frames = () =>
      new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    await frames()
    state.pressed = button
    await frames()
    state.pressed = -1
    await frames()
  }, button)
}
async function publish(kind: string, resource: string) {
  await application.evaluate(
    ({ BrowserWindow }, event) => BrowserWindow.getAllWindows()[0]!.webContents.send('winnow:event', event),
    { kind, resource },
  )
}
for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} single-copy achievements remain visible without an identity relationship`, async () => {
    await surface(mode)
    const detail = await details()
    await detail.getByRole('tab', { name: 'Library', exact: true }).click()
    await expect(detail.locator('.avalon-copy')).toHaveCount(1)
    await expect(detail.getByText('Steam: 5 of 20 unlocked · 25%', { exact: true })).toBeVisible()
    await expect(detail.getByRole('heading', { name: 'Related games & editions' })).toHaveCount(0)
  })
  test(`${mode} reload preserves the local Journal section and an open unsaved editor`, async () => {
    await surface(mode)
    const detail = await details()
    await detail.getByRole('tab', { name: 'Journal', exact: true }).click()
    await detail.getByRole('button', { name: 'Edit note', exact: true }).click()
    const editor = page.getByRole('dialog', { name: 'Remember this session', exact: true })
    await editor.getByRole('textbox').fill('Open draft survives the replacement')
    await application.evaluate(() => {
      ;(globalThis as any).detailsContractsFixture.revision = 'After'
    })
    await publish('library.changed', 'library')
    await expect(detail.locator('h1')).toContainText('After')
    await expect(editor.getByRole('textbox')).toHaveValue('Open draft survives the replacement')
    await editor.getByRole('button', { name: 'Close journal editor', exact: true }).click()
    await expect(detail.getByRole('tab', { name: 'Journal', exact: true })).toHaveAttribute(
      'aria-selected',
      'true',
    )
    await page
      .getByRole('button', {
        name: mode === 'desktop' ? 'Close game details' : 'B · Back to Library',
        exact: true,
      })
      .click()
    await expect(detail).toHaveCount(0)
    expect(await application.evaluate(() => (globalThis as any).detailsContractsFixture.writes)).toEqual([])
  })
  test(`${mode} expansion-end separation keeps the safe choice and writes the child identity only after confirmation`, async () => {
    await surface(mode, { longTitle: false, child: true })
    const detail = await details(true)
    await detail.getByRole('tab', { name: 'Library', exact: true }).click()
    await detail.getByRole('button', { name: 'Separate Expansion…', exact: true }).click()
    const confirmation = page.getByRole('dialog', { name: 'Separate Expansion from Base game?', exact: true })
    await expect(confirmation.getByRole('button', { name: 'Keep relationship' })).toBeFocused()
    expect(await application.evaluate(() => (globalThis as any).detailsContractsFixture.writes)).toEqual([])
    if (mode === 'fullscreen') {
      await controller(13)
      await controller(0)
    } else await confirmation.getByRole('button', { name: 'Separate games', exact: true }).click()
    await expect(confirmation).toBeHidden()
    const writes = await application.evaluate(() => (globalThis as any).detailsContractsFixture.writes)
    expect(writes).toEqual([{ path: '/api/v1/identity/links/99', body: { expectedLinkId: 271 } }])
  })
}
test('fullscreen history initially focuses Lifetime and controller Right selects the original observed session', async () => {
  await surface('fullscreen')
  const detail = await details()
  await detail.getByRole('button', { name: 'Play history →', exact: true }).click()
  await expect(detail.getByRole('button', { name: 'Lifetime', exact: true })).toBeFocused()
  await controller(15)
  await controller(0)
  await expect(detail.getByRole('button', { name: 'Tracked sessions', exact: true })).toHaveAttribute(
    'aria-pressed',
    'true',
  )
  await expect(detail.locator('.activity-timeline-bar.tracked')).toHaveCount(1)
  await expect(detail.getByText('Only sessions observed by Winnow are shown.', { exact: true })).toBeVisible()
})
test('fullscreen ten-repeat long title keeps every hero action above section navigation', async ({}, info) => {
  await surface('fullscreen', { longTitle: true, child: false })
  const detail = await details()
  const bounds = await detail.evaluate((node) => {
    const title = node.querySelector('h1')!,
      hero = node.querySelector('.avalon-details-header')!.getBoundingClientRect()
    return {
      title: title.getBoundingClientRect().toJSON(),
      lineHeight: parseFloat(getComputedStyle(title).lineHeight),
      hero: hero.toJSON(),
      tabs: node.querySelector('[role="tablist"]')!.getBoundingClientRect().toJSON(),
      actions: [...node.querySelectorAll('.avalon-details-header button')].map((button) =>
        button.getBoundingClientRect().toJSON(),
      ),
    }
  })
  expect(bounds.title.height).toBeGreaterThan(bounds.lineHeight)
  expect(bounds.hero.bottom).toBeLessThanOrEqual(bounds.tabs.top)
  for (const action of bounds.actions) {
    expect(action.top).toBeGreaterThanOrEqual(bounds.hero.top)
    expect(action.bottom).toBeLessThanOrEqual(bounds.hero.bottom + 1)
  }
  await page.screenshot({ path: info.outputPath('long-title.png') })
})
test('a pending desktop journal prompt appears on fullscreen attachment and Back dismisses the shared prompt', async () => {
  await surface('desktop')
  await publish('session.ended', 'sessions/888')
  await expect(page.locator('.session-notifications.mode-desktop .session-prompt')).toBeVisible()
  await application.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0]!.webContents.send('winnow:fullscreen:changed', true),
  )
  await expect(page.locator('.session-notifications.mode-fullscreen .session-prompt')).toBeVisible()
  await controller(1)
  await expect(page.locator('.session-prompt')).toHaveCount(0)
  await application.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0]!.webContents.send('winnow:fullscreen:changed', false),
  )
  await expect(page.locator('.avalon-shell.desktop')).toBeVisible()
  await expect(page.locator('.session-prompt')).toHaveCount(0)
  expect(await application.evaluate(() => (globalThis as any).detailsContractsFixture.writes)).toEqual([])
})
