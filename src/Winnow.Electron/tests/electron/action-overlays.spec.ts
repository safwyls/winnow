import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

let application: ElectronApplication, page: Page, directory: string
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-action-overlays-'))
  const env = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/action-overlays-main.mjs'),
      '--data-dir',
      directory,
      '--seed-sample',
      '--no-sync',
    ],
    env,
    chromiumSandbox: true,
    timeout: 60000,
  })
  page = await application.firstWindow()
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
})
test.afterAll(async () => closeFixture(application, directory))

async function surface(text = 1, reduced = true, scale = 1, safe = 5) {
  await application.evaluate(({ BrowserWindow }) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setContentSize(1920, 1080)
    window.webContents.send('winnow:fullscreen:changed', true)
    window.focus()
  })
  await expect(page.locator('.avalon-shell')).toHaveClass(/fullscreen/)
  for (const [preference, value] of [
    ['FullscreenTextScale', String(text)],
    ['FullscreenReducedMotion', String(reduced)],
    ['FullscreenInterfaceScale', String(scale)],
    ['FullscreenSafeMargin', String(safe)],
  ])
    await page.evaluate(
      async ([preference, value]) => {
        const response = await window.winnow.request({
          route: 'preferences.presentation.put',
          params: { preference },
          body: { value },
        })
        if (!response.ok) throw Error(response.message)
      },
      [preference!, value!],
    )
  await expect
    .poll(() =>
      page.evaluate(() => document.documentElement.style.getPropertyValue('--fullscreen-text-scale')),
    )
    .toBe(String(text))
  await expect
    .poll(() =>
      page.evaluate(() => document.documentElement.style.getPropertyValue('--fullscreen-interface-scale')),
    )
    .toBe(String(scale))
  await expect
    .poll(() =>
      page.evaluate(() => document.documentElement.style.getPropertyValue('--fullscreen-safe-ratio')),
    )
    .toBe(String(safe / 100))
  await expect
    .poll(() => page.evaluate(() => document.documentElement.classList.contains('reduced-motion')))
    .toBe(reduced)
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
  await page.locator('.avalon-cover').first().click()
  await expect(page.locator('.avalon-details.fullscreen')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Read more →', exact: true })).toBeVisible()
}
async function pad(buttons: number[]) {
  await page.evaluate(async (buttons) => {
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          connected: true,
          mapping: 'standard',
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, index) => ({
            pressed: buttons.includes(index),
            touched: false,
            value: buttons.includes(index) ? 1 : 0,
          })),
        },
      ],
    })
    await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
  }, buttons)
}
async function tap(buttons: number[]) {
  await pad([])
  await pad(buttons)
  await pad([])
}

