import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Page,
  type Locator,
} from '@playwright/test'
import electronPath from 'electron'
import { mkdtemp, mkdir, readFile, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { DEFAULT_PROFILE } from '../../src/shared/theme'
import { profileDirectory } from '../../src/main/storage'
import { closeFixture } from './fixture-cleanup'
import {
  prebuiltBackend as backend,
  prebuiltFixture as fixture,
  prebuiltActivationHelper,
} from './prebuilt-backend'

let app: ElectronApplication, page: Page, directory: string
const errors: string[] = []
const wizard = () => page.locator('.setup-dialog')
async function pad() {
  await page.evaluate(() => {
    const value = { pressed: [] as number[] }
    Object.assign(window, { setupContractsPad: value })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          id: 'Native setup fixture controller',
          connected: true,
          mapping: 'standard',
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, index) => ({
            pressed: value.pressed.includes(index),
            touched: value.pressed.includes(index),
            value: value.pressed.includes(index) ? 1 : 0,
          })),
        },
      ],
    })
  })
}
async function tap(button: number) {
  for (const pressed of [[], [button], []])
    await page.evaluate(async (pressed) => {
      ;(window as unknown as { setupContractsPad: { pressed: number[] } }).setupContractsPad.pressed = pressed
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
}
async function surface(mode: 'desktop' | 'fullscreen', width: number, height: number) {
  await app.evaluate(
    ({ BrowserWindow }, value) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setFullScreen(false)
      window.setMinimumSize(0, 0)
      window.setContentSize(value.width, value.height)
      window.isFullScreen = () => value.mode === 'fullscreen'
      window.webContents.send('winnow:fullscreen:changed', value.mode === 'fullscreen')
      window.focus()
    },
    { mode, width, height },
  )
  await expect(wizard()).toHaveClass(new RegExp(`mode-${mode}`))
  await expect.poll(() => page.evaluate(() => [innerWidth, innerHeight])).toEqual([width, height])
}
async function activate(target: Locator, controller = false) {
  await target.focus()
  await expect(target).toBeFocused()
  if (controller) await tap(0)
  else await page.keyboard.press('Enter')
}
async function capture(name: string) {
  await page.screenshot({ path: test.info().outputPath(`${name}.png`) })
}
const progress = () =>
  page.evaluate(async () => {
    const result = await window.winnow.request<{ step: number | null }>({ route: 'setup.get' })
    if (!result.ok) throw Error(result.message)
    if (!result.data) throw Error('Setup progress response was empty')
    return result.data
  })
const calls = () => app.evaluate(() => (globalThis as any).__setupContracts.requests as { route: string }[])

test.beforeAll(async () => {
  await Promise.all([readFile(fixture), readFile(backend)])
})
test.beforeEach(async ({}, info) => {
  test.setTimeout(60000)
  errors.length = 0
  const cold = info.title.includes('cold saved')
  const mode = info.title.startsWith('fullscreen') ? 'fullscreen' : 'desktop'
  directory = await mkdtemp(
    join(
      resolve('../..', '.tmp'),
      cold ? `winnow-electron-startup-boundary-${mode}-setup-` : 'winnow-electron-setup-contracts-',
    ),
  )
  const profile = structuredClone(DEFAULT_PROFILE)
  profile.appearance.scale = 100
  profile.appearance.reducedMotion = true
  profile.settings.avalon = {
    ...profile.settings.avalon,
    palette: info.title.includes('rose-pine-dawn') ? 'rose-pine-dawn' : 'winnow',
  }
  const preferences = profileDirectory(join(directory, 'electron-userdata'), directory)
  await mkdir(preferences, { recursive: true })
  await writeFile(join(preferences, 'preferences.json'), JSON.stringify(profile))
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/setup-contracts-main.mjs'),
      '--data-dir',
      directory,
      '--no-sync',
      '--force-color-profile=srgb',
    ],
    env: {
      ...(Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ) as Record<string, string>),
      WINNOW_BACKEND_PATH: cold ? fixture : backend,
      WINNOW_ACTIVATION_HELPER_PATH: prebuiltActivationHelper,
      WINNOW_SETUP_HOLD_PREFERENCES: cold ? '1' : '0',
    },
    chromiumSandbox: true,
    timeout: 60000,
  })
  if (cold) {
    await expect
      .poll(() => app.evaluate(() => (globalThis as any).__setupContracts.preferences.held), {
        timeout: 30000,
      })
      .toBe(true)
    const held = await app.evaluate(({ BrowserWindow }) => {
      const state = (globalThis as any).__setupContracts
      state.heldWindowCount = BrowserWindow.getAllWindows().length
      return { count: state.heldWindowCount, firstSetup: state.firstSetup, status: state.preferences.status }
    })
    expect(held).toEqual({ count: 0, firstSetup: null, status: 200 })
    await app.evaluate(() => (globalThis as any).__releaseSetupPreferences())
  }
  page = await app.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(wizard().getByRole('heading', { name: 'Welcome to Winnow', exact: true })).toBeVisible({
    timeout: 45000,
  })
  if (!cold) await surface(mode, mode === 'desktop' ? 1200 : 1920, mode === 'desktop' ? 604 : 1080)
  await pad()
})
test.afterEach(async ({}, info) => {
  try {
    if (app)
      await info.attach('setup-native-boundary-ledger', {
        body: JSON.stringify(
          await app.evaluate(() => ({
            setup: (globalThis as any).__setupContracts,
            dispatches: (globalThis as any).__identityProjections.dispatches,
          })),
          null,
          2,
        ),
        contentType: 'application/json',
      })
    if (info.status !== info.expectedStatus && page && !page.isClosed()) {
      await info.attach('setup-before-teardown', { body: await page.screenshot(), contentType: 'image/png' })
      await info.attach('setup-before-teardown-dom', {
        body: await page.locator('body').ariaSnapshot(),
        contentType: 'text/plain',
      })
    }
  } finally {
    await closeFixture(app, directory)
  }
  expect(errors).toEqual([])
})

