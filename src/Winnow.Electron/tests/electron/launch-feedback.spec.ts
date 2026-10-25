import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-launch-feedback-'))
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/launch-feedback-main.mjs'),
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
  expect(await application.evaluate(() => (globalThis as any).launchFeedbackFixture.forbidden)).toEqual([])
})
async function surface(
  mode: 'desktop' | 'fullscreen',
  width = 1920,
  height = 1080,
  scale = 1,
  firstInstalled = true,
) {
  await application.evaluate(({}, firstInstalled) => {
    Object.assign((globalThis as any).launchFeedbackFixture, { actions: [], outcome: 0, firstInstalled })
  }, firstInstalled)
  await page.reload()
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
  await application.evaluate(
    ({ BrowserWindow }, { mode, width, height }) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setFullScreen(false)
      window.setContentSize(width, height)
      window.focus()
      window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
    },
    { mode, width, height },
  )
  await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
  await page.evaluate(async (scale) => {
    const result = await window.winnow.request({
      route: 'preferences.presentation.put',
      params: { preference: 'FullscreenTextScale' },
      body: { value: String(scale) },
    })
    if (!result.ok) throw Error(result.message)
  }, scale)
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
}
async function details() {
  await page.locator('.avalon-cover').first().click()
  const detail = page.locator('.avalon-details')
  await expect(detail).toBeVisible()
  const title = await detail.locator('h1').innerText()
  const copies = await application.evaluate(
    ({}, title) =>
      (globalThis as any).launchFeedbackFixture.copies.find((game: any) => game.title === title)
        .ids as number[],
    title,
  )
  return { detail, title, copies }
}
async function observed(id: number) {
  await application.evaluate(
    ({ BrowserWindow }, id) =>
      BrowserWindow.getAllWindows()[0]!.webContents.send('winnow:event', {
        kind: 'launch.observed',
        resource: String(id),
      }),
    id,
  )
}
async function records() {
  return application.evaluate(
    () =>
      (globalThis as any).launchFeedbackFixture.actions as {
        ownershipId: number
        action: string
        operationId: string
      }[],
  )
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

for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} Details focuses without dispatch, launches the selected copy and resolves only its watcher signal`, async () => {
    await surface(mode)
    const { detail, title, copies } = await details()
    const primary = detail
      .locator('.avalon-details-actions')
      .getByRole('button', { name: 'Play', exact: true })
    if (mode === 'fullscreen') await expect(primary).toBeFocused()
    expect(await records()).toEqual([])
    let selected = copies[0]
    if (mode === 'fullscreen') {
      await detail.getByRole('button', { name: 'Choose launch version' }).click()
      const panel = page.getByRole('dialog', { name: 'Choose launch version' })
      await expect(panel).toBeVisible()
      expect(await records()).toEqual([])
      await controller(13)
      await controller(0)
      selected = copies[1]
    } else {
      await detail.getByRole('tab', { name: 'Library', exact: true }).click()
      await detail.getByRole('button', { name: 'Play', exact: true }).last().click()
      selected = copies[1]
    }
    const strip = page.locator('.launch-feedback')
    await expect(strip).toHaveText(`Starting ${title}…`)
    expect(await records()).toEqual([
      { ownershipId: selected, action: 'Play', operationId: expect.any(String) },
    ])
    await observed(copies[0])
    await expect(strip).toHaveAttribute('data-waiting', 'true')
    await observed(selected)
    await expect(strip).toHaveText(`${title} is running.`)
    await expect(strip).toHaveAttribute('data-problem', 'false')
    await expect(strip).toBeHidden({ timeout: 5000 })
  })
  test(`${mode} primary launch refusal remains visible across a page change`, async () => {
    await surface(mode)
    const { detail, title } = await details()
    await application.evaluate(() => {
      ;(globalThis as any).launchFeedbackFixture.outcome = 2
    })
    await detail.locator('.avalon-details-actions').getByRole('button', { name: 'Play', exact: true }).click()
    const strip = page.locator('.launch-feedback')
    await expect(strip).toHaveText(`Couldn't reach Steam to start ${title}.`)
    await page
      .getByRole('button', {
        name: mode === 'desktop' ? 'Close game details' : 'B · Back to Library',
        exact: true,
      })
      .click()
    await expect(strip).toBeVisible()
    await expect(strip).toHaveAttribute('data-problem', 'true')
    await expect(strip).toHaveCSS('pointer-events', 'none')
    expect(await records()).toHaveLength(1)
  })
}

