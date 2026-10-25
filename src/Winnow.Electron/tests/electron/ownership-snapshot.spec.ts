import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Locator,
  type Page,
} from '@playwright/test'
import electronPath from 'electron'
import { mkdtemp, mkdir, readFile, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { prebuiltActivationHelper, prebuiltFixture } from './prebuilt-backend'
import { closeFixture } from './fixture-cleanup'
import { profileDirectory } from '../../src/main/storage'
import { DEFAULT_PROFILE } from '../../src/shared/theme'
import type { LibraryResponse } from '../../src/renderer/api/types'

let application: ElectronApplication, page: Page, directory: string
let endpoint: { address: string; token: string }
let mode: 'desktop' | 'fullscreen'
const errors: string[] = []
async function control(action: string, body?: unknown): Promise<any> {
  const response = await fetch(new URL(`/__fixture/ownership-snapshot/${action}`, endpoint.address), {
    method: body === undefined ? 'GET' : 'POST',
    headers: { Authorization: `Bearer ${endpoint.token}`, 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(15000),
  })
  if (!response.ok) throw Error(`Ownership fixture ${action}: ${response.status}`)
  return response.status === 204 ? null : response.json()
}
async function tap(button: number) {
  for (const pressed of [[], [button], []])
    await page.evaluate(async (pressed) => {
      ;(window as any).__ownershipPad.pressed = pressed
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
}
async function activate(target: Locator) {
  await expect(target).toBeEnabled()
  await target.scrollIntoViewIfNeeded()
  await target.focus()
  await expect(target).toBeFocused()
  if (mode === 'fullscreen') await tap(0)
  else if (await target.evaluate((node) => node instanceof HTMLInputElement && node.type === 'checkbox'))
    await page.keyboard.press('Space')
  else await page.keyboard.press('Enter')
}
async function navigate(name: string) {
  await activate(
    page.getByRole('navigation', { name: 'Main navigation' }).getByRole('button', { name, exact: true }),
  )
}
async function accountSettings() {
  await activate(
    mode === 'desktop'
      ? page.getByRole('button', { name: 'Settings', exact: true })
      : page
          .getByRole('navigation', { name: 'Main navigation' })
          .getByRole('button', { name: 'Settings', exact: true }),
  )
  await activate(
    page
      .getByRole('navigation', { name: 'Settings section' })
      .getByRole('button', { name: 'Library', exact: true }),
  )
  await expect(scope()).toBeVisible()
}
function scope() {
  return page.getByRole(mode === 'desktop' ? 'checkbox' : 'switch', {
    name: 'Show only your account',
    exact: true,
  })
}
async function library() {
  return page.evaluate(async () => {
    const result = await window.winnow.request<LibraryResponse>({ route: 'library.get' })
    if (!result.ok || !result.data) throw Error('Library did not publish')
    return result.data
  })
}
const details = () => page.locator('.avalon-details')
const tile = (id: number) => page.locator(`.avalon-library [data-avalon-game="${id}"]`)
async function openDetails(id: number) {
  await navigate('Library')
  await activate(tile(id))
  await expect(details().locator('h1')).toBeVisible()
}
async function closeDetails(id: number) {
  if (mode === 'fullscreen') await tap(1)
  else await page.keyboard.press('Escape')
  await expect(details()).toHaveCount(0)
  await expect(tile(id)).toBeFocused()
}
async function capture(name: string) {
  await page.screenshot({ path: test.info().outputPath(`${name}.png`) })
}
async function nativeState() {
  return application.evaluate(() => (globalThis as any).__ownershipSnapshot)
}

test.beforeAll(async () => {
  await Promise.all([readFile(prebuiltFixture), readFile(prebuiltActivationHelper)])
})
test.beforeEach(async ({}, info) => {
  application = undefined!
  page = undefined!
  endpoint = undefined!
  errors.length = 0
  mode = info.title.startsWith('fullscreen') ? 'fullscreen' : 'desktop'
  directory = await mkdtemp(join(resolve('../..', '.tmp'), `winnow-electron-ownership-snapshot-${mode}-`))
  const profile = structuredClone(DEFAULT_PROFILE)
  profile.appearance.scale = 100
  profile.appearance.reducedMotion = true
  const profileRoot = profileDirectory(join(directory, 'electron-userdata'), directory)
  await mkdir(profileRoot, { recursive: true })
  await writeFile(join(profileRoot, 'preferences.json'), JSON.stringify(profile))
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/ownership-snapshot-main.mjs'),
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
      WINNOW_BACKEND_PATH: prebuiltFixture,
      WINNOW_ACTIVATION_HELPER_PATH: prebuiltActivationHelper,
    },
    chromiumSandbox: true,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.locator('.avalon-shell')).toBeVisible({ timeout: 30000 })
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
  expect((await fetch(new URL('/__fixture/ownership-snapshot/state', endpoint.address))).status).toBe(401)
  await application.evaluate(({ BrowserWindow }, mode) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setMinimumSize(0, 0)
    window.setContentSize(mode === 'desktop' ? 1000 : 1920, mode === 'desktop' ? 800 : 1080)
    window.isFullScreen = () => mode === 'fullscreen'
    window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
    window.focus()
  }, mode)
  await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
  await page.evaluate(() => {
    const state = ((window as any).__ownershipPad = { pressed: [] as number[] })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          id: 'Ownership standard simulated controller',
          connected: true,
          mapping: 'standard',
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, i) => ({
            pressed: state.pressed.includes(i),
            touched: state.pressed.includes(i),
            value: state.pressed.includes(i) ? 1 : 0,
          })),
        },
      ],
    })
    window.dispatchEvent(new Event('gamepadconnected'))
  })
})
test.afterEach(async () => {
  try {
    if (endpoint)
      await test.info().attach('ownership-snapshot-backend', {
        body: JSON.stringify(await control('state')),
        contentType: 'application/json',
      })
    if (application)
      await test.info().attach('ownership-snapshot-native', {
        body: JSON.stringify(await nativeState()),
        contentType: 'application/json',
      })
    if (test.info().status !== test.info().expectedStatus && page && !page.isClosed())
      await capture('before-teardown')
  } finally {
    await closeFixture(application, directory)
  }
  expect(errors).toEqual([])
})