test('fullscreen More retains Details, traps Tab and Play/Search, and restores its trigger for every dismissal input', async ({}, info) => {
  await surface()
  const details = page.locator('.avalon-details.fullscreen')
  await expect(details.locator('[data-controller-play]')).toBeEnabled()
  await details.evaluate((element) => {
    ;(window as unknown as { retainedDetails: Element }).retainedDetails = element
  })
  const more = page.locator('.avalon-details-more > button')
  for (const input of ['keyboard', 'controller', 'right-click', 'veil']) {
    await more.click()
    const panel = page.getByRole('dialog', { name: 'More game actions', exact: true })
    await expect(panel).toBeVisible()
    expect(await details.evaluate((element) => element.closest('[inert]') !== null)).toBe(true)
    expect(
      await details.evaluate(
        (element) => element === (window as unknown as { retainedDetails: Element }).retainedDetails,
      ),
    ).toBe(true)
    for (let index = 0; index < 6; index++) {
      await page.keyboard.press('Tab')
      expect(await panel.evaluate((element) => element.contains(document.activeElement))).toBe(true)
    }
    await tap([2, 8])
    await expect(panel).toBeVisible()
    expect(
      await application.evaluate(
        () => (globalThis as unknown as { overlayLaunches: number }).overlayLaunches,
      ),
    ).toBe(0)
    expect(await page.locator('.avalon-search').count()).toBe(0)
    if (input === 'keyboard') await page.keyboard.press('Escape')
    else if (input === 'controller') await tap([1])
    else if (input === 'right-click') await panel.getByRole('button').first().click({ button: 'right' })
    else await page.mouse.click(40, 540)
    await expect(panel).toHaveCount(0)
    await expect(more).toBeFocused()
    expect(await details.evaluate((element) => element.closest('[inert]') !== null)).toBe(false)
  }
  await more.click()
  await page.getByRole('button', { name: 'Hide game…', exact: true }).click()
  const confirmation = page.getByRole('dialog', { name: /^Hide / })
  await expect(page.getByRole('dialog')).toHaveCount(1)
  await expect(confirmation.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused()
  expect(
    await details.evaluate(
      (element) =>
        element === (window as unknown as { retainedDetails: Element }).retainedDetails &&
        element.closest('[inert]') !== null,
    ),
  ).toBe(true)
  await tap([0])
  await expect(confirmation).toHaveCount(0)
  await expect(more).toBeFocused()
  await tap([2])
  await expect
    .poll(() =>
      application.evaluate(() => (globalThis as unknown as { overlayLaunches: number }).overlayLaunches),
    )
    .toBe(1)
  await page.screenshot({ path: info.outputPath('details-after-action-dismissals.png') })
  await page.getByRole('button', { name: 'B · Back to Library', exact: true }).click()
})

test('fullscreen action opens a child editor once and right-click respects the reading-page Back handler', async () => {
  await surface()
  const more = page.locator('.avalon-details-more > button')
  await more.click()
  await page
    .getByRole('dialog', { name: 'More game actions', exact: true })
    .getByRole('button', { name: 'Edit details', exact: true })
    .click()
  await expect(page.locator('.avalon-actions-panel')).toHaveCount(0)
  await expect(page.getByRole('dialog')).toHaveCount(1)
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(more).toBeFocused()
  const read = page.getByRole('button', { name: 'Read more →', exact: true })
  await read.click()
  await expect(page.getByRole('button', { name: 'Back to Overview', exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Back to Overview', exact: true }).click({ button: 'right' })
  await expect(read).toBeFocused()
  await expect(page.locator('.avalon-details.fullscreen')).toBeVisible()
  await read.click({ button: 'right' })
  await expect(page.locator('.avalon-details')).toHaveCount(0)
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog', { name: 'Quick menu', exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Resume', exact: true }).click()
})

for (const [text, reduced, scale] of [
  [1, true, 1],
  [1.4, true, 1],
  [1, false, 1],
  [1.4, true, 1.2],
] as const)
  test(`fullscreen action panel preserves edge alignment, safe margins and type at text ${text}, interface ${scale}, reduced ${reduced}`, async ({}, info) => {
    await surface(text, reduced, scale, 10)
    await page.locator('.avalon-details-more > button').click()
    const panel = page.getByRole('dialog', { name: 'More game actions', exact: true })
    await expect(panel).toBeVisible()
    const animations = await panel.evaluate((element) =>
      element.getAnimations().map((animation) => animation.effect?.getTiming().duration),
    )
    if (!reduced) {
      expect(animations).toContain(180)
      expect(
        await panel.evaluate((element) => {
          for (const animation of element.getAnimations()) {
            animation.pause()
            animation.currentTime = 0
          }
          return new DOMMatrix(getComputedStyle(element).transform).m41
        }),
      ).toBeGreaterThan(0)
    } else expect(animations).toEqual([])
    await panel.evaluate(async (element) => {
      await Promise.all(
        element.getAnimations().map((animation) => {
          animation.play()
          return animation.finished
        }),
      )
    })
    expect(
      await panel.evaluate((element) => Math.abs(new DOMMatrix(getComputedStyle(element).transform).m41)),
    ).toBeLessThanOrEqual(0.1)
    const bounds = (await panel.boundingBox())!
    expect(Math.abs(bounds.x + bounds.width - 1920)).toBeLessThanOrEqual(2)
    expect(bounds.y).toBe(0)
    expect(Math.abs(bounds.height - 1080)).toBeLessThanOrEqual(2)
    expect(bounds.width).toBeLessThan(1920 * 0.6)
    if (scale === 1) {
      expect(bounds.width).toBeGreaterThanOrEqual(500)
      expect(bounds.width).toBeLessThanOrEqual(900)
    }
    const title = (await panel.getByRole('heading').boundingBox())!
    expect(
      await panel.getByRole('heading').evaluate((element) => parseFloat(getComputedStyle(element).fontSize)),
    ).toBeGreaterThanOrEqual(40 * text - 0.1)
    await expect(panel.getByRole('button', { name: 'Close', exact: true })).toHaveCount(0)
    expect(title.y).toBeGreaterThanOrEqual(107)
    expect(title.x + title.width).toBeLessThanOrEqual(1729)
    const footer = (await panel.locator('footer').boundingBox())!
    expect(footer.y + footer.height).toBeLessThanOrEqual(973)
    for (const button of await panel.getByRole('button').all()) {
      expect(
        await button.evaluate((element) => parseFloat(getComputedStyle(element).fontSize)),
      ).toBeGreaterThanOrEqual(28 * text - 0.1)
      expect((await button.boundingBox())!.width).toBeLessThanOrEqual(bounds.width)
    }
    await page.screenshot({ path: info.outputPath('action-panel.png') })
    await page.keyboard.press('Escape')
    await page.getByRole('button', { name: 'B · Back to Library', exact: true }).click()
  })
