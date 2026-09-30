import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Page,
  type Locator,
} from '@playwright/test'
import electronPath from 'electron'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { closeFixture } from './fixture-cleanup'
import { fillLibrarySearch, expectLibrarySearch, setLibrarySort, expectLibrarySort } from './library-controls'

let app: ElectronApplication, page: Page, directory: string
const errors: string[] = []
test.beforeEach(async ({}, info) => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-fullscreen-lifecycle-'))
  errors.length = 0
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/fullscreen-lifecycle-main.mjs'),
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
      WINNOW_LIFECYCLE_HOLD: info.title.includes('before library context') ? '1' : '0',
    },
    chromiumSandbox: true,
  })
  page = await app.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  if (!info.title.includes('before library context')) await ready()
})
test.afterEach(async ({}, info) => {
  if (info.status !== info.expectedStatus)
    await info.attach('lifecycle-failure', { body: await page.screenshot(), contentType: 'image/png' })
  await app.evaluate(() => (globalThis as any).__fullscreenLifecycle.release())
  await closeFixture(app, directory)
  expect(errors).toEqual([])
})
async function ready() {
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  await expect(page.locator('.avalon-shell')).toBeVisible()
}
async function preference(preference: string, value: string) {
  await page.evaluate(
    async ({ preference, value }) => {
      const result = await window.winnow.request({
        route: 'preferences.presentation.put',
        params: { preference },
        body: { value },
      })
      if (!result.ok) throw Error(result.message)
    },
    { preference, value },
  )
}
async function fullscreen(width = 1920, height = 1080, scale = 1) {
  await app.evaluate(
    ({ BrowserWindow }, size) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setContentSize(size.width, size.height)
      window.webContents.send('winnow:fullscreen:changed', true)
      window.focus()
    },
    { width, height },
  )
  await ready()
  await expect(page.locator('.avalon-shell')).toHaveClass(/fullscreen/)
  await preference('FullscreenTextScale', String(scale))
  await expect
    .poll(() =>
      page.evaluate(() => document.documentElement.style.getPropertyValue('--fullscreen-text-scale')),
    )
    .toBe(String(scale))
  await page.evaluate(() => {
    Object.assign(window, { lifecyclePad: [] })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          connected: true,
          mapping: 'standard',
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, i) => ({
            pressed: (window as any).lifecyclePad.includes(i),
            value: (window as any).lifecyclePad.includes(i) ? 1 : 0,
          })),
        },
      ],
    })
  })
  await sample([])
}
async function sample(pressed: number[]) {
  await page.evaluate(async (pressed) => {
    ;(window as any).lifecyclePad = pressed
    await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
  }, pressed)
}
async function tap(button: number) {
  await sample([])
  await sample([button])
  await sample([])
}
async function root(name: string) {
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name, exact: true })
    .click()
}
async function fits(locator: Locator) {
  await expect(locator).toBeVisible()
  const result = await locator.evaluate((node) => {
    const r = node.getBoundingClientRect()
    return r.left >= -1 && r.top >= -1 && r.right <= innerWidth + 1 && r.bottom <= innerHeight + 1
  })
  expect(result).toBe(true)
}

test('entry button and F11 toggle before library context is available', async () => {
  await expect(page.getByRole('dialog', { name: 'Preparing your library' })).toBeVisible()
  await expect(page.locator('.avalon-cover')).toHaveCount(0)
  await page.getByRole('button', { name: 'Enter fullscreen', exact: true }).click()
  await expect(page.getByRole('dialog', { name: 'Preparing fullscreen' })).toBeVisible()
  await expect
    .poll(() => app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0]!.isFullScreen()))
    .toBe(true)
  await page.keyboard.press('F11')
  await expect(page.getByRole('dialog', { name: 'Preparing your library' })).toBeVisible()
  await page.keyboard.press('F11')
  await expect(page.getByRole('dialog', { name: 'Preparing fullscreen' })).toBeVisible()
  await app.evaluate(() => (globalThis as any).__fullscreenLifecycle.release())
  await ready()
})