const blocked =
  'Winnow does not know which Steam account is yours yet. Signing in tells it immediately; an API key finds out at the next Steam import.'
const caveat = "Games Winnow cannot attribute stay visible. Shown playtime becomes your account's."
async function countFigure(count: '1' | '1,234') {
  const row = page.locator('.account-scope-count')
  const value = row.locator('.account-scope-count-value')
  await expect(value).toHaveText(count)
  await expect(row.locator('.account-scope-count-unit')).toHaveText(
    count === '1' ? 'game from other accounts' : 'games from other accounts',
  )
  await row.scrollIntoViewIfNeeded()
  const metrics = await value.evaluate((element) => {
    const style = getComputedStyle(element)
    const token = document.createElement('span')
    token.style.fontFamily = 'var(--font-mono)'
    element.append(token)
    const dataFamily = getComputedStyle(token).fontFamily
    token.remove()
    const bounds = element.getBoundingClientRect()
    return {
      family: style.fontFamily,
      dataFamily,
      variant: style.fontVariantNumeric,
      bounds: bounds.toJSON(),
      visible: bounds.x >= 0 && bounds.right <= innerWidth && bounds.y >= 0 && bounds.bottom <= innerHeight,
    }
  })
  expect(metrics.family).toBe(metrics.dataFamily)
  expect(metrics.variant).toContain('tabular-nums')
  expect(metrics.visible).toBe(true)
  expect(metrics.bounds.width).toBeGreaterThan(0)
  await test
    .info()
    .attach(`account-${count}-data-face`, { body: JSON.stringify(metrics), contentType: 'application/json' })
}