for (const palette of ['winnow', 'rose-pine-dawn'])
  test(`desktop ${palette} source seven pixel inner corners clip every header and footer fill`, async () => {
    const frame = wizard(),
      clip = frame.locator('.setup-content-clip')
    await expect(frame).toHaveCSS('border-top-width', '1px')
    await expect(frame).toHaveCSS('border-radius', '8px')
    await expect(frame).toHaveCSS('padding', '0px')
    await expect(frame).toHaveCSS('max-width', 'none')
    await expect(clip).toHaveCSS('border-radius', '7px')
    await expect(clip).toHaveCSS('overflow', 'hidden')
    await page.evaluate(() => document.fonts.ready)
    const bounds = (await clip.boundingBox())!
    const outer = (await frame.boundingBox())!
    expect(outer.width).toBeCloseTo(860, 0)
    expect(bounds.x - outer.x).toBeCloseTo(1, 1)
    expect(bounds.y - outer.y).toBeCloseTo(1, 1)
    expect(outer.x + outer.width - bounds.x - bounds.width).toBeCloseTo(1, 1)
    const corrected = await page.screenshot({ path: test.info().outputPath(`setup-rounded-${palette}.png`) })
    await clip.evaluate((node) => {
      ;(node as HTMLElement).style.overflow = 'visible'
    })
    const square = await page.screenshot({ path: test.info().outputPath(`setup-unclipped-${palette}.png`) })
    await clip.evaluate((node) => {
      ;(node as HTMLElement).style.removeProperty('overflow')
    })
    const corners = await app.evaluate(
      ({ nativeImage }, value) => {
        const normal = nativeImage.createFromBuffer(Buffer.from(value.corrected)),
          square = nativeImage.createFromBuffer(Buffer.from(value.square))
        const expected = normal.toBitmap(),
          actual = square.toBitmap(),
          width = normal.getSize().width
        return [false, true].flatMap((right) =>
          [false, true].map((bottom) => {
            let changed = 0
            for (let y = 0; y < 7; y++)
              for (let x = 0; x < 7; x++) {
                const px = Math.floor(value.bounds.x) + (right ? Math.floor(value.bounds.width) - 1 - x : x)
                const py = Math.floor(value.bounds.y) + (bottom ? Math.floor(value.bounds.height) - 1 - y : y)
                const offset = (py * width + px) * 4
                if (!expected.subarray(offset, offset + 4).equals(actual.subarray(offset, offset + 4)))
                  changed++
              }
            return { right, bottom, changed }
          }),
        )
      },
      { corrected: [...corrected], square: [...square], bounds },
    )
    for (const corner of corners) expect(corner.changed, JSON.stringify(corner)).toBeGreaterThan(0)
    await test.info().attach('source-seven-pixel-corner-differences', {
      body: JSON.stringify({ palette, bounds, corners }),
      contentType: 'application/json',
    })
  })

