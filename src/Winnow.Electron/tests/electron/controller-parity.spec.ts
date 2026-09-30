import { closeFixture } from './fixture-cleanup'
import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
type Pad = { pressed: number[]; axes: number[] }
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-controller-parity-'))
  const env = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('tests/electron/details-main.mjs'), '--data-dir', directory, '--seed-sample', '--no-sync'],
    env,
    chromiumSandbox: true,
    timeout: 60000,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
})
test.afterAll(async () => closeFixture(application, directory))
test.afterEach(async ({}, info) => {
  if (info.status !== info.expectedStatus)
    await info.attach('controller-failure', { body: await page.screenshot(), contentType: 'image/png' })
  await page.evaluate(() => {
    document.querySelector('[data-controller-fixture]')?.remove()
  })
  expect(errors).toEqual([])
})
async function sample(pressed: number[] = [], axes = [0, 0, 0, 0]) {
  await page.evaluate(
    async (value) => {
      Object.assign((window as unknown as { parityPad: Pad }).parityPad, value)
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    },
    { pressed, axes },
  )
}
async function tap(button: number) {
  await sample()
  await sample([button])
  await sample()
}
async function surface(mode: 'desktop' | 'fullscreen', count = 2, scale = 1) {
  await application.evaluate((_, count) => {
    Object.assign(globalThis, { __galleryCount: count })
  }, count)
  await page.reload()
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
  await application.evaluate(({ BrowserWindow }, mode) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setContentSize(1920, 1080)
    window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
  }, mode)
  await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
  await page.evaluate(async (scale) => {
    const result = await window.winnow.request({
      route: 'preferences.presentation.put',
      params: { preference: 'FullscreenTextScale' },
      body: { value: String(scale) },
    })
    if (!result.ok) throw Error(result.message)
    const state: Pad = { pressed: [], axes: [0, 0, 0, 0] }
    Object.assign(window, { parityPad: state })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          connected: true,
          mapping: 'standard',
          axes: state.axes,
          buttons: Array.from({ length: 17 }, (_, index) => ({
            pressed: state.pressed.includes(index),
            value: state.pressed.includes(index) ? 1 : 0,
          })),
        },
      ],
    })
  }, scale)
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
  await sample()
}
test('desktop controller opens the keyboard for Weekend, restores the field and opens fullscreen and its quick menu', async () => {
  await surface('desktop')
  const search = page.locator('[data-library-search]')
  await search.fill('Weekend')
  await tap(0)
  await expect(page.getByRole('dialog', { name: 'Enter text' })).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog', { name: 'Enter text' })).toHaveCount(0)
  await expect(search).toHaveValue('Weekend')
  await expect(search).toBeFocused()
  await tap(9)
  await expect(page.locator('.avalon-shell')).toHaveClass(/fullscreen/)
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  await tap(9)
  await expect(page.getByRole('dialog', { name: 'Quick menu' })).toBeVisible()
  await page.keyboard.press('F11')
  await expect(page.locator('.avalon-shell')).toHaveClass(/desktop/)
})
test('desktop controller stays in flyouts, toggles checkboxes and skips disabled controls on shoulder navigation', async () => {
  await surface('desktop')
  await page.evaluate(() => {
    const panel = document.createElement('div')
    panel.dataset.controllerFixture = ''
    panel.setAttribute('role', 'menu')
    panel.style.cssText =
      'position:fixed;inset:100px auto auto 100px;z-index:10000;padding:30px;background:#222;display:grid;gap:40px'
    panel.innerHTML =
      '<input type="checkbox" aria-label="First fixture choice"><input type="checkbox" aria-label="Second fixture choice"><button disabled>Unavailable fixture action</button><button>Final fixture action</button>'
    panel.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') {
        event.preventDefault()
        event.stopPropagation()
        panel.remove()
      }
    })
    document.body.append(panel)
    panel.querySelector<HTMLInputElement>('input')!.focus()
  })
  await tap(13)
  await expect(page.getByRole('checkbox', { name: 'Second fixture choice' })).toBeFocused()
  await tap(0)
  await expect(page.getByRole('checkbox', { name: 'Second fixture choice' })).toBeChecked()
  await tap(5)
  await expect(page.getByRole('button', { name: 'Final fixture action' })).toBeFocused()
  await page.getByRole('checkbox', { name: 'Second fixture choice' }).focus()
  await tap(13)
  await expect(page.getByRole('button', { name: 'Final fixture action' })).toBeFocused()
  for (const button of [5, 4])
    for (let index = 0; index < 15; index++) {
      await tap(button)
      expect(await page.evaluate(() => !!document.activeElement?.closest('[data-controller-fixture]'))).toBe(
        true,
      )
    }
  await tap(1)
  await expect(page.locator('[data-controller-fixture]')).toHaveCount(0)
})
test('desktop right stick scrolls the focused 300px viewport by 75px and preserves focus', async () => {
  await surface('desktop')
  await page.evaluate(() => {
    const panel = document.createElement('div')
    panel.dataset.controllerFixture = ''
    panel.setAttribute('role', 'dialog')
    panel.style.cssText =
      'position:fixed;inset:100px auto auto 100px;z-index:10000;width:500px;height:300px;overflow-y:auto;background:#222;border:0;padding:0'
    panel.innerHTML = '<div style="height:2000px"><button>Scroll fixture anchor</button></div>'
    document.body.append(panel)
    panel.querySelector('button')!.focus()
  })
  await sample([], [0, 0, 0, 1])
  await sample()
  await expect
    .poll(() => page.locator('[data-controller-fixture]').evaluate((node) => node.scrollTop))
    .toBe(75)
  await expect(page.getByRole('button', { name: 'Scroll fixture anchor' })).toBeFocused()
  await sample([], [0, 0, 0, -1])
  await sample()
  await expect
    .poll(() => page.locator('[data-controller-fixture]').evaluate((node) => node.scrollTop))
    .toBe(0)
})
for (const count of [0, 1, 2])
  for (const scale of [1, 1.4])
    test(`fullscreen Details preserves action, tab and media focus rows with ${count} screenshots at text ${scale}`, async ({}, info) => {
      await surface('fullscreen', count, scale)
      await page.locator('.avalon-cover').first().click()
      const details = page.locator('.avalon-details.fullscreen'),
        primary = details.locator('[data-controller-play], .avalon-details-more > button').first()
      await expect(primary).toBeFocused()
      const overview = details.getByRole('tab', { name: 'Overview', exact: true })
      const history = details.locator('[data-details-reading="History"]'),
        about = details.locator('[data-details-reading="About"]')
      const journal = details.locator('.avalon-latest-note button'),
        shots = details.locator('.screenshot-strip button')
      await expect(shots).toHaveCount(count)
      await tap(13)
      await expect(
        overview,
        await page.evaluate(() =>
          JSON.stringify({
            active: document.activeElement?.outerHTML,
            actions: [...document.querySelectorAll('.avalon-details-actions button')].map(
              (node) => node.outerHTML,
            ),
          }),
        ),
      ).toBeFocused()
      await tap(15)
      await expect(details.getByRole('tab').nth(1)).toBeFocused()
      await expect(overview).toHaveAttribute('aria-selected', 'true')
      await expect(details.getByRole('tab').nth(1)).toHaveAttribute('aria-selected', 'false')
      await expect(details.getByRole('tab').nth(1)).toHaveCSS('border-bottom-color', 'rgba(0, 0, 0, 0)')
      await expect(details.getByRole('tab').nth(1)).toHaveCSS('box-shadow', 'none')
      await expect(details.getByRole('tab').nth(1)).toHaveCSS('outline-width', '0px')
      const raised = await page.evaluate(() => {
        const probe = document.createElement('span')
        probe.style.background = 'var(--raised)'
        document.body.append(probe)
        const color = getComputedStyle(probe).backgroundColor
        probe.remove()
        return color
      })
      await expect(details.getByRole('tab').nth(1)).toHaveCSS('background-color', raised)
      const underlineWidth = await overview.evaluate((node) => {
        // Chromium snaps borders to device pixels after applying the interface zoom.
        const probe = document.createElement('span')
        probe.style.borderBottom = '2px solid'
        node.parentElement!.append(probe)
        const width = getComputedStyle(probe).borderBottomWidth
        probe.remove()
        return width
      })
      await expect(overview).toHaveCSS('border-bottom-width', underlineWidth)
      await expect(overview).not.toHaveCSS('border-bottom-color', 'rgba(0, 0, 0, 0)')
      if (count === 2 && scale === 1.4)
        await page.screenshot({ path: info.outputPath('fullscreen-controller-details.png') })
      await tap(14)
      await tap(13)
      await expect(history).toBeFocused()
      await tap(15)
      await expect(about).toBeFocused()
      await tap(13)
      if (count) {
        await expect(shots.first()).toBeFocused()
        for (let index = 1; index < count; index++) {
          await tap(15)
          await expect(shots.nth(index)).toBeFocused()
        }
        await tap(15)
        await expect(shots.last()).toBeFocused()
        await tap(13)
        await expect(details.locator('.screenshot-gallery-link')).toBeFocused()
        await tap(12)
        expect(
          await page.evaluate(
            () => !!document.activeElement?.closest('.avalon-latest-note,.screenshot-strip'),
          ),
        ).toBe(true)
        await tap(12)
        expect(await page.evaluate(() => document.activeElement?.hasAttribute('data-details-reading'))).toBe(
          true,
        )
      } else await expect(journal).toBeFocused()
      await history.focus()
      await tap(12)
      await expect(overview).toBeFocused()
      await tap(12)
      await expect(primary).toBeFocused()
      await tap(7)
      await expect(details.getByRole('tab').nth(1)).toBeFocused()
      await expect(details.getByRole('tab').nth(1)).toHaveAttribute('aria-selected', 'true')
      await tap(6)
      await expect(overview).toHaveAttribute('aria-selected', 'true')
      await tap(6)
      await expect(details.getByRole('tab', { name: 'Library', exact: true })).toHaveAttribute(
        'aria-selected',
        'true',
      )
    })