for (const surface of ['desktop', 'fullscreen'] as const) {
  test(`${surface} account scope confirms identity defaults without writing saves reopens filters the real library and renders the source 1234 data figure`, async () => {
    await control('seed', { kind: 'account', otherAccounts: 1234 })
    await accountSettings()
    await expect(scope()).toBeDisabled()
    await expect(scope()).not.toBeChecked()
    await expect(page.getByText(blocked, { exact: true })).toBeVisible()
    await expect(page.locator('.account-scope-count')).toHaveCount(0)
    await expect(page.locator('.account-scope-caveat')).toHaveCount(0)
    expect((await nativeState()).writes).toEqual([])
    const before = await control('state')
    expect(before.confirmedAccount).toBeNull()
    expect(before.accountScope).toBeNull()
    expect(before.workCount).toBe(1235)
    expect(before.ownershipCount).toBe(1235)
    expect(before.completeInventories).toBe(1)
    await test.info().attach('account-default-before-confirm', {
      body: JSON.stringify(before),
      contentType: 'application/json',
    })
    await control('confirm', {})
    await expect(scope()).toBeEnabled()
    await expect(scope()).not.toBeChecked()
    await expect(page.getByText(blocked, { exact: true })).toHaveCount(0)
    await expect(scope()).toHaveAccessibleName('Show only your account')
    await countFigure('1,234')
    const confirmed = await control('state')
    expect(confirmed.confirmedAccount).toBe('11111')
    expect(confirmed.accountScope).toBeNull()
    const all = await library()
    expect(all.games).toHaveLength(1235)
    await activate(scope())
    await expect(scope()).toBeChecked()
    await expect(scope()).toBeEnabled()
    await expect(page.locator('.account-scope-caveat')).toHaveText(caveat)
    expect((await nativeState()).writes).toEqual([{ ownAccountOnly: true }])
    expect((await control('state')).accountScope).toBe('own')
    expect(
      (await control('state')).requests.filter(
        (request: string) => request === 'PUT /api/v1/connections/account-visibility',
      ),
    ).toHaveLength(1)
    await expect.poll(async () => (await library()).games.length).toBe(1)
    await capture(`${surface}-account-scope-1234-data-face`)
    await navigate('Library')
    await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(1)
    const own = (await library()).games[0]!
    expect(own.title).toBe('My game')
    await expect(tile(own.workId)).toBeVisible()
    await capture(`${surface}-account-library-filtered`)
    await accountSettings()
    await expect(scope()).toBeChecked()
    await expect(page.locator('.account-scope-caveat')).toHaveText(caveat)
    expect((await nativeState()).writes).toHaveLength(1)
    await countFigure('1,234')
    expect((await control('state')).accountScope).toBe('own')
    await capture(`${surface}-account-scope-reopened`)
  })

  test(`${surface} exact GOG Cloud Saves notes stay safe with Panzer storefront provenance and missing Epic becomes only its cached Hades link`, async () => {
    await control('seed', { kind: 'storefront' })
    await page.evaluate(async () => {
      const saved = await window.winnow.request({
        route: 'preferences.presentation.put',
        params: { preference: 'LinkDestination' },
        body: { value: 'browser' },
      })
      if (!saved.ok) throw Error(saved.message)
    })
    await openDetails(1)
    await expect(details().locator('h1')).toHaveText('Panzer General 2')
    await activate(details().getByRole('tab', { name: 'Updates', exact: true }))
    const notes = page.locator('.gog-patch-notes-text')
    if (mode === 'desktop') {
      const expander = details().locator('summary', { hasText: 'GOG patch notes' })
      await expect(notes).not.toBeVisible()
      await activate(expander)
    } else {
      const action = details().getByRole('button', { name: 'Patch notes', exact: true })
      await activate(action)
      await expect(page.getByRole('region', { name: 'Patch notes', exact: true })).toBeVisible()
      const hints = page.getByRole('group', { name: 'Patch notes controls', exact: true })
      await expect(hints).toContainText('Read')
      await expect(hints).toContainText('Back')
    }
    await expect(notes).toBeVisible()
    await expect(notes).toContainText('Internal Update')
    await expect(notes).toContainText('Cloud Saves support')
    await expect(notes).not.toContainText('bad()')
    expect(await notes.innerText()).not.toContain('<')
    await expect(notes.locator('script,ul,li,h4')).toHaveCount(0)
    const readingGeometry = await notes.evaluate((element) => {
      const bounds = element.getBoundingClientRect()
      const style = getComputedStyle(element)
      return {
        bounds: bounds.toJSON(),
        fontSize: Number.parseFloat(style.fontSize),
        whiteSpace: style.whiteSpace,
        viewport: { width: innerWidth, height: innerHeight },
      }
    })
    expect(readingGeometry.whiteSpace).toBe('pre-wrap')
    expect(readingGeometry.bounds.x).toBeGreaterThanOrEqual(0)
    expect(readingGeometry.bounds.right).toBeLessThanOrEqual(readingGeometry.viewport.width)
    if (mode === 'fullscreen') {
      expect(readingGeometry.fontSize).toBe(24)
      expect(readingGeometry.bounds.width).toBeLessThanOrEqual(1200)
      await expect(details().getByRole('heading', { name: 'Patch notes', exact: true })).toHaveCSS(
        'font-size',
        '32px',
      )
      await expect(details().getByRole('button', { name: 'Back to Updates', exact: true })).toBeVisible()
    } else expect(readingGeometry.fontSize).toBe(13)
    await test.info().attach('safe-reading-geometry', {
      body: JSON.stringify(readingGeometry),
      contentType: 'application/json',
    })
    await capture(`${surface}-gog-safe-patch-notes`)
    if (mode === 'fullscreen') {
      await tap(13)
      await tap(12)
      await tap(1)
      await expect(page.getByRole('region', { name: 'Patch notes', exact: true })).toHaveCount(0)
      await expect(details().getByRole('button', { name: 'Patch notes', exact: true })).toBeFocused()
    }
    await activate(details().getByRole('tab', { name: 'Library', exact: true }))
    const gog = details().getByRole('button', { name: 'GOG store page', exact: true })
    await expect(gog).toHaveAttribute('title', 'https://www.gog.com/game/panzer_general_2')
    await activate(gog)
    await expect
      .poll(async () => (await nativeState()).dispatches)
      .toEqual(['https://www.gog.com/game/panzer_general_2'])
    await closeDetails(1)
    await openDetails(2)
    await activate(details().getByRole('tab', { name: 'Updates', exact: true }))
    await expect(details().locator('.gog-patch-notes-text')).toHaveCount(0)
    await expect(details().getByRole('button', { name: 'Patch notes', exact: true })).toHaveCount(0)
    await activate(details().getByRole('tab', { name: 'Library', exact: true }))
    await expect(details().getByRole('button', { name: 'Epic Games store page', exact: true })).toHaveCount(0)
    await control('epic-link', {})
    const epic = details().getByRole('button', { name: 'Epic Games store page', exact: true })
    await expect(epic).toHaveAttribute('title', 'https://store.epicgames.com/p/hades')
    await activate(epic)
    await expect
      .poll(async () => (await nativeState()).dispatches)
      .toEqual(['https://www.gog.com/game/panzer_general_2', 'https://store.epicgames.com/p/hades'])
    await capture(`${surface}-epic-cached-storefront`)
    const finalState = await control('state')
    expect(finalState.providerRequests).toEqual([])
    expect(finalState.storefronts['gog:1207658871'].storeUrl).toBe(
      'https://www.gog.com/game/panzer_general_2',
    )
    expect(finalState.storefronts['epic:min'].storeUrl).toBe('https://store.epicgames.com/p/hades')
  })
}