for (const mode of ['desktop', 'fullscreen'])
  test(`${mode} cold saved presentation opens Welcome only after startup in its original native mode`, async () => {
    await expect
      .poll(() => app.evaluate(() => (globalThis as any).__setupContracts.firstSetup))
      .not.toBeNull()
    const first = await app.evaluate(() => (globalThis as any).__setupContracts.firstSetup)
    expect(first).toMatchObject({ mode, startupPresent: false })
    expect(first.className).toContain(`mode-${mode}`)
    if (mode === 'fullscreen') {
      const actual = (await wizard().boundingBox())!
      const viewport = await page.evaluate(() => ({ width: innerWidth, height: innerHeight }))
      expect(actual.x).toBeCloseTo(0, 0)
      expect(actual.y).toBeCloseTo(0, 0)
      expect(actual.width).toBeCloseTo(viewport.width, 0)
      expect(actual.height).toBeCloseTo(viewport.height, 0)
    }
    expect(await app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0]!.isFullScreen())).toBe(
      mode === 'fullscreen',
    )
    expect((await progress()).step).toBe(0)
    const build = await page.evaluate(() => window.winnow.applicationInfo!())
    expect(build.packaged).toBe(false)
    expect(build.version).toBe(JSON.parse(await readFile(resolve('package.json'), 'utf8')).version)
    expect(build.commit).toMatch(/^[a-f0-9]{40}$/)
    await test.info().attach('actual-unpackaged-frontend-build', {
      body: JSON.stringify(build),
      contentType: 'application/json',
    })
    await expect(wizard().getByRole('button', { name: 'Get started', exact: true })).toBeFocused()
    await capture(`cold-selected-${mode}`)
    await activate(wizard().getByRole('button', { name: 'Skip setup', exact: true }), mode === 'fullscreen')
    await expect(wizard()).toHaveCount(0)
    expect((await progress()).step).toBeNull()
  })

