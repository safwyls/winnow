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

type Mode = 'desktop' | 'fullscreen'
let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
const storeUrl = 'https://store.epicgames.com/en-US/p/moonlighter'
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-install-refresh-'))
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/install-action-refresh-main.mjs'),
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
  await expect(page.locator('.avalon-shell')).toBeVisible()
})
test.afterAll(async () => closeFixture(application, directory))
test.afterEach(async ({}, info) => {
  if (info.status !== info.expectedStatus) await page.screenshot({ path: info.outputPath('failure.png') })
  await application.evaluate(() => {
    const state = (globalThis as any).installRefreshFixture
    state.release?.()
    state.hold = false
  })
  expect(errors).toEqual([])
  expect(await application.evaluate(() => (globalThis as any).installRefreshFixture.forbidden)).toEqual([])
})
async function setup(
  mode: Mode,
  size: readonly [number, number] = [1200, 640],
  options: { store?: 'steam'; installed?: boolean; cached?: boolean } = {},
) {
  await application.evaluate(
    ({}, options) =>
      Object.assign((globalThis as any).installRefreshFixture, {
        store: 'epic',
        installed: false,
        cached: false,
        hold: false,
        external: [],
        actions: [],
        forbidden: [],
        release: null,
        ...options,
      }),
    options,
  )
  await page.reload()
  await expect(page.locator('.avalon-shell')).toBeVisible()
  await application.evaluate(
    ({ BrowserWindow }, { mode, size }) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setFullScreen(false)
      window.setContentSize(size[0], size[1])
      window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
      window.focus()
    },
    { mode, size },
  )
  await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  await page.evaluate(async () => {
    const result = await window.winnow.request({
      route: 'preferences.presentation.put',
      params: { preference: 'LinkDestination' },
      body: { value: 'browser' },
    })
    if (!result.ok) throw Error(result.message)
  })
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
  await expect(page.locator('.avalon-cover')).toHaveCount(1)
  await page.locator('.avalon-cover').click()
  await expect(page.locator('.avalon-details')).toBeVisible()
  await expect(primary()).toHaveText(options.installed ? 'Play' : 'Install')
}
function primary() {
  return page.locator('.avalon-details [data-controller-play]')
}
function more() {
  return page.locator('.avalon-details-more > button')
}
function storeLink() {
  return page
    .getByRole('navigation', { name: 'Game links' })
    .getByRole('button', { name: 'Epic Games store page', exact: true })
}
async function openMore() {
  if ((await more().getAttribute('aria-expanded')) !== 'true') await more().click()
  await expect(page.getByRole('navigation', { name: 'Game links' })).toBeVisible()
  await page.getByRole('navigation', { name: 'Game links' }).evaluate(async (element) => {
    const panel = element.closest('.avalon-actions-panel')
    await Promise.all(panel?.getAnimations().map((animation) => animation.finished) ?? [])
  })
}
async function closeMore() {
  await page.keyboard.press('Escape')
  await expect(more()).toHaveAttribute('aria-expanded', 'false')
}
async function closeDetails(mode: Mode) {
  await page
    .getByRole('button', {
      name: mode === 'desktop' ? 'Close game details' : 'B · Back to Library',
      exact: true,
    })
    .click()
  await expect(page.locator('.avalon-details')).toHaveCount(0)
}
async function refresh(installed: boolean) {
  await application.evaluate(({ BrowserWindow }, installed) => {
    ;(globalThis as any).installRefreshFixture.installed = installed
    BrowserWindow.getAllWindows()[0]!.webContents.send('winnow:event', {
      kind: 'library.changed',
      epoch: 'install-refresh',
      sequence: Date.now(),
      occurredAt: new Date().toISOString(),
    })
  }, installed)
  if (await primary().count()) await expect(primary()).toHaveText(installed ? 'Play' : 'Install')
}
async function counts() {
  return application.evaluate(() => {
    const state = (globalThis as any).installRefreshFixture
    return { external: state.external, actions: state.actions }
  })
}
async function geometry(locator: Locator) {
  return locator.evaluate((element) => {
    const rect = element.getBoundingClientRect()
    return { x: rect.x, y: rect.y, width: rect.width, height: rect.height }
  })
}
async function sameBounds(locator: Locator, expected: Awaited<ReturnType<typeof geometry>>) {
  await expect
    .poll(async () => {
      const actual = await geometry(locator)
      return Object.keys(expected).every(
        (key) => Math.abs(actual[key as keyof typeof actual] - expected[key as keyof typeof expected]) <= 1,
      )
    })
    .toBe(true)
}
async function readable(link = storeLink()) {
  await expect(link).toBeVisible()
  await expect(link).toBeEnabled()
  await expect(link).toHaveText('Epic Games store page')
  await expect(link).toHaveAttribute('title', storeUrl)
  await expect
    .poll(() =>
      link.evaluate((element) => {
        const rect = element.getBoundingClientRect(),
          text = element.querySelector('span')!,
          textRect = text.getBoundingClientRect()
        const hit = document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2)
        return (
          getComputedStyle(element).opacity === '1' &&
          rect.width > 0 &&
          rect.height > 0 &&
          textRect.width > 0 &&
          textRect.height > 0 &&
          textRect.left >= rect.left &&
          textRect.right <= rect.right + 1 &&
          hit?.closest('button') === element
        )
      }),
    )
    .toBe(true)
}
async function clickCenter(link = storeLink()) {
  await readable(link)
  const rect = await geometry(link)
  await page.mouse.click(rect.x + rect.width / 2, rect.y + rect.height / 2)
}
async function expectReferenceSizing(size: readonly [number, number], ui = 1, text = 1, safe = 0.05) {
  const viewportScale = Math.min(size[0] / 1920, size[1] / 1080)
  const zoom = 0.85 * ui * viewportScale
  // Each preference write publishes separately; measure only after the last
  // setting has reached the renderer as well as the earlier text-size change.
  await expect
    .poll(() =>
      page.evaluate(() => {
        const style = document.documentElement.style
        return ['--fullscreen-interface-scale', '--fullscreen-text-scale', '--fullscreen-safe-ratio'].map(
          (key) => Number(style.getPropertyValue(key)),
        )
      }),
    )
    .toEqual([ui, text, safe])
  await expect
    .poll(() => storeLink().evaluate((element) => parseFloat(getComputedStyle(element).fontSize)))
    .toBeCloseTo(28 * text, 2)
  const measured = await page.locator('.avalon-actions-panel').evaluate((element) => {
    const css = getComputedStyle(element)
    return {
      width: element.getBoundingClientRect().width,
      zoom: Number(getComputedStyle(document.body).zoom),
      rightPadding: parseFloat(css.paddingRight),
      topPadding: parseFloat(css.paddingTop),
    }
  })
  expect(measured.zoom).toBeCloseTo(zoom, 2)
  expect(Math.abs(measured.width - 620 * text * zoom)).toBeLessThanOrEqual(1)
  expect(Math.abs(measured.rightPadding * measured.zoom - size[0] * safe)).toBeLessThanOrEqual(1)
  expect(Math.abs(measured.topPadding * measured.zoom - size[1] * safe)).toBeLessThanOrEqual(1)
  return { physicalFont: 28 * text * measured.zoom, width: measured.width }
}
async function traverseLastAction() {
  await page.evaluate(() => {
    const state = { pressed: -1 }
    Object.assign(window, { installRefreshPad: state })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          id: 'Simulated install refresh controller',
          index: 0,
          connected: true,
          mapping: 'standard',
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, index) => ({
            pressed: state.pressed === index,
            value: state.pressed === index ? 1 : 0,
          })),
        },
      ],
    })
  })
  const panel = page.getByRole('dialog', { name: 'More game actions', exact: true })
  const buttons = panel.locator('button:not(:disabled)')
  await buttons.first().focus()
  const names: string[] = []
  for (let index = 0; index < (await buttons.count()); index++) {
    await expect(buttons.nth(index)).toBeFocused()
    names.push((await buttons.nth(index).textContent())!.trim())
    await page.evaluate(async () => {
      const state = (window as unknown as { installRefreshPad: { pressed: number } }).installRefreshPad
      for (const pressed of [-1, 13, -1]) {
        state.pressed = pressed
        await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
      }
    })
  }
  expect(names).toEqual([
    'Add to list',
    'Epic Games store page',
    'Manage in launcher',
    'Refetch metadata',
    'Wrong game?Search IGDB for the right entry',
    'Edit details',
    'Artwork…',
    'Hide game…',
  ])
  const last = panel.getByRole('button', { name: 'Hide game…', exact: true })
  await expect(last).toBeFocused()
  expect(
    await last.evaluate((element) => {
      const row = element.getBoundingClientRect(),
        body = element.closest('.avalon-actions-body')!.getBoundingClientRect()
      return (
        row.top >= body.top - 1 &&
        row.bottom <= body.bottom + 1 &&
        document.elementFromPoint(row.x + row.width / 2, row.y + row.height / 2)?.closest('button') ===
          element
      )
    }),
  ).toBe(true)
}
let referencePanel: { physicalFont: number; width: number } | undefined
for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} reload preserves the Moonlighter editor draft, Epic cache URL, folder state and selected work`, async ({}, info) => {
    await setup(mode, mode === 'desktop' ? [1200, 640] : [1280, 720], { cached: true })
    const detail = await page.locator('.avalon-details').elementHandle()
    await openMore()
    await expect(storeLink()).toHaveAttribute('title', 'https://store.epicgames.com/p/moonlighter')
    await page.getByRole('button', { name: 'Edit details', exact: true }).click()
    const editor = page.getByRole('dialog', { name: 'Edit metadata · Moonlighter' })
    if (mode === 'fullscreen') await editor.getByRole('button', { name: 'Name · IGDB' }).click()
    const name = editor.getByLabel('Name', { exact: true })
    // The source contract is preservation of a typed draft. Keyboard behavior has
    // its own native suite; use the real field's input event in both presentations.
    await name.evaluate((input) => {
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!
      setter.call(input, 'An unsaved edit')
      input.dispatchEvent(new Event('input', { bubbles: true }))
    })
    const originalName = await name.elementHandle()
    for (const installed of [true, false]) {
      await refresh(installed)
      await expect(editor).toBeVisible()
      await expect(name).toHaveValue('An unsaved edit')
      expect(
        await page.locator('.avalon-details').evaluate((element, original) => element === original, detail),
      ).toBe(true)
      expect(await name.evaluate((element, original) => element === original, originalName)).toBe(true)
      const snapshot = await page.evaluate(async () => {
        const response = await window.winnow.request({ route: 'game.details', params: { workId: 1 } })
        if (!response.ok) throw Error(response.message)
        return response.data as { ownerships: { id: number; installPath: string | null }[] }
      })
      expect(snapshot.ownerships).toEqual([
        expect.objectContaining({ id: 1, installPath: installed ? 'C:\\Games\\Moonlighter' : null }),
      ])
    }
    await page.screenshot({ path: info.outputPath('retained-editor-draft.png') })
    await editor.getByRole('button', { name: 'Back', exact: true }).click()
    if (mode === 'fullscreen') await editor.getByRole('button', { name: 'Back', exact: true }).click()
    await expect(editor).toHaveCount(0)
    await refresh(true)
    await openMore()
    await expect(page.getByRole('button', { name: 'Open install folder', exact: true })).toBeVisible()
    await expect(storeLink()).toHaveAttribute('title', 'https://store.epicgames.com/p/moonlighter')
    await closeMore()
    await refresh(false)
    await openMore()
    await expect(page.getByRole('button', { name: 'Open install folder', exact: true })).toHaveCount(0)
    await closeMore()
    await closeDetails(mode)
    await refresh(false)
    const tile = page.getByRole('button', { name: 'View Moonlighter', exact: true })
    await expect(tile).toBeFocused()
    await expect(tile).toHaveAttribute('data-selected', 'true')
    await expect(tile).toHaveAttribute('data-work-id', '1')
    await tile.click()
    await expect(primary()).toHaveText('Install')
    expect(await counts()).toEqual({ external: [], actions: [] })
  })

  test(`${mode} Steam 620 uninstall disappears after refresh and the retained details offer Install`, async () => {
    await setup(mode, mode === 'desktop' ? [1200, 640] : [1280, 720], { store: 'steam', installed: true })
    await openMore()
    const uninstall = page.getByRole('button', { name: 'Uninstall in Steam', exact: true })
    await expect(uninstall).toBeVisible()
    await uninstall.focus()
    await expect(uninstall).toBeFocused()
    await closeMore()
    await refresh(false)
    await openMore()
    await expect(page.getByRole('button', { name: 'Uninstall in Steam', exact: true })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Store page', exact: true })).toHaveAttribute(
      'title',
      'https://store.steampowered.com/app/620/',
    )
    expect(await counts()).toEqual({ external: [], actions: [] })
  })

  test(`${mode} Moonlighter refresh dispatches current Play and preserves three store visits through reopening details`, async () => {
    await setup(mode, mode === 'desktop' ? [1200, 640] : [1280, 720])
    const detail = await page.locator('.avalon-details').elementHandle()
    await expect(primary().locator('.lucide-download')).toBeVisible()
    await expect(primary().locator('.lucide-play')).toHaveCount(0)
    await openMore()
    await clickCenter()
    await expect.poll(async () => (await counts()).external).toEqual([storeUrl])
    await closeMore()
    await refresh(true)
    expect(
      await page.locator('.avalon-details').evaluate((element, original) => element === original, detail),
    ).toBe(true)
    await expect(primary().locator('.lucide-play')).toBeVisible()
    await expect(primary().locator('.lucide-download')).toHaveCount(0)
    await openMore()
    const box = await geometry(storeLink())
    await clickCenter()
    await expect.poll(async () => (await counts()).external).toEqual([storeUrl, storeUrl])
    await closeMore()
    await primary().click()
    await expect(primary()).toBeEnabled()
    expect((await counts()).actions).toEqual([
      expect.objectContaining({ ownershipId: 1, action: 'Play', installed: true }),
    ])
    await openMore()
    await readable()
    await sameBounds(storeLink(), box)
    await closeMore()
    await closeDetails(mode)
    await page.getByRole('button', { name: 'View Moonlighter', exact: true }).click()
    await expect(primary()).toHaveText('Play')
    await openMore()
    await readable()
    await sameBounds(storeLink(), box)
    await clickCenter()
    await expect.poll(async () => (await counts()).external).toEqual(Array(3).fill(storeUrl))
    expect((await counts()).actions).toHaveLength(1)
  })

  for (const size of [
    [1200, 640],
    [1280, 820],
    [1920, 1080],
    [3840, 2160],
  ] as const) {
    test(`${mode} ${size[0]}x${size[1]} store text and hit target survive pending and completed Install with one dispatch`, async ({}, info) => {
      await setup(mode, size)
      await openMore()
      await readable()
      if (mode === 'fullscreen' && size[0] >= 1920) {
        const measurements = await expectReferenceSizing(size)
        if (size[0] === 1920) referencePanel = measurements
        else if (referencePanel) {
          expect(measurements.physicalFont / referencePanel.physicalFont).toBeCloseTo(2, 2)
          expect(measurements.width / referencePanel.width).toBeCloseTo(2, 2)
        }
      }
      const box = await geometry(storeLink()),
        trigger = await geometry(more())
      await closeMore()
      await application.evaluate(() => {
        ;(globalThis as any).installRefreshFixture.hold = true
      })
      await primary().focus()
      await expect(primary()).toBeFocused()
      await primary().click()
      await expect(primary()).toBeDisabled()
      // A second physical pointer hit on the disabled action must not dispatch.
      const launch = await geometry(primary())
      await page.mouse.click(launch.x + launch.width / 2, launch.y + launch.height / 2)
      await sameBounds(more(), trigger)
      await openMore()
      await readable()
      await sameBounds(storeLink(), box)
      const retained = await storeLink().elementHandle()
      await clickCenter()
      await expect.poll(async () => (await counts()).external).toEqual([storeUrl])
      expect((await counts()).actions).toEqual([
        expect.objectContaining({ ownershipId: 1, action: 'Install', installed: false }),
      ])
      await page.screenshot({ path: info.outputPath('store-link-during-install.png') })
      await application.evaluate(() => {
        ;(globalThis as any).installRefreshFixture.release()
      })
      await expect(primary()).toBeEnabled()
      await page.mouse.move(8, 8)
      if (mode === 'fullscreen') {
        const status = page.locator('.avalon-details-actions .status-message')
        await expect(status).toHaveText('Sent to the launcher. The launcher will handle the next step.')
        expect(await status.evaluate((element) => parseFloat(getComputedStyle(element).fontSize))).toBe(24)
      }
      await sameBounds(more(), trigger)
      await sameBounds(storeLink(), box)
      expect(await storeLink().evaluate((element, original) => element === original, retained)).toBe(true)
      await clickCenter()
      await expect.poll(async () => (await counts()).external).toEqual([storeUrl, storeUrl])
      await storeLink().focus()
      await expect(storeLink()).toBeFocused()
      await readable()
      await page.screenshot({ path: info.outputPath('store-link-after-install.png') })
      expect((await counts()).actions).toHaveLength(1)
      if (mode === 'fullscreen' && size[0] >= 1920) {
        await traverseLastAction()
        await page.screenshot({ path: info.outputPath('last-action-controller-focus.png') })
      }
    })
  }
}

test('fullscreen action sheet preserves interface scale, text size and safe margins with controller access to its last row', async ({}, info) => {
  await setup('fullscreen', [1920, 1080])
  await page.evaluate(async () => {
    for (const [preference, value] of [
      ['FullscreenInterfaceScale', '0.8'],
      ['FullscreenTextScale', '1.4'],
      ['FullscreenSafeMargin', '8'],
    ]) {
      const result = await window.winnow.request({
        route: 'preferences.presentation.put',
        params: { preference },
        body: { value },
      })
      if (!result.ok) throw Error(result.message)
    }
  })
  await openMore()
  await expectReferenceSizing([1920, 1080], 0.8, 1.4, 0.08)
  await readable()
  await traverseLastAction()
  await page.screenshot({ path: info.outputPath('scaled-safe-action-sheet.png') })
  expect(await counts()).toEqual({ external: [], actions: [] })
})