for (const maximized of [false, true])
  test(`restores native window state maximized ${maximized} and desktop visibility`, async () => {
    await app.evaluate(({ BrowserWindow }, maximized) => {
      const window = BrowserWindow.getAllWindows()[0]!
      if (maximized) window.maximize()
      else {
        window.unmaximize()
        window.setBounds({ x: 40, y: 40, width: 1200, height: 760 })
      }
    }, maximized)
    await expect
      .poll(() => app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0]!.isMaximized()))
      .toBe(maximized)
    const original = await app.evaluate(({ BrowserWindow }) =>
      BrowserWindow.getAllWindows()[0]!.getNormalBounds(),
    )
    await page.getByRole('button', { name: 'Enter fullscreen', exact: true }).click()
    await ready()
    await expect(page.locator('.avalon-shell.fullscreen')).toBeVisible()
    await expect(page.locator('.avalon-shell.desktop')).toHaveCount(0)
    if (maximized)
      await app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0]!.setFullScreen(false))
    else await page.keyboard.press('F11')
    await ready()
    await expect(page.locator('.avalon-shell.desktop')).toBeVisible()
    await expect
      .poll(() => app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0]!.isMaximized()))
      .toBe(maximized)
    expect(
      await app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0]!.getNormalBounds()),
    ).toEqual(original)
  })

test('controller cursor ownership restores the hovered input and releases on fullscreen exit', async () => {
  await fullscreen(1280, 720)
  await root('Library')
  await tap(13)
  await expect(page.locator('html')).toHaveAttribute('data-controller', 'true')
  await expect(page.locator('body')).toHaveCSS('cursor', 'none')
  await page.mouse.move(20, 20)
  await expect(page.locator('html')).not.toHaveAttribute('data-controller', 'true')
  await tap(13)
  await page.keyboard.press('F11')
  await ready()
  await expect(page.locator('html')).not.toHaveAttribute('data-controller', 'true')
  await root('Library')
  await fillLibrarySearch(page, 'Hollow')
  const field = page.locator('[data-library-search]')
  await field.hover()
  await expect(field).toHaveCSS('cursor', 'text')
  await tap(13)
  await expect(field).toHaveCSS('cursor', 'none')
  await field.hover({ position: { x: 4, y: 4 } })
  await expect(field).toHaveCSS('cursor', 'text')
})

for (const nested of [false, true])
  test(`repeated controller Menu preserves one menu and exact origin with nested ${nested}`, async () => {
    await fullscreen()
    await root('Library')
    let origin = page.locator('.avalon-cover').first()
    if (nested) {
      await page.getByRole('button', { name: 'Filter & sort', exact: true }).click()
      origin = page
        .getByRole('dialog', { name: 'Library filters' })
        .getByRole('combobox', { name: 'Sort', exact: true })
    }
    await origin.focus()
    for (const resume of [false, true]) {
      await tap(9)
      const menu = page.getByRole('dialog', { name: 'Quick menu', exact: true })
      await expect(menu).toHaveCount(1)
      await tap(13)
      const focus = await page.evaluateHandle(() => document.activeElement)
      for (let i = 0; i < 5; i++) await tap(9)
      await expect(menu).toHaveCount(1)
      expect(await focus.evaluate((node) => node === document.activeElement)).toBe(true)
      await focus.dispose()
      if (resume) {
        await menu.getByRole('button', { name: 'Resume', exact: true }).focus()
        await tap(0)
      } else await tap(1)
      await expect(menu).toHaveCount(0)
      await expect(origin).toBeFocused()
    }
  })

test('quick menu and fullscreen browse retain independent desktop filters and refresh behind preparation on return', async () => {
  await root('Library')
  await fillLibrarySearch(page, 'Hollow')
  await setLibrarySort(page, 'title')
  await fullscreen()
  await root('Library')
  await page.getByRole('button', { name: 'Never played', exact: true }).click()
  await page.getByRole('button', { name: 'Filter & sort', exact: true }).click()
  await tap(1)
  await tap(9)
  await expect(page.getByRole('dialog', { name: 'Quick menu' })).toBeVisible()
  await tap(1)
  await app.evaluate(() => {
    ;(globalThis as any).__fullscreenLifecycle.hold = true
  })
  await page.keyboard.press('F11')
  await expect(page.getByRole('dialog', { name: 'Preparing your library' })).toBeVisible()
  await expect(page.locator('.prepared-surfaces')).toHaveAttribute('inert', '')
  await expect
    .poll(() => app.evaluate(() => (globalThis as any).__fullscreenLifecycle.activeFeeds))
    .toBeGreaterThan(0)
  await app.evaluate(() => (globalThis as any).__fullscreenLifecycle.release())
  await ready()
  await expectLibrarySearch(page, 'Hollow')
  await expectLibrarySort(page, 'title')
  await page.keyboard.press('F11')
  await ready()
  await expect(page.getByRole('button', { name: 'Never played', exact: true })).toHaveAttribute(
    'aria-pressed',
    'true',
  )
})