test('desktop source all nine steps retain navigation and fixed IGDB actions at 1200 by 604 then restore the replay origin', async () => {
  await activate(wizard().getByRole('button', { name: 'Skip setup', exact: true }))
  await expect(wizard()).toHaveCount(0)
  await page.getByRole('button', { name: 'Settings', exact: true }).click()
  await page.getByRole('button', { name: 'Application', exact: true }).click()
  const origin = page.getByRole('button', { name: 'Run setup again', exact: true })
  await activate(origin)
  for (let step = 0; step < 9; step++) {
    await expect(wizard().getByText(`SETUP \u00b7 ${step + 1} OF 9`, { exact: true })).toBeVisible()
    const next = wizard().getByRole('button', {
      name: step === 0 ? 'Get started' : step === 8 ? 'Open my library' : 'Continue',
      exact: true,
    })
    await expect(next).toBeFocused()
    for (const target of await wizard().locator('.setup-footer button, .setup-header h2').all()) {
      const box = (await target.boundingBox())!
      expect(box.width).toBeGreaterThan(0)
      expect(box.height).toBeGreaterThan(0)
      expect(box.x).toBeGreaterThanOrEqual(0)
      expect(box.y).toBeGreaterThanOrEqual(0)
      expect(box.x + box.width).toBeLessThanOrEqual(1201)
      expect(box.y + box.height).toBeLessThanOrEqual(605)
    }
    if (step === 1) {
      for (const name of ['Save credentials', 'Remove saved credentials']) {
        const action = wizard().getByRole('button', { name, exact: true })
        await expect(action).toBeVisible()
        const fact = await action.evaluate((node) => {
          const rect = node.getBoundingClientRect()
          const scrollingAncestors: string[] = []
          for (let parent = node.parentElement; parent; parent = parent.parentElement)
            if (/auto|scroll/.test(getComputedStyle(parent).overflowY))
              scrollingAncestors.push(parent.className)
          return {
            within:
              rect.x >= 0 && rect.y >= 0 && rect.right <= innerWidth + 1 && rect.bottom <= innerHeight + 1,
            scrollingAncestors,
          }
        })
        expect(fact).toEqual({ within: true, scrollingAncestors: [] })
      }
    }
    if ([2, 3, 4].includes(step))
      await expect(wizard().getByRole('heading', { name: 'Platforms', exact: true })).toHaveCount(0)
    if (step === 5) {
      for (const name of ['Winnow design', 'Theme', 'Backdrop', 'Pane layout'])
        await expect(wizard().getByRole('combobox', { name, exact: true })).toBeVisible()
      for (const name of ['Transparency', 'Theme text size'])
        await expect(wizard().getByRole('slider', { name, exact: true })).toBeVisible()
      await expect(wizard().getByRole('checkbox', { name: /Include content panes/ })).toBeVisible()
      for (const name of ['Heading font', 'Interface font', 'Data font'])
        await expect(wizard().getByRole('combobox', { name, exact: true })).toBeVisible()
      const palette = wizard().getByRole('combobox', { name: 'Theme', exact: true })
      const saved = join(
        profileDirectory(join(directory, 'electron-userdata'), directory),
        'preferences.json',
      )
      for (const value of ['nightshift', 'winnow']) {
        await palette.selectOption(value)
        await expect(palette).toHaveValue(value)
        await expect
          .poll(async () => JSON.parse(await readFile(saved, 'utf8')).settings.avalon.palette)
          .toBe(value)
        await expect(next).toBeEnabled()
      }
      await next.focus()
      await expect(next).toBeFocused()
    }
    if (step === 6) {
      await expect(wizard().getByRole('button', { name: 'Run setup again', exact: true })).toHaveCount(0)
      await expect(wizard().locator('.igdb-connection-panel')).toHaveCount(0)
    }
    await capture(`desktop-short-step-${step}`)
    await page.keyboard.press('Enter')
  }
  await expect(wizard()).toHaveCount(0)
  await expect(origin).toBeFocused()
  expect((await progress()).step).toBeNull()
})

test('desktop source masked keyboard owns the setup input layer and mode flips clear its unsaved draft', async () => {
  await surface('desktop', 1200, 640)
  expect(
    await page.locator('.avalon-shell').evaluate((node) => ({
      hidden: !!node.closest('[aria-hidden="true"], [inert]'),
      pointerEvents: getComputedStyle(node).pointerEvents,
    })),
  ).toEqual({ hidden: true, pointerEvents: 'none' })
  await activate(wizard().getByRole('button', { name: 'Get started', exact: true }))
  const secret = wizard().getByLabel('Client secret', { exact: true })
  await expect(secret).toHaveAttribute('type', 'password')
  await secret.focus()
  await tap(3)
  const keyboard = page.getByRole('dialog', { name: 'Enter text', exact: true })
  await expect(keyboard).toBeVisible()
  const key = keyboard.getByRole('button', { name: '1', exact: true })
  await expect(key).toBeEnabled()
  const topmost = await key.evaluate((node) => {
    const rect = node.getBoundingClientRect()
    return !!document
      .elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2)
      ?.closest('.onscreen-keyboard')
  })
  expect(topmost).toBe(true)
  await expect(key).toBeFocused()
  await tap(0)
  await expect(secret).not.toHaveValue('')
  await expect(secret).toHaveAttribute('type', 'password')
  await capture('desktop-masked-setup-keyboard')
  await tap(1)
  await expect(keyboard).toHaveCount(0)
  await expect(secret).toBeFocused()
  expect((await progress()).step).toBe(1)
  await surface('fullscreen', 1920, 1080)
  await surface('desktop', 1200, 640)
  await expect(wizard().getByLabel('Client secret', { exact: true })).toHaveValue('')
  expect(await wizard().evaluate((node) => node.contains(document.activeElement))).toBe(true)
  expect((await calls()).filter((call) => /connections\.igdb\.(put|delete)/.test(call.route))).toEqual([])
  await activate(wizard().getByRole('button', { name: 'Skip setup', exact: true }))
  await expect(wizard()).toHaveCount(0)
  expect(
    await page.locator('.avalon-shell').evaluate((node) => ({
      hidden: !!node.closest('[aria-hidden="true"], [inert]'),
      pointerEvents: getComputedStyle(node).pointerEvents,
    })),
  ).toEqual({ hidden: false, pointerEvents: 'auto' })
  await page.getByRole('button', { name: 'Winnow home', exact: true }).focus()
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeFocused()
})