test('desktop Library tile uses the shared launch strip and survives switching to fullscreen', async () => {
  await surface('desktop', 1920, 1080, 1, false)
  const cover = page.locator('.avalon-cover').first()
  await cover.hover()
  await cover.locator('..').getByRole('button', { name: 'Play', exact: true }).click()
  await expect(page.locator('.launch-feedback')).toHaveAttribute('data-waiting', 'true')
  const [record] = await records()
  expect(record.action).toBe('Play')
  expect(record.ownershipId).toBeGreaterThan(1000000)
  await application.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0]!.webContents.send('winnow:fullscreen:changed', true),
  )
  await expect(page.locator('.launch-feedback.fullscreen')).toBeVisible()
  await observed(record.ownershipId)
  await expect(page.locator('.launch-feedback')).toHaveAttribute('data-waiting', 'false')
  expect(await records()).toHaveLength(1)
})

test('fullscreen controller triggers wrap local sections while opening Details never launches', async () => {
  await surface('fullscreen')
  const { detail } = await details()
  await expect(
    detail.locator('.avalon-details-actions').getByRole('button', { name: 'Play', exact: true }),
  ).toBeFocused()
  await expect(detail.getByRole('tab', { name: 'Overview', exact: true })).toHaveAttribute(
    'aria-selected',
    'true',
  )
  await controller(7)
  await expect(detail.getByRole('tab', { name: 'Updates', exact: true })).toHaveAttribute(
    'aria-selected',
    'true',
  )
  await controller(6)
  await expect(detail.getByRole('tab', { name: 'Overview', exact: true })).toHaveAttribute(
    'aria-selected',
    'true',
  )
  await controller(6)
  await expect(detail.getByRole('tab', { name: 'Library', exact: true })).toHaveAttribute(
    'aria-selected',
    'true',
  )
  expect(await records()).toEqual([])
  await application.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0]!.webContents.send('winnow:fullscreen:changed', false),
  )
  await expect(page.locator('.avalon-shell.desktop')).toBeVisible()
  if (!(await page.locator('.avalon-details').isVisible())) await details()
  await expect(
    page.locator('.avalon-details').getByRole('tab', { name: 'Overview', exact: true }),
  ).toHaveAttribute('aria-selected', 'true')
  expect(await records()).toEqual([])
})

for (const [width, height, scale] of [
  [1920, 1080, 1],
  [1920, 1080, 1.4],
  [1280, 720, 1.4],
]) {
  test(`fullscreen Details preserves readable summary type at ${width}×${height} and ${scale} text`, async ({}, info) => {
    await surface('fullscreen', width, height, scale)
    const { detail } = await details()
    const summary = detail.locator('.game-summary').first()
    // The original 1920×1080 fullscreen canvas scales its 24px text with the viewport.
    await expect
      .poll(() => summary.evaluate((node) => parseFloat(getComputedStyle(node).fontSize)))
      .toBeCloseTo(24 * scale * Math.min(width / 1920, 1), 1)
    const boxes = await detail.evaluate((node) => {
      const shell = node.getBoundingClientRect(),
        tabs = node.querySelector('[role="tablist"]')!.getBoundingClientRect(),
        actions = node.querySelector('.avalon-details-actions')!.getBoundingClientRect()
      return {
        width: window.innerWidth,
        height: window.innerHeight,
        shell: shell.toJSON(),
        tabs: tabs.toJSON(),
        actions: actions.toJSON(),
      }
    })
    expect(boxes.actions.bottom).toBeLessThanOrEqual(boxes.tabs.top)
    expect(boxes.tabs.bottom).toBeLessThan(boxes.height)
    expect(boxes.actions.right).toBeLessThanOrEqual(boxes.width)
    await page.screenshot({ path: info.outputPath('launch-details.png') })
  })
}