test('minimum 1200x688 fullscreen uses its own search page and bounded controller keyboard', async ({}, info) => {
  await fullscreen(1200, 688)
  await root('Library')
  await tap(8)
  await expect(page.getByRole('region', { name: 'Search', exact: true })).toBeVisible()
  await expect(page.locator('.avalon-shell.desktop')).toHaveCount(0)
  const field = page.getByRole('searchbox', { name: 'Search games' })
  await field.focus()
  await tap(3)
  const keyboard = page.getByRole('dialog', { name: 'Enter text' })
  await fits(keyboard)
  await page.screenshot({ path: info.outputPath('minimum-keyboard.png') })
  await tap(1)
  await expect(keyboard).toHaveCount(0)
  await expect(field).toBeFocused()
})

for (const [width, height, scale] of [
  [2560, 1440, 0.7],
  [2560, 1440, 1],
  [2560, 1440, 1.4],
  [1920, 1080, 1],
  [1920, 1080, 1.4],
])
  test(`local section labels and trigger hints stay stationary and visible at ${width}x${height} text ${scale}`, async ({}, info) => {
    await fullscreen(width, height, scale)
    for (const name of ['Library', 'Activity', 'Settings']) {
      await root(name)
      const row = page.locator('.fullscreen-section-navigation').first(),
        hints = row.locator('[data-section-trigger]')
      await expect(hints).toHaveCount(2)
      for (const hint of await hints.all()) {
        await fits(hint)
        expect(await hint.evaluate((node) => (node as HTMLElement).tabIndex)).toBe(-1)
        await expect(hint.locator('svg')).toHaveAttribute('viewBox', '0 0 64 64')
        await expect(hint.locator('path')).toBeVisible()
      }
      const original = await row
        .locator('button,[data-section-trigger]')
        .evaluateAll((nodes) => nodes.map((node) => node.getBoundingClientRect().toJSON()))
      const tabs = row.locator('button')
      for (let index = 0; index < (await tabs.count()); index++) {
        await tabs.nth(index).focus()
        await tap(0)
        const current = await row
          .locator('button,[data-section-trigger]')
          .evaluateAll((nodes) => nodes.map((node) => node.getBoundingClientRect().toJSON()))
        expect(current, `${name} section ${index}`).toEqual(original)
        await expect(tabs.nth(index)).toHaveCSS('font-weight', '700')
      }
      await tap(7)
      await fits(hints.first())
      await fits(hints.last())
      await page.screenshot({ path: info.outputPath(`sections-${name}-${scale}.png`) })
    }
  })

for (const [width, height, scale] of [
  [1920, 1080, 1],
  [2560, 1440, 1],
  [2560, 1440, 1.4],
])
  test(`Details keeps unread badge tab geometry and restores Library backdrop origin at ${width}x${height} text ${scale}`, async ({}, info) => {
    await fullscreen(width, height, scale)
    await root('Library')
    await page.getByRole('button', { name: /^Patched, \d+ games with unread updates$/ }).click()
    const chosen = page.getByRole('button', { name: /patched since you played: 1 update/ }).first()
    const work = await chosen.getAttribute('data-avalon-game')
    await chosen.click()
    await expect(page.locator('.avalon-details.fullscreen')).toBeVisible()
    await expect(page.locator('.avalon-header nav')).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'B · Back to Library' })).toBeVisible()
    const art = page.locator('.avalon-detail-backdrop')
    await expect(art).toBeVisible()
    expect(await art.boundingBox()).toEqual({ x: 0, y: 0, width, height })
    const tabs = page.getByRole('tablist', { name: 'Game information' })
    await expect(tabs.getByRole('tab', { name: 'Updates, 1 unread update', exact: true })).toHaveText(
      'Updates 1',
    )
    await expect(tabs.locator('.avalon-details-unread')).toBeVisible()
    const geometry = () =>
      tabs.getByRole('tab').evaluateAll((nodes) => nodes.map((node) => node.getBoundingClientRect().toJSON()))
    const original = await geometry()
    for (let i = 0; i < 4; i++) {
      await tap(7)
      expect(await geometry()).toEqual(original)
    }
    await page.screenshot({ path: info.outputPath(`details-tabs-${scale}.png`) })
    await tap(1)
    await expect(page.locator('.avalon-library')).toBeVisible()
    await expect(page.locator('.avalon-header nav')).toBeVisible()
    if (work) await expect(page.locator(`[data-avalon-game="${work}"]`).first()).toBeFocused()
  })