test('fullscreen About scrolls from Back immediately by 160px and restores the Overview origin', async () => {
  await surface('fullscreen')
  await page.locator('.avalon-cover').first().click()
  const details = page.locator('.avalon-details.fullscreen'),
    about = details.locator('[data-details-reading="About"]')
  await about.click()
  const region = details.getByRole('region', { name: 'About', exact: true }),
    back = details.getByRole('button', { name: 'Back to Overview' })
  await expect(back).toBeFocused()
  await tap(13)
  await expect.poll(() => region.evaluate((node) => node.scrollTop)).toBe(160)
  await expect(back).toBeFocused()
  await tap(12)
  await expect.poll(() => region.evaluate((node) => node.scrollTop)).toBe(0)
  await sample([], [0, 0, 0, 1])
  await sample()
  await expect.poll(() => region.evaluate((node) => node.scrollTop)).toBe(160)
  await tap(1)
  await expect(about).toBeFocused()
  await tap(3)
  await page.getByRole('button', { name: 'Add to list', exact: true }).click()
  await expect(page.getByRole('dialog', { name: 'More game actions' })).toHaveCount(0)
  const prompt = page.getByRole('dialog', { name: /^Add .* to a list$/ })
  await expect(prompt.getByRole('textbox', { name: 'New list name' })).toBeFocused()
  for (const button of [5, 4])
    for (let index = 0; index < 15; index++) {
      await tap(button)
      expect(await prompt.evaluate((node) => node.contains(document.activeElement))).toBe(true)
    }
  await tap(1)
  await expect(prompt).toHaveCount(0)
  await expect(details.getByRole('button', { name: 'More', exact: true })).toBeFocused()
})
