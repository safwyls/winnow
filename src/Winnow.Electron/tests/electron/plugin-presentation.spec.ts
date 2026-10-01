import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Locator,
  type Page,
} from '@playwright/test'
import electronPath from 'electron'
import { mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { prebuiltActivationHelper, prebuiltFixture } from './prebuilt-backend'
import { closeFixture } from './fixture-cleanup'
import type { LibraryResponse, PluginSnapshot } from '../../src/renderer/api/types'

let application: ElectronApplication, page: Page, directory: string
let endpoint: { address: string; token: string }
let mode: 'desktop' | 'fullscreen'
const errors: string[] = []
async function control(action: string, body?: unknown): Promise<any> {
  const response = await fetch(new URL(`/__fixture/plugin-provenance/${action}`, endpoint.address), {
    method: body === undefined ? 'GET' : 'POST',
    headers: { Authorization: `Bearer ${endpoint.token}`, 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(15000),
  })
  if (!response.ok) throw Error(`Plugin provenance ${action}: ${response.status}`)
  return response.status === 204 ? null : response.json()
}
async function tap(button: number) {
  for (const pressed of [[], [button], []])
    await page.evaluate(async (pressed) => {
      ;(window as any).__pluginPad.pressed = pressed
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
}
async function activate(target: Locator) {
  await expect(target).toBeEnabled()
  await target.scrollIntoViewIfNeeded()
  await target.focus()
  await expect(target).toBeFocused()
  if (mode === 'fullscreen') await tap(0)
  else
    await page.keyboard.press(
      (await target.evaluate((node) => node instanceof HTMLInputElement && node.type === 'checkbox'))
        ? 'Space'
        : 'Enter',
    )
}
async function navigate(name: string) {
  await activate(
    page.getByRole('navigation', { name: 'Main navigation' }).getByRole('button', { name, exact: true }),
  )
}
async function settings(section: string) {
  await activate(page.getByRole('button', { name: 'Settings', exact: true }))
  await activate(
    page
      .getByRole('navigation', { name: 'Settings section' })
      .getByRole('button', { name: section, exact: true }),
  )
}
const tile = (id: number) => page.locator(`.avalon-library [data-avalon-game="${id}"]`)
const details = () => page.locator('.avalon-details')
async function seed(kind: 'grouped' | 'psn' | 'filter' | 'settings') {
  const state = await control('seed', { kind })
  expect(state.loaded).toBe(true)
  expect(state.isolated).toBe(true)
  await navigate('Library')
  if (kind !== 'settings') await expect(tile(1)).toBeVisible()
}
async function library() {
  return page.evaluate(async () => {
    const response = await window.winnow.request<LibraryResponse>({ route: 'library.get' })
    if (!response.ok || !response.data) throw Error('Library did not publish')
    return response.data
  })
}
async function plugins() {
  return page.evaluate(async () => {
    const response = await window.winnow.request<PluginSnapshot[]>({ route: 'plugins.get' })
    if (!response.ok || !response.data) throw Error('Plugins did not publish')
    return response.data
  })
}
async function nativeState() {
  return application.evaluate(() => (globalThis as any).__pluginPresentation)
}
async function capture(name: string) {
  await page.screenshot({ path: test.info().outputPath(`${name}.png`) })
}
async function mainHints() {
  if (mode !== 'fullscreen') return
  await expect(page.locator('[data-root-bumper="LB"] svg')).toBeVisible()
  await expect(page.locator('[data-root-bumper="RB"] svg')).toBeVisible()
}
async function detailsHints() {
  if (mode === 'fullscreen')
    await expect(page.getByRole('button', { name: 'B · Back to Library', exact: true })).toBeVisible()
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
  directory = await mkdtemp(join(resolve('../..', '.tmp'), `winnow-electron-plugin-provenance-${mode}-`))
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/plugin-presentation-main.mjs'),
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
  expect((await fetch(new URL('/__fixture/plugin-provenance/state', endpoint.address))).status).toBe(401)
  const width = mode === 'fullscreen' || info.title.includes('PlayStation filter') ? 1920 : 1200
  const height = mode === 'desktop' && info.title.includes('grouped Steam primary') ? 800 : 1080
  await application.evaluate(
    ({ BrowserWindow }, size) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setFullScreen(false)
      window.setMinimumSize(0, 0)
      window.setContentSize(size.width, size.height)
      window.isFullScreen = () => size.fullscreen
      window.webContents.send('winnow:fullscreen:changed', size.fullscreen)
      window.focus()
    },
    { width, height, fullscreen: mode === 'fullscreen' },
  )
  await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
  await page.evaluate(() => {
    const state = ((window as any).__pluginPad = { pressed: [] as number[] })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          id: 'Plugin source standard simulated controller',
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
      await test.info().attach('plugin-provenance-backend', {
        body: JSON.stringify(await control('state')),
        contentType: 'application/json',
      })
    if (application)
      await test.info().attach('plugin-provenance-native', {
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

for (const surface of ['desktop', 'fullscreen'] as const) {
  test(`${surface} grouped Steam primary retains one exact Xbox history source summary`, async () => {
    await seed('grouped')
    const games = (await library()).games
    expect(games).toHaveLength(1)
    expect(games[0]).toMatchObject({ workId: 1, title: 'Fixture', playtimeMinutes: 0 })
    expect(
      games[0]!.entries.map((entry) => ({
        ownershipId: entry.ownershipId,
        releaseId: entry.releaseId,
        store: entry.store,
        playtimeMinutes: entry.playtimeMinutes,
      })),
    ).toEqual([
      { ownershipId: 1, releaseId: 1, store: 'steam', playtimeMinutes: 0 },
      { ownershipId: 2, releaseId: 2, store: 'plugin:xbox', playtimeMinutes: 0 },
    ])
    await mainHints()
    await activate(tile(1))
    await expect(details().locator('h1')).toHaveText('Fixture')
    const summary = details().locator('[data-library-source-summary]')
    await expect(summary).toHaveCount(1)
    await expect(summary).toBeVisible()
    await expect(summary).toHaveText('Xbox: Played history — not proof of ownership.')
    await expect(details().locator('.avalon-details-identity > .detail-support').first()).toContainText(
      'Steam',
    )
    await detailsHints()
    await capture(`${surface}-grouped-steam-xbox-source`)
  })

  test(`${surface} PlayStation history preserves sixty minutes source attribution and no Play or Install`, async () => {
    await seed('psn')
    const games = (await library()).games
    expect(games).toHaveLength(1)
    expect(games[0]).toMatchObject({ workId: 1, title: 'Fixture', playtimeMinutes: 60 })
    expect(games[0]!.entries).toHaveLength(1)
    // The persisted ownership contract is boolean. The source's absent Ownership
    // (null install state) is separately exercised at the component boundary.
    expect(games[0]!.entries[0]).toMatchObject({
      ownershipId: 1,
      releaseId: 1,
      store: 'plugin:psn',
      installed: false,
      playtimeMinutes: 60,
    })
    const now = Date.parse((await control('state')).now)
    expect(Date.parse(games[0]!.lastPlayedAt!)).toBe(now - 86400000)
    await tile(1).focus()
    const badge = tile(1).locator('.avalon-store-chips > span[title="PlayStation"]')
    await expect(badge).toHaveText('PLAYSTATION')
    if (surface === 'desktop') await expect(badge).toBeVisible()
    // TV Library deliberately hides portrait captions. The exact badge value
    // survives in the card, while Details presents the readable source label.
    else await expect(badge).toBeHidden()
    await mainHints()
    await capture(`${surface}-playstation-history-badge`)
    await activate(tile(1))
    await expect(details().locator('[data-library-source-summary]')).toHaveText(
      'PlayStation: Played history — not proof of ownership.',
    )
    const store = details().locator('.avalon-details-identity > .detail-support').first()
    await expect(store).toContainText('PlayStation')
    await expect(store).toContainText('Not installed')
    await expect(
      details()
        .locator('.avalon-details-actions')
        .getByRole('button', { name: /^(Play|Launch|Install)(\s|$)/ }),
    ).toHaveCount(0)
    await detailsHints()
    await capture(`${surface}-playstation-history-no-action`)
  })

  test(`${surface} PlayStation filter retains its one matching title and selects only Game 2`, async () => {
    await seed('filter')
    expect((await library()).games.map((game) => game.title).sort()).toEqual(['Game 1', 'Game 2'])
    await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(2)
    await activate(
      page.getByRole('button', { name: surface === 'desktop' ? 'Filters' : 'Filter & sort', exact: true }),
    )
    const panel = page.getByRole(surface === 'desktop' ? 'region' : 'dialog', {
      name: 'Library filters',
      exact: true,
    })
    if (surface === 'fullscreen')
      await activate(panel.getByRole('button', { name: 'PLATFORM · Any', exact: true }))
    else {
      const stores = panel
        .locator('details')
        .filter({ has: page.locator('input[aria-label="PlayStation, 1 matching title"]') })
      if ((await stores.count()) && (await stores.getAttribute('open')) === null)
        await activate(stores.locator('summary'))
    }
    const choice = panel.getByRole(surface === 'desktop' ? 'checkbox' : 'button', {
      name: 'PlayStation, 1 matching title',
      exact: true,
    })
    await activate(choice)
    if (surface === 'fullscreen') {
      await expect(choice).toHaveAttribute('aria-pressed', 'true')
      await tap(3)
    } else {
      await expect(choice).toBeChecked()
      await activate(panel.getByRole('button', { name: 'Close filters' }))
    }
    await expect(panel).toHaveCount(0)
    await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(1)
    await expect(tile(2)).toBeVisible()
    await expect(tile(2)).toHaveAccessibleName(/Game 2/)
    await expect(page.locator('.avalon-results-count')).toContainText('1 game')
    await mainHints()
    await capture(`${surface}-playstation-filter-game-two`)
  })

  test(`${surface} NPSSO exact three fields mask save omit remove and discard with keyboard focus`, async () => {
    await seed('settings')
    // Seed publishes library changes; use the actual preference command to
    // refresh the main process's independently cached link destination too.
    await page.evaluate(async () => {
      const response = await window.winnow.request({
        route: 'preferences.presentation.put',
        params: { preference: 'LinkDestination' },
        body: { value: 'browser' },
      })
      if (!response.ok) throw Error('Could not set fixture link destination')
    })
    await settings('Plugins')
    await activate(page.getByRole('tab', { name: 'PlayStation', exact: true }))
    const card = page.getByRole('region', { name: 'PlayStation settings' })
    const secret = card.getByLabel('PlayStation Sony session token (NPSSO)', { exact: true })
    await expect(secret).toHaveAttribute('type', 'password')
    await expect(secret).toHaveValue('')
    await expect(card.locator('input[type="text"]:visible,input[type="password"]:visible')).toHaveCount(1)
    const provider = (await plugins()).find((plugin) => plugin.id === 'psn')!
    expect(provider.hasAccount).toBe(false)
    expect(provider.settings.map((field) => field.key)).toEqual(['npsso', 'import-history', 'include-legacy'])
    expect(provider.settings[0]).toMatchObject({
      hasStoredSecret: true,
      setupUrl: 'https://ca.account.sony.com/api/v1/ssocookie',
    })
    expect(provider.settings[0]!.value ?? '').toBe('')
    await expect(card.getByRole('button', { name: /Sign (in to|out of) PlayStation/ })).toHaveCount(0)
    await activate(
      card.getByRole('button', { name: 'Get PlayStation Sony session token (NPSSO)', exact: true }),
    )
    await expect
      .poll(async () => (await nativeState()).links)
      .toEqual(['https://ca.account.sony.com/api/v1/ssocookie'])
    await secret.focus()
    if (surface === 'fullscreen') {
      await tap(0)
      const keyboard = page.getByRole('dialog', { name: 'Enter text' })
      await expect(keyboard).toBeVisible()
      await expect(keyboard.getByRole('button', { name: '1', exact: true })).toBeFocused()
      await tap(0)
      await expect(keyboard.getByRole('status', { name: 'Current text' })).toHaveText('•')
      await capture('fullscreen-npsso-masked-keyboard')
      await tap(1)
      await expect(keyboard).toHaveCount(0)
      await expect(secret).toBeFocused()
    }
    await secret.fill('fixture-session-token')
    for (const label of [
      'PlayStation Include played games',
      'PlayStation Include PS3 and PS Vita trophy history',
    ]) {
      const toggle = card.getByLabel(label, { exact: true })
      await expect(toggle).not.toBeChecked()
      await activate(toggle)
      await expect(toggle).toBeChecked()
    }
    const save = card.getByRole('button', { name: 'Save PlayStation settings', exact: true })
    await activate(save)
    await expect(card).toContainText('Refresh queued.')
    await expect(secret).toHaveValue('')
    if (surface === 'fullscreen') await expect(save).toBeFocused()
    await expect
      .poll(async () => (await nativeState()).settings.filter((call: any) => call.path === '/psn/settings'))
      .toHaveLength(1)
    expect(
      (await nativeState()).settings.find((call: any) => call.path === '/psn/settings').body.values,
    ).toEqual({ npsso: 'fixture-session-token', 'import-history': 'true', 'include-legacy': 'true' })
    const saved = await control('state')
    expect(saved.writes).toEqual([
      { npsso: 'fixture-session-token', 'import-history': 'true', 'include-legacy': 'true' },
    ])
    expect(saved.secret).toBe('fixture-session-token')
    expect(saved.refreshRequests).toBeGreaterThanOrEqual(1)
    await mainHints()
    await capture(`${surface}-npsso-saved-empty-secret`)
    await activate(save)
    await expect
      .poll(async () => (await nativeState()).settings.filter((call: any) => call.path === '/psn/settings'))
      .toHaveLength(2)
    expect(
      (await nativeState()).settings.filter((call: any) => call.path === '/psn/settings')[1].body.values,
    ).toEqual({ 'import-history': 'true', 'include-legacy': 'true' })
    await expect(save).toBeEnabled()
    expect((await plugins()).find((plugin) => plugin.id === 'psn')!.settings[0]!.hasStoredSecret).toBe(true)
    const remove = card.getByRole('button', {
      name: 'Remove saved PlayStation Sony session token (NPSSO)',
      exact: true,
    })
    await activate(remove)
    await expect(card).toContainText('Saved secret removed.')
    await expect(remove).toBeDisabled()
    expect((await plugins()).find((plugin) => plugin.id === 'psn')!.settings[0]!.hasStoredSecret).toBe(false)
    expect(
      (await nativeState()).settings.filter((call: any) => call.path === '/psn/secrets/npsso'),
    ).toHaveLength(1)
    await secret.fill('unsaved-session-token')
    await activate(
      page
        .getByRole('navigation', { name: 'Settings section' })
        .getByRole('button', { name: 'Application', exact: true }),
    )
    await expect(card).toHaveCount(0)
    await activate(
      page
        .getByRole('navigation', { name: 'Settings section' })
        .getByRole('button', { name: 'Plugins', exact: true }),
    )
    await activate(page.getByRole('tab', { name: 'PlayStation', exact: true }))
    await expect(secret).toHaveValue('')
    expect((await nativeState()).settings.filter((call: any) => call.path === '/psn/settings')).toHaveLength(
      2,
    )
    expect((await nativeState()).shellAttempts).toEqual([])
    const final = await control('state')
    expect(final.writes).toEqual([
      { npsso: 'fixture-session-token', 'import-history': 'true', 'include-legacy': 'true' },
      { 'import-history': 'true', 'include-legacy': 'true' },
    ])
    expect(final.removedSecrets).toEqual(['npsso'])
    expect(final.storedSecret).toBe(false)
    expect(final.secret).toBeNull()
  })
}