for (const [width, height] of [
  [1280, 720],
  [3840, 2160],
])
  test(`all original palette changes preserve controller root cycling and loaded shelves at ${width}x${height}`, async () => {
    await fullscreen(width, height, 1.4)
    await preference('FullscreenSafeMargin', '2')
    await expect
      .poll(() =>
        page.evaluate(() => document.documentElement.style.getPropertyValue('--fullscreen-safe-margin')),
      )
      .toBe('2%')
    await root('For you')
    await tap(4)
    await page.getByRole('button', { name: 'Theme', exact: true }).click()
    const palettes = [
      'Winnow',
      'Nightshift',
      'Tungsten',
      'Box art',
      'Bottle green',
      'SilkCircuit',
      'SilkCircuit Dawn',
      'Rosé Pine',
      'Rosé Pine Dawn',
    ]
    for (const palette of palettes)
      await expect(
        page
          .getByRole('dialog', { name: 'Theme', exact: true })
          .getByRole('button', { name: palette, exact: true }),
      ).toBeVisible()
    await tap(1)
    await tap(5)
    for (const palette of palettes) {
      await tap(4)
      await expect(page.locator('.settings-page.mode-fullscreen')).toBeVisible()
      await page.getByRole('button', { name: 'Theme', exact: true }).click()
      await page
        .getByRole('dialog', { name: 'Theme', exact: true })
        .getByRole('button', { name: palette, exact: true })
        .click()
      await tap(5)
      const first = page.locator('[data-row-active="true"] .avalon-cover').first()
      await expect(first).toBeFocused()
      await tap(15)
      await expect(first).not.toBeFocused()
      await tap(14)
      await expect(first).toBeFocused()
      const backdrop = page.locator('.avalon-home-backdrop .avalon-backdrop')
      await expect(backdrop).toHaveAttribute('data-state', 'ready')
      const colors = await backdrop.evaluate((node) => ({
        ground: getComputedStyle(node).backgroundColor,
        gradient: getComputedStyle(node.querySelector('.avalon-backdrop-reading-veil')!).backgroundImage,
      }))
      expect(colors.gradient).toContain(colors.ground)
    }
  })

test('hover leaves actions clear while selection stays neutral and focus uses the accent', async () => {
  await fullscreen()
  await tap(4)
  const margins = page.getByRole('button', { name: 'Screen margins', exact: true })
  await margins.hover()
  await margins.focus()
  await tap(13)
  await expect(margins).not.toBeFocused()
  await expect(margins).toHaveCSS('border-bottom-color', 'rgba(0, 0, 0, 0)')
  const selected = page.getByRole('navigation', { name: 'Settings section' }).locator('[aria-pressed="true"]')
  const focused = page.locator(':focus')
  const colors = await page.evaluate(() => {
    const style = getComputedStyle(document.querySelector('.settings-page')!)
    const color = (name: string) => {
      const node = document.createElement('i')
      node.style.color = `var(${name})`
      document.querySelector('.settings-page')!.append(node)
      const result = getComputedStyle(node).color
      node.remove()
      return result
    }
    return {
      muted: color('--muted'),
      accent: color(
        style.getPropertyValue('--accent-foreground').trim() ? '--accent-foreground' : '--accent',
      ),
    }
  })
  await expect(selected).toHaveCSS('border-bottom-color', colors.muted)
  await expect(focused).toHaveCSS('border-bottom-color', colors.accent)
  expect(colors.muted).not.toBe(colors.accent)
})