for (const [width, height, textScale] of [
  [1920, 1080, 1],
  [1280, 720, 1.4],
] as const)
  test(`fullscreen source setup blocks root routes and provider Back restores every unchanged step at ${width} by ${height} with text ${textScale}`, async () => {
    await page.evaluate(async (scale) => {
      const result = await window.winnow.request({
        route: 'preferences.presentation.put',
        params: { preference: 'FullscreenTextScale' },
        body: { value: String(scale) },
      })
      if (!result.ok) throw Error(result.message)
    }, textScale)
    await surface('fullscreen', width, height)
    await expect
      .poll(() =>
        page.evaluate(() =>
          Number(getComputedStyle(document.documentElement).getPropertyValue('--fullscreen-text-scale')),
        ),
      )
      .toBe(textScale)
    const frame = (await wizard().boundingBox())!
    expect(frame.x).toBeCloseTo(0, 0)
    expect(frame.y).toBeCloseTo(0, 0)
    expect(frame.width).toBeCloseTo(width, 0)
    expect(frame.height).toBeCloseTo(height, 0)
    for (const [selector, source, minimum] of [
      ['.setup-header h2', 64, 42.67],
      ['.setup-header p:last-child', 32, 21.33],
      ['.setup-header .eyebrow', 24, 16],
    ] as const) {
      const size = await wizard()
        .locator(selector)
        .evaluate((node) => Number.parseFloat(getComputedStyle(node).fontSize))
      expect(size).toBeCloseTo(Math.max(minimum, (source * width) / 1920) * textScale, 1)
    }
    const hints = wizard().getByRole('group', { name: 'Setup controls' })
    await expect(hints).toContainText('A Select')
    await expect(hints.locator('[data-setup-glyph="A"] svg')).toBeVisible()
    await expect(hints.locator('[data-setup-glyph="B"]')).toHaveCount(0)
    for (const button of [5, 9, 1]) {
      await tap(button)
      await expect(wizard().getByRole('heading', { name: 'Welcome to Winnow', exact: true })).toBeVisible()
      await expect(page.getByRole('dialog', { name: 'Quick menu', exact: true })).toHaveCount(0)
      expect((await progress()).step).toBe(0)
    }
    await capture(`fullscreen-${width}-setup-welcome-root-blocked`)
    await activate(wizard().getByRole('button', { name: 'Get started', exact: true }), true)
    const providers = [
      ['Set up IGDB metadata', 'IGDB metadata'],
      ['Set up Steam', 'Steam'],
      ['Set up Epic', 'Epic'],
      ['Check GOG Galaxy', 'GOG'],
      ['Choose theme and appearance', 'Appearance'],
      ['Choose app settings', 'Application'],
      ['Choose library settings', 'Library'],
    ] as const
    for (const [index, [label, title]] of providers.entries()) {
      const step = index + 1
      const entry = wizard().getByRole('button', { name: label, exact: true })
      await expect(entry).toBeFocused()
      const heading = await wizard().locator('.setup-header h2').textContent()
      await expect(hints).toContainText('B Previous step')
      await expect(hints.locator('[data-setup-glyph="B"] svg')).toBeVisible()
      if (step <= 4) {
        const status = wizard().locator('.setup-provider-status')
        await expect(status).toBeVisible()
        expect(
          await status.evaluate((node) => Number.parseFloat(getComputedStyle(node).fontSize)),
        ).toBeCloseTo(Math.max(18.67, (28 * width) / 1920) * textScale, 1)
      }
      await tap(0)
      await expect(wizard()).toHaveAttribute('data-setup-provider', String(step))
      await expect(wizard().getByRole('heading', { name: title, exact: true }).first()).toBeVisible()
      await expect(hints).toContainText('B Back')
      const back = wizard().getByRole('button', { name: 'Back to setup', exact: true })
      const box = (await back.boundingBox())!
      expect(box.y + box.height).toBeLessThanOrEqual(height + 1)
      expect(box.x + box.width).toBeLessThanOrEqual(width + 1)
      if (step === 1) {
        const secret = wizard().getByLabel('Client secret', { exact: true })
        await expect(secret).toHaveAttribute('type', 'password')
        await secret.focus()
        await expect(hints).toContainText('Y Keyboard')
        await expect(hints.locator('[data-setup-glyph="Y"] svg')).toBeVisible()
      }
      if ([2, 3, 4].includes(step))
        await expect(wizard().getByRole('heading', { name: 'Platforms', exact: true })).toHaveCount(0)
      if (step === 6) {
        await expect(wizard().getByRole('button', { name: 'Run setup again', exact: true })).toHaveCount(0)
        await expect(wizard().locator('.igdb-connection-panel')).toHaveCount(0)
        for (const name of ['Start in fullscreen', 'Close to tray', 'Minimize to tray'])
          await expect(wizard().getByRole('switch', { name, exact: true })).toBeVisible()
        await expect(wizard().getByRole('button', { name: 'Open links in', exact: true })).toBeVisible()
      }
      if (step === 7) {
        for (const name of [
          'Default library sort',
          'Content age limit',
          'Preferred platform for grouped games',
        ])
          await expect(wizard().getByRole('button', { name, exact: true })).toBeVisible()
        for (const name of ['Non-game entries', 'Group expansions', 'Explicit content'])
          await expect(wizard().getByRole('switch', { name, exact: true })).toBeVisible()
      }
      if (step === 5) {
        for (const name of ['Text size', 'Interface scale', 'Screen margins', 'Cover art'])
          await expect(wizard().getByRole('button', { name, exact: true })).toBeVisible()
        await expect(wizard().getByRole('switch', { name: 'Reduce motion', exact: true })).toBeVisible()
        for (const name of ['Theme', 'Interface font']) {
          const origin = wizard().getByRole('button', { name, exact: true })
          await activate(origin, true)
          const picker = page.getByRole('dialog', { name, exact: true })
          await expect(picker).toBeVisible()
          await expect(picker).toHaveClass(/setup-nested-dialog/)
          await expect(picker).toHaveCSS('z-index', '1021')
          const target = picker.getByRole('button').first()
          await target.focus()
          expect(
            await target.evaluate((node) => {
              const r = node.getBoundingClientRect()
              return !!document
                .elementFromPoint(r.x + r.width / 2, r.y + r.height / 2)
                ?.closest('.setup-nested-dialog')
            }),
          ).toBe(true)
          await capture(`fullscreen-${width}-setup-${name.replaceAll(' ', '-')}-layer`)
          if (name === 'Interface font') {
            const option = picker.locator('.fullscreen-font-options button').first()
            const visibleOption = await option.evaluate((node) => {
              const bounds = node.getBoundingClientRect()
              const body = node.closest('.fullscreen-settings-picker-body')!.getBoundingClientRect()
              const list = node.closest('.fullscreen-font-options')!.getBoundingClientRect()
              return {
                x: bounds.x,
                y: bounds.y,
                right: bounds.right,
                bottom: bounds.bottom,
                width: bounds.width,
                height: bounds.height,
                clipLeft: Math.max(0, body.left, list.left),
                clipTop: Math.max(0, body.top, list.top),
                clipRight: Math.min(innerWidth, body.right, list.right),
                clipBottom: Math.min(innerHeight, body.bottom, list.bottom),
                topmost:
                  document.elementFromPoint(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2) ===
                  node,
              }
            })
            await test.info().attach('font-picker-initial-option-bounds', {
              body: Buffer.from(
                JSON.stringify({ viewportWidth: width, viewportHeight: height, textScale, ...visibleOption }),
              ),
              contentType: 'application/json',
            })
            expect(visibleOption.topmost).toBe(true)
            expect(visibleOption.width).toBeGreaterThan(0)
            expect(visibleOption.height).toBeGreaterThan(0)
            expect(visibleOption.x).toBeGreaterThanOrEqual(visibleOption.clipLeft - 1)
            expect(visibleOption.y).toBeGreaterThanOrEqual(visibleOption.clipTop - 1)
            expect(visibleOption.right).toBeLessThanOrEqual(visibleOption.clipRight + 1)
            expect(visibleOption.bottom).toBeLessThanOrEqual(visibleOption.clipBottom + 1)
            await capture(`fullscreen-${width}-setup-font-picker-options`)
            const family = picker.getByLabel('Font family', { exact: true })
            await family.focus()
            await expect(family).toBeFocused()
            expect(
              await family
                .locator('..')
                .evaluate((node) => Number.parseFloat(getComputedStyle(node).fontSize)),
            ).toBeCloseTo(24 * textScale, 1)
            const pickerHints = picker.getByRole('group', { name: 'Theme picker controls' })
            for (const text of ['A Select', 'B Back', 'Y Keyboard'])
              await expect(pickerHints).toContainText(text)
            await expect(pickerHints.locator('[data-theme-picker-glyph="Y"] svg')).toBeVisible()
            await capture(`fullscreen-${width}-setup-Interface-font-layer`)
            await tap(3)
            const keyboard = page.getByRole('dialog', { name: 'Enter text', exact: true })
            await expect(keyboard).toBeVisible()
            const key = keyboard.getByRole('button', { name: '1', exact: true })
            await expect(key).toBeFocused()
            expect(
              await key.evaluate((node) => {
                const rect = node.getBoundingClientRect()
                return !!document
                  .elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2)
                  ?.closest('.onscreen-keyboard')
              }),
            ).toBe(true)
            await tap(0)
            await expect(keyboard.getByRole('status', { name: 'Current text' })).toHaveText('1')
            await expect(page.locator('.fullscreen-settings-picker input')).toHaveValue('1')
            await capture(`fullscreen-${width}-setup-font-picker-keyboard`)
            await tap(1)
            await expect(keyboard).toHaveCount(0)
            await expect(picker).toBeVisible()
            await expect(family).toBeFocused()
            await expect(family).toHaveValue('1')
            await expect(pickerHints).toContainText('Y Keyboard')
          }
          await tap(1)
          await expect(picker).toHaveCount(0)
          await expect(origin).toBeFocused()
          expect((await progress()).step).toBe(5)
          await expect(wizard()).toHaveAttribute('data-setup-provider', '5')
        }
        const reset = wizard().getByRole('button', { name: 'Reset fullscreen appearance\u2026', exact: true })
        await reset.focus()
        await expect(reset).toBeFocused()
        await expect(hints).toContainText('Y Reset page')
        await expect(hints).toContainText('Left / Right Adjust')
        await expect(hints.locator('[data-setup-glyph="Y"] svg')).toBeVisible()
        await tap(3)
        const confirmation = page.getByRole('alertdialog', {
          name: 'Reset fullscreen appearance?',
          exact: true,
        })
        await expect(confirmation).toBeVisible()
        await expect(confirmation).toHaveClass(/setup-nested-dialog/)
        await expect(confirmation).toHaveCSS('z-index', '1021')
        await capture(`fullscreen-${width}-setup-reset-layer`)
        await tap(1)
        await expect(confirmation).toHaveCount(0)
        await expect(reset).toBeFocused()
        await expect(wizard()).toHaveAttribute('data-setup-provider', '5')
      }
      await capture(`fullscreen-${width}-setup-provider-${step}`)
      await tap(1)
      await expect(wizard()).not.toHaveAttribute('data-setup-provider')
      await expect(wizard().locator('.setup-header h2')).toHaveText(heading!)
      await expect(entry).toBeFocused()
      expect((await progress()).step).toBe(step)
      if (step < 7)
        await activate(wizard().getByRole('button', { name: 'Skip this step', exact: true }), true)
    }
    await activate(wizard().getByRole('button', { name: 'Skip setup', exact: true }), true)
    await expect(wizard()).toHaveCount(0)
    await expect(page.getByRole('navigation', { name: 'Main navigation', exact: true })).toBeVisible()
  })