test('main pages retain bounded controller focus at reference and smaller sizes with idempotent text scaling', async ({}, info) => {
  await fullscreen()
  for (const name of ['For you', 'Library', 'Activity', 'Settings']) {
    await expect(
      page.getByRole('navigation', { name: 'Main navigation' }).getByRole('button', { name, exact: true }),
    ).toHaveAttribute('aria-current', 'page')
    const focus = page.locator('#main-content button:focus')
    await fits(focus)
    if (name === 'For you' || name === 'Library') {
      await expect(focus).toHaveClass(/avalon-cover/)
      expect(await focus.evaluate((node) => getComputedStyle(node).outlineStyle)).not.toBe('none')
    }
    await page.screenshot({ path: info.outputPath(`reference-${name}.png`) })
    await tap(5)
  }
  await page.locator('[data-row-active="true"] .avalon-cover').first().click()
  await expect(page.locator('.avalon-details.fullscreen')).toBeVisible()
  await page.screenshot({ path: info.outputPath('reference-details.png') })
  await tap(1)
  await tap(8)
  await page.getByRole('searchbox', { name: 'Search games' }).focus()
  await tap(3)
  await fits(page.getByRole('dialog', { name: 'Enter text' }))
  await page.screenshot({ path: info.outputPath('reference-keyboard.png') })
  await tap(1)
  await tap(1)
  await app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0]!.setContentSize(1280, 720))
  await fits(page.locator('#main-content button:focus'))
  await page.screenshot({ path: info.outputPath('small-home.png') })
  await tap(5)
  await fits(page.locator('#main-content button:focus'))
  await page.screenshot({ path: info.outputPath('small-library.png') })
  const title = page.locator('.avalon-cover:focus .avalon-cover-fallback')
  await expect(title).toBeVisible()
  const font = () => title.evaluate((node) => Number.parseFloat(getComputedStyle(node).fontSize))
  const normal = await font()
  await preference('FullscreenTextScale', '1.4')
  await expect.poll(font).toBeCloseTo(normal * 1.4, 1)
  const enlarged = await font()
  // The original 1920-wide logical canvas is rendered at 2/3 size on this viewport.
  expect((enlarged * 1920) / 1280).toBeGreaterThanOrEqual(24)
  expect((enlarged * 1920) / 1280).toBeLessThanOrEqual(34)
  expect(
    await title.evaluate((node) => {
      const label = node.getBoundingClientRect(),
        cover = node.parentElement!.getBoundingClientRect()
      return (
        label.top >= cover.top && label.bottom <= cover.bottom && node.scrollWidth <= node.clientWidth + 1
      )
    }),
  ).toBe(true)
  await preference('FullscreenTextScale', '1.4')
  expect(await font()).toBe(enlarged)
  await fits(page.locator('#main-content button:focus'))
  await page.screenshot({ path: info.outputPath('small-library-large-text.png') })
  await tap(5)
  await tap(5)
  await fits(page.locator('#main-content button:focus'))
  await page.screenshot({ path: info.outputPath('small-settings-large-text.png') })
})

test('ultrawide fit expands and restores content without changing typography', async () => {
  await fullscreen(2560, 1080)
  await root('Library')
  const measure = () =>
    page.locator('.avalon-header').evaluate((node) => ({
      width: node.getBoundingClientRect().width,
      font: getComputedStyle(node.querySelector('nav button')!).fontSize,
    }))
  await preference('FullscreenFitUltrawide', 'false')
  await expect(page.locator('html')).toHaveAttribute('data-fit-ultrawide', 'false')
  const bounded = await measure()
  await preference('FullscreenFitUltrawide', 'true')
  await expect(page.locator('html')).toHaveAttribute('data-fit-ultrawide', 'true')
  const expanded = await measure()
  expect(expanded.width).toBeGreaterThan(bounded.width)
  expect(expanded.font).toBe(bounded.font)
  await app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0]!.setContentSize(3440, 1440))
  const larger = await measure()
  expect(larger.width).toBeGreaterThan(expanded.width)
  await preference('FullscreenFitUltrawide', 'false')
  await expect(page.locator('html')).toHaveAttribute('data-fit-ultrawide', 'false')
  expect((await measure()).width).toBeLessThan(larger.width)
})
