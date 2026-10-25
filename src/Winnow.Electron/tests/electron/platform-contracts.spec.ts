import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Locator,
  type Page,
} from '@playwright/test'
import electronPath from 'electron'
import { copyFile, mkdtemp, mkdir, readFile, readdir, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { prebuiltActivationHelper, prebuiltFixture } from './prebuilt-backend'
import { closeFixture } from './fixture-cleanup'
import { profileDirectory } from '../../src/main/storage'
import { DEFAULT_PROFILE } from '../../src/shared/theme'

let application: ElectronApplication, page: Page, directory: string
let endpoint: { address: string; token: string }
let mode: 'desktop' | 'fullscreen'
const errors: string[] = []
const blocked =
  'Winnow does not know which Steam account is yours yet. Signing in tells it immediately; an API key finds out at the next Steam import.'
async function control(action: string, body?: unknown): Promise<any> {
  const response = await fetch(new URL(`/__fixture/platform-context/${action}`, endpoint.address), {
    method: body === undefined ? 'GET' : 'POST',
    headers: { Authorization: `Bearer ${endpoint.token}`, 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(15000),
  })
  if (!response.ok) throw Error(`Platform fixture ${action}: ${response.status}`)
  return response.status === 204 ? null : response.json()
}
async function api(route: string): Promise<any> {
  return page.evaluate(async (route) => {
    const response = await window.winnow.request({ route })
    if (!response.ok || !response.data) throw Error(`Failed actual API read: ${route}`)
    return response.data
  }, route)
}
async function tap(button: number) {
  for (const pressed of [[], [button], []])
    await page.evaluate(async (pressed) => {
      ;(window as any).__platformPad.pressed = pressed
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
async function back() {
  if (mode === 'fullscreen') await tap(1)
  else await page.keyboard.press('Escape')
}
async function navigate(name: string) {
  await activate(
    page.getByRole('navigation', { name: 'Main navigation' }).getByRole('button', { name, exact: true }),
  )
}
async function settings(section: string) {
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
      .getByRole('button', { name: section, exact: true }),
  )
}
async function platforms() {
  await settings('Platforms')
}
async function provider(name: 'Steam' | 'Epic' | 'GOG') {
  if (mode === 'fullscreen')
    await activate(page.locator('.fullscreen-platform-summary').getByRole('button', { name, exact: true }))
  else
    await activate(
      page
        .getByRole('navigation', { name: 'Platforms' })
        .getByRole('button', { name: name.toUpperCase(), exact: true }),
    )
  await expect(connection(name)).toBeVisible()
}
const connection = (name: string) => page.getByRole('region', { name: `${name} connection`, exact: true })
async function providerBack(name: string) {
  if (mode === 'fullscreen') {
    await activate(page.getByRole('button', { name: 'Back to Platforms', exact: true }))
    await expect(
      page.locator('.fullscreen-platform-summary').getByRole('button', { name, exact: true }),
    ).toBeFocused()
  }
}
async function keyPage() {
  if (mode === 'fullscreen')
    await activate(page.getByRole('button', { name: 'Steam Web API key', exact: true }))
  await expect(page.getByLabel('Steam Web API key', { exact: true }).and(page.locator('input'))).toBeVisible()
}
async function keyBack() {
  if (mode === 'fullscreen') await activate(page.getByRole('button', { name: 'Back to Steam', exact: true }))
}
const details = () => page.locator('.avalon-details')
const tile = (id: number) => page.locator(`.avalon-library [data-avalon-game="${id}"]`)
async function openDetails(id = 1) {
  await navigate('Library')
  await activate(tile(id))
  await expect(details().locator('h1')).toBeVisible()
}
async function closeDetails(id = 1) {
  await back()
  await expect(details()).toHaveCount(0)
  await expect(tile(id)).toBeFocused()
}
async function spending() {
  await settings(mode === 'desktop' ? 'Spending' : 'Library')
  if (mode === 'fullscreen') await activate(page.getByRole('button', { name: 'Spending', exact: true }))
  await expect(page.getByRole('region', { name: 'Account spending' })).toBeVisible()
}
const nativeState = () => application.evaluate(() => (globalThis as any).__platformContracts)
async function capture(name: string) {
  await page.screenshot({ path: test.info().outputPath(`${name}.png`) })
}
async function visibleWithinClip(target: Locator) {
  await expect(target).toBeVisible()
  const bounds = await target.evaluate((element) => {
    const box = element.getBoundingClientRect()
    let left = 0,
      top = 0,
      right = innerWidth,
      bottom = innerHeight
    for (let parent = element.parentElement; parent; parent = parent.parentElement) {
      const style = getComputedStyle(parent),
        rect = parent.getBoundingClientRect()
      if (/auto|scroll|hidden|clip/.test(style.overflowY)) {
        top = Math.max(top, rect.top)
        bottom = Math.min(bottom, rect.bottom)
      }
      if (/auto|scroll|hidden|clip/.test(style.overflowX)) {
        left = Math.max(left, rect.left)
        right = Math.min(right, rect.right)
      }
    }
    const hit = document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2)
    return {
      visible:
        box.left >= left - 1 && box.top >= top - 1 && box.right <= right + 1 && box.bottom <= bottom + 1,
      hit: hit === element || element.contains(hit),
      box: box.toJSON(),
    }
  })
  expect(bounds.visible).toBe(true)
  expect(bounds.hit).toBe(true)
  return bounds.box
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
  directory = await mkdtemp(join(resolve('../..', '.tmp'), `winnow-electron-platform-context-${mode}-`))
  await mkdir(join(directory, 'documents'), { recursive: true })
  await copyFile(
    resolve('../../tests/fixtures/steam-account-pages/purchase-history.html'),
    join(directory, 'documents/history.html'),
  )
  await writeFile(join(directory, 'documents/unchanged.csv'), 'existing destination bytes')
  const profile = structuredClone(DEFAULT_PROFILE)
  profile.appearance.scale = 100
  profile.appearance.reducedMotion = true
  const profileRoot = profileDirectory(join(directory, 'electron-userdata'), directory)
  await mkdir(profileRoot, { recursive: true })
  await writeFile(join(profileRoot, 'preferences.json'), JSON.stringify(profile))
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/platform-contracts-main.mjs'),
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
  expect((await fetch(new URL('/__fixture/platform-context/state', endpoint.address))).status).toBe(401)
  await application.evaluate(({ BrowserWindow }, mode) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setMinimumSize(0, 0)
    window.setContentSize(1920, 1080)
    window.isFullScreen = () => mode === 'fullscreen'
    window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
    window.focus()
  }, mode)
  await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
  await page.evaluate(() => {
    const state = ((window as any).__platformPad = { pressed: [] as number[] })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          id: 'Platform standard simulated controller',
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
      await test.info().attach('platform-context-backend', {
        body: JSON.stringify(await control('state')),
        contentType: 'application/json',
      })
    if (application)
      await test.info().attach('platform-context-native', {
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

async function libraryAndFeed(count: number) {
  await navigate('Library')
  await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(count)
  await expect(tile(1)).toBeVisible()
  if (count === 2) await expect(tile(2)).toBeVisible()
  else await expect(tile(2)).toHaveCount(0)
  const library = await api('library.get')
  expect(library.games).toHaveLength(count)
  await navigate('For you')
  await expect(page.locator('.avalon-feed-body [data-avalon-game]')).toHaveCount(count)
  if (count === 2) await expect(page.locator('.avalon-feed-body [data-avalon-game="2"]')).toBeVisible()
}
async function status(name: string, text: string, tone: string) {
  const label = connection(name).getByRole('status', { name: text, exact: true })
  await expect(label).toHaveCount(1)
  await expect(label).toBeVisible()
  await expect(label).toHaveAttribute('data-tone', tone)
  const box = await label.boundingBox()
  expect(box!.width).toBeGreaterThan(0)
}
async function signOutEpic(cancel: boolean) {
  const origin = connection('Epic').getByRole('button', { name: 'Sign out of Epic', exact: true })
  await activate(origin)
  const dialog = page.getByRole('dialog', { name: 'Sign out of Epic?', exact: true })
  await expect(dialog).toBeVisible()
  await expect(dialog.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused()
  if (cancel) {
    await back()
    await expect(dialog).toHaveCount(0)
    await expect(origin).toBeFocused()
  } else {
    await activate(dialog.getByRole('button', { name: 'Sign out', exact: true }))
    await expect(dialog).toHaveCount(0)
    await expect(connection('Epic')).toBeVisible()
    await status('Epic', 'NOT SIGNED IN', 'quiet')
  }
}
async function signInEpic(cancel: boolean) {
  await activate(
    connection('Epic').getByRole('button', { name: /^(Connect Epic Games|Sign in to Epic again)$/ }),
  )
  const agreement = page.getByRole('checkbox', { name: 'I agree to connect this account', exact: true })
  await expect(agreement).toBeVisible()
  await activate(agreement)
  await activate(page.getByRole('button', { name: 'Open Epic sign-in window', exact: true }))
  await expect
    .poll(() =>
      application
        .context()
        .pages()
        .some((candidate) => candidate.url().startsWith('https://www.epicgames.com/')),
    )
    .toBe(true)
  const provider = application
    .context()
    .pages()
    .find((candidate) => candidate.url().startsWith('https://www.epicgames.com/'))!
  await expect(provider.getByRole('heading', { name: 'Offline Epic provider fixture' })).toBeVisible()
  if (cancel) {
    await page.bringToFront()
    await activate(connection('Epic').getByRole('button', { name: 'Cancel Epic sign-in', exact: true }))
  } else await provider.getByRole('link', { name: 'Complete fixture sign-in' }).click({ noWaitAfter: true })
  await expect.poll(() => provider.isClosed()).toBe(true)
  await page.bringToFront()
  if (cancel) await expect(connection('Epic')).toContainText('Sign-in cancelled. Nothing was changed.')
}
async function savedPages() {
  if (mode === 'fullscreen') {
    await activate(page.getByRole('button', { name: 'Purchase history', exact: true }))
    await activate(page.getByRole('button', { name: 'Read saved pages', exact: true }))
  } else await activate(page.getByRole('button', { name: 'Import purchase history', exact: true }))
  await expect(page.getByRole('region', { name: 'Steam purchase and licence import' })).toBeVisible()
}
const imports = async () =>
  (await nativeState()).requests.filter(
    (call: any) => call.path.startsWith('/api/v1/imports/steam/') && call.method === 'POST',
  )
async function chooseSavedPage(cancel = false) {
  if (mode === 'desktop') {
    const input = page.getByLabel('Saved Steam pages', { exact: true })
    await expect(input).toHaveAttribute('accept', '.html,.htm')
    if (cancel) await input.dispatchEvent('cancel')
    else await input.setInputFiles(join(directory, 'documents/history.html'))
  } else {
    await activate(page.getByRole('button', { name: 'Choose a page', exact: true }))
    const picker = page.getByRole('dialog', { name: 'Choose a saved Steam page', exact: true })
    await expect(picker).toBeVisible()
    await expect(picker.getByRole('button', { name: 'File unchanged.csv', exact: true })).toHaveCount(0)
    await expect(picker.getByRole('group', { name: 'File chooser controls' })).toBeVisible()
    if (cancel) await tap(1)
    else {
      await expect(picker.getByRole('button', { name: 'Parent folder', exact: true })).toBeFocused()
      await tap(13)
      await expect(picker.getByRole('button', { name: 'File history.html', exact: true })).toBeFocused()
      await tap(0)
    }
    await expect(picker).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Choose a page', exact: true })).toBeFocused()
  }
}
async function closeSavedPages() {
  if (mode === 'desktop') {
    await expect(
      page
        .getByRole('dialog', { name: 'Import Steam purchase history', exact: true })
        .getByRole('button', { name: 'Close', exact: true }),
    ).toBeEnabled()
    await back()
    await expect(
      page.getByRole('dialog', { name: 'Import Steam purchase history', exact: true }),
    ).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Import purchase history', exact: true })).toBeFocused()
  } else {
    await activate(page.getByRole('button', { name: 'Back to Purchase history', exact: true }))
    await expect(page.getByRole('button', { name: 'Read saved pages', exact: true })).toBeFocused()
    await activate(page.getByRole('button', { name: 'Back to Steam', exact: true }))
    await expect(page.getByRole('button', { name: 'Purchase history', exact: true })).toBeFocused()
  }
}
async function exportCsv() {
  if (mode === 'fullscreen') await providerBack('Steam')
  await spending()
  const button = page.getByRole('button', { name: 'Export acquisitions', exact: true })
  const names = await readdir(join(directory, 'documents'))
  await activate(button)
  if (mode === 'fullscreen') {
    const picker = page.getByRole('dialog', { name: 'Export acquisitions', exact: true })
    await expect(picker.getByRole('textbox', { name: 'File name', exact: true })).toHaveValue(
      'winnow-acquisitions.csv',
    )
    await tap(1)
    await expect(picker).toHaveCount(0)
    await expect(button).toBeFocused()
  } else await expect.poll(async () => (await nativeState()).saveDialogs.length).toBe(1)
  expect(await readdir(join(directory, 'documents'))).toEqual(names)
  expect(await readFile(join(directory, 'documents/unchanged.csv'), 'utf8')).toBe(
    'existing destination bytes',
  )
  const destination = join(directory, 'documents/winnow-acquisitions.csv')
  if (mode === 'desktop')
    await application.evaluate((_, path) => {
      ;(globalThis as any).__platformContracts.nextSave = path
    }, destination)
  await activate(button)
  if (mode === 'fullscreen') {
    const picker = page.getByRole('dialog', { name: 'Export acquisitions', exact: true })
    await expect(picker.getByRole('textbox', { name: 'File name', exact: true })).toHaveValue(
      'winnow-acquisitions.csv',
    )
    await expect(picker.getByRole('group', { name: 'File chooser controls' })).toBeVisible()
    await activate(picker.getByRole('button', { name: 'Save here', exact: true }))
    await expect(picker).toHaveCount(0)
  }
  const expected = await api('acquisitions.export')
  await expect(
    page.getByText(
      `Exported ${expected.ownershipCount.toLocaleString()} ownership ${expected.ownershipCount === 1 ? 'record' : 'records'}.`,
      { exact: true },
    ),
  ).toBeVisible()
  const bytes = await readFile(destination)
  expect([...bytes.subarray(0, 3)]).toEqual([0xef, 0xbb, 0xbf])
  expect(bytes.subarray(3).equals(Buffer.from(expected.content, 'utf8'))).toBe(true)
  expect(bytes.subarray(3, 6).equals(Buffer.from([0xef, 0xbb, 0xbf]))).toBe(false)
  if (mode === 'desktop')
    for (const options of (await nativeState()).saveDialogs) {
      expect(options.title).toBe('Export acquisitions')
      expect(options.defaultPath).toBe('winnow-acquisitions.csv')
      expect(options.filters).toEqual([{ name: 'CSV', extensions: ['csv'] }])
    }
  await capture(`${mode}-real-api-export-complete`)
}
for (const surface of ['desktop', 'fullscreen'] as const) {
  test(`${surface} masked key draft disposal replacement confirmation reset and explicit saved HTML Read preserve export BOM and cancellation`, async () => {
    test.setTimeout(120000)
    await control('seed', { kind: 'key' })
    expect((await control('state')).confirmedAccount).toBe('10001')
    await platforms()
    await provider('Steam')
    await keyPage()
    let field = page.getByLabel('Steam Web API key', { exact: true }).and(page.locator('input'))
    await expect(field).toHaveAttribute('type', 'password')
    await field.fill('unsaved-local-draft')
    expect((await control('state')).keyWrites).toBe(1)
    if (mode === 'fullscreen') {
      await keyBack()
      await keyPage()
    } else {
      await settings('Application')
      await platforms()
      await provider('Steam')
    }
    field = page.getByLabel('Steam Web API key', { exact: true }).and(page.locator('input'))
    await expect(field).toHaveValue('')
    await field.fill('replacement-test-key')
    await activate(page.getByRole('button', { name: 'Save API key', exact: true }))
    await expect(page.getByText('API key saved securely.', { exact: true })).toBeVisible()
    await expect.poll(async () => (await control('state')).confirmedAccount).toBe('')
    expect((await api('connections.visibility.get')).accountConfirmed).toBe(false)
    expect((await control('state')).keyPresent).toBe(true)
    await keyBack()
    const toggle = page.getByRole(mode === 'desktop' ? 'checkbox' : 'switch', {
      name: 'Show only your account',
      exact: true,
    })
    await expect(toggle).toBeDisabled()
    await expect(connection('Steam')).toContainText(
      'Your API key is set, but Winnow has not confirmed which account it belongs to yet. This happens automatically during the next Steam import.',
    )
    await keyPage()
    await activate(page.getByRole('button', { name: 'Remove saved API key', exact: true }))
    await expect.poll(async () => (await control('state')).keyPresent).toBe(false)
    await keyBack()
    await expect(toggle).toBeDisabled()
    await expect(connection('Steam')).toContainText(blocked)
    await savedPages()
    await chooseSavedPage(true)
    await expect(
      page.getByText(
        mode === 'fullscreen'
          ? 'No additional page was selected. Nothing was imported.'
          : 'No pages were selected. Nothing was imported.',
        { exact: true },
      ),
    ).toBeVisible()
    expect(await imports()).toEqual([])
    await chooseSavedPage()
    await expect(page.getByRole('list', { name: 'Selected Steam pages' })).toHaveText('history.html')
    expect(await imports()).toEqual([])
    if (mode === 'fullscreen')
      expect((await nativeState()).savedPages.filter((call: any) => call.channel.endsWith(':read'))).toEqual(
        [],
      )
    await activate(page.getByRole('button', { name: 'Clear selection', exact: true }))
    await expect(page.getByRole('list', { name: 'Selected Steam pages' })).toHaveCount(0)
    await expect(
      page.getByRole('button', { name: mode === 'fullscreen' ? 'Read selected pages' : 'Read', exact: true }),
    ).toBeDisabled()
    await chooseSavedPage()
    await closeSavedPages()
    await savedPages()
    await expect(page.getByRole('list', { name: 'Selected Steam pages' })).toHaveCount(0)
    expect(await imports()).toEqual([])
    await chooseSavedPage()
    await capture(`${surface}-selected-html-before-read`)
    await activate(
      page.getByRole('button', { name: mode === 'fullscreen' ? 'Read selected pages' : 'Read', exact: true }),
    )
    await expect
      .poll(async () => (await imports()).map((call: any) => call.path))
      .toEqual(['/api/v1/imports/steam/load-files', '/api/v1/imports/steam/pages'])
    await expect(page.locator('.steam-import-report')).toBeVisible()
    await closeSavedPages()
    await exportCsv()
  })
  test(`${surface} platform summary neutral statuses Epic replacement and expired cancellation preserve current identity and Steam health`, async () => {
    test.setTimeout(120000)
    await control('seed', { kind: 'platforms' })
    await platforms()
    const before = (await control('state')).requests.filter(
      (value: string) => value === 'GET /api/v1/connections/stores',
    ).length
    await settings('Application')
    await platforms()
    if (mode === 'fullscreen')
      await expect
        .poll(
          async () =>
            (await control('state')).requests.filter(
              (value: string) => value === 'GET /api/v1/connections/stores',
            ).length,
        )
        .toBeGreaterThan(before)
    if (mode === 'fullscreen') {
      const action = page
        .locator('.fullscreen-platform-summary')
        .getByRole('button', { name: 'Epic', exact: true })
      await expect(action).toContainText('SIGNED IN')
      await expect(action.locator('.fullscreen-setting-cue')).toHaveText('Open ›')
      await expect(action.locator('.fullscreen-setting-cue > span')).toHaveCount(1)
    }
    await provider('Epic')
    await expect(connection('Epic')).toContainText('Account A')
    await status('Epic', 'SIGNED IN', 'live')
    await signOutEpic(true)
    await expect(connection('Epic')).toContainText('Account A')
    await signOutEpic(false)
    await expect(connection('Epic')).not.toContainText('Account A')
    await signInEpic(false)
    await expect(connection('Epic')).toContainText('Account B')
    await expect(connection('Epic')).not.toContainText('Account A')
    await control('change', { stage: 'epic-expired' })
    await status('Epic', 'SESSION EXPIRED', 'attention')
    await expect(connection('Epic')).toContainText('Test player')
    const exchanged = (await control('state')).epicExchanges
    await signInEpic(true)
    await status('Epic', 'SESSION EXPIRED', 'attention')
    await expect(connection('Epic')).toContainText('Test player')
    expect((await control('state')).epicExchanges).toBe(exchanged)
    await control('change', { stage: 'epic-next-new' })
    await signOutEpic(false)
    await signInEpic(false)
    await expect(connection('Epic')).toContainText('New test player')
    await capture(`${surface}-epic-current-identity`)
    await signOutEpic(false)
    await status('Epic', 'NOT SIGNED IN', 'quiet')
    await providerBack('Epic')
    if (mode === 'fullscreen') {
      const action = page
        .locator('.fullscreen-platform-summary')
        .getByRole('button', { name: 'Steam', exact: true })
      await action.focus()
      const same = await action.elementHandle()
      await control('change', { stage: 'steam-live' })
      await expect(action).toContainText('SIGNED IN')
      await expect(action).toBeFocused()
      expect(await same!.evaluate((node) => node.isConnected)).toBe(true)
    } else await control('change', { stage: 'steam-live' })
    await provider('Steam')
    await status('Steam', 'SIGNED IN', 'live')
    await expect(connection('Steam')).toContainText('76561198000000000')
    await expect(
      connection('Steam').getByRole('button', { name: 'Sign out of Steam', exact: true }),
    ).toBeVisible()
    await control('change', { stage: 'steam-expired' })
    await status('Steam', 'SIGN-IN EXPIRED', 'attention')
    await expect(
      connection('Steam').getByRole('button', { name: 'Sign in again', exact: true }),
    ).toBeVisible()
    await control('change', { stage: 'steam-none' })
    await status('Steam', 'NO CONNECTION', 'quiet')
    await expect(connection('Steam')).not.toContainText('76561198000000000')
    await expect(
      connection('Steam').getByRole('button', { name: 'Sign out of Steam', exact: true }),
    ).toHaveCount(0)
    await providerBack('Steam')
    await provider('GOG')
    await status('GOG', 'Not needed', 'live')
    await expect(connection('GOG').getByLabel('Needs attention')).toHaveCount(0)
    await capture(`${surface}-optional-gog-neutral`)
    expect((await nativeState()).forbidden).toEqual([])
  })
  test(`${surface} exact incomplete complete then failed Steam inventory restores household Library and Feed visibility`, async () => {
    await control('seed', { kind: 'inventory', complete: false })
    await libraryAndFeed(2)
    await control('change', { stage: 'complete' })
    await libraryAndFeed(1)
    await control('change', { stage: 'failed' })
    await libraryAndFeed(2)
    await settings('Library')
    await expect(
      page.getByRole(mode === 'desktop' ? 'checkbox' : 'switch', {
        name: 'Show only your account',
        exact: true,
      }),
    ).toBeChecked()
    await expect(page.locator('.account-scope-count')).toHaveCount(0)
    await capture(`${surface}-failed-inventory-keeps-household`)
  })

  for (const store of ['epic', 'gog'] as const)
    test(`${surface} ${store} persisted unknown install keeps Play and path on the same Details until authoritative absence`, async () => {
      await control('seed', { kind: 'install', store })
      await openDetails()
      const same = await details().elementHandle()
      const primary = details().locator('.avalon-details-actions .primary-button')
      await expect(primary).toContainText('Play')
      await control('change', { stage: 'unknown' })
      await expect
        .poll(
          async () => (await control('state')).ownerships.find((entry: any) => entry.id === 1).installPath,
        )
        .toBe('C:\\Fixture\\Game')
      await expect(primary).toContainText('Play')
      expect(
        await same!.evaluate(
          (node) => node.isConnected && node === document.querySelector('.avalon-details'),
        ),
      ).toBe(true)
      expect((await control('state')).ownerships.find((entry: any) => entry.id === 1).installPath).toBe(
        'C:\\Fixture\\Game',
      )
      await control('change', { stage: 'absent' })
      await expect(primary).toContainText('Install')
      expect(
        await same!.evaluate(
          (node) => node.isConnected && node === document.querySelector('.avalon-details'),
        ),
      ).toBe(true)
      expect((await control('state')).ownerships.find((entry: any) => entry.id === 1).installPath).toBeNull()
      await capture(`${surface}-${store}-authoritative-install-absence`)
      expect((await nativeState()).dispatches).toEqual([])
    })

  test(`${surface} selected 2024 gift becomes aggregate 2020 without licence and ambiguous three-account spending has no money total`, async () => {
    await control('seed', { kind: 'acquisition' })
    await openDetails()
    const same = await details().elementHandle()
    await activate(details().getByRole('tab', { name: 'Library', exact: true }))
    await expect(details().locator('.acquisition-facts')).toContainText('2024')
    await expect(details().locator('.acquisition-facts')).toContainText('Gift')
    await details().locator('.acquisition-facts').scrollIntoViewIfNeeded()
    await expect(details().locator('.acquisition-facts').getByText(/2024/)).toBeVisible()
    await expect(
      details().locator('.acquisition-facts').getByText('Gift or guest pass', { exact: true }),
    ).toBeVisible()
    await control('change', { stage: 'all' })
    await expect(details().locator('.acquisition-facts')).toContainText('2020')
    await expect(details().locator('.acquisition-facts')).not.toContainText('Gift')
    await expect(details().locator('.acquisition-facts')).not.toContainText('Retail')
    await details().locator('.acquisition-facts').scrollIntoViewIfNeeded()
    await expect(details().locator('.acquisition-facts').getByText(/2020/)).toBeVisible()
    expect(
      await same!.evaluate((node) => node.isConnected && node === document.querySelector('.avalon-details')),
    ).toBe(true)
    await capture(`${surface}-aggregate-acquisition`)
    await closeDetails()
    await spending()
    const stats = page.getByRole('region', { name: 'Account spending' })
    await expect(stats).toContainText('2 identified')
    await expect(stats).toContainText('unknown account')
    await expect(stats).not.toContainText('$15.00')
    await expect(stats.locator('.account-chart strong')).toHaveCount(0)
    await capture(`${surface}-ambiguous-account-spending`)
  })
}

async function verticalRoute(scope: Locator) {
  const controls = scope
    .locator('button:enabled, input:enabled, select:enabled, textarea:enabled, summary')
    .filter({ visible: true })
  const count = await controls.count()
  expect(count).toBeGreaterThanOrEqual(3)
  await controls.first().scrollIntoViewIfNeeded()
  await controls.first().focus()
  for (let index = 0; index < count; index++) {
    const current = controls.nth(index)
    await expect(current).toBeFocused()
    await tap(15)
    await expect(current).toBeFocused()
    if (index + 1 < count) {
      await tap(13)
      const next = controls.nth(index + 1)
      await expect(next).toBeFocused()
      const previousBox = await current.boundingBox(),
        nextBox = await next.boundingBox()
      expect(nextBox!.y).toBeGreaterThan(previousBox!.y)
      await visibleWithinClip(next)
    }
  }
  for (let index = count - 2; index >= 0; index--) {
    await tap(12)
    await expect(controls.nth(index)).toBeFocused()
  }
}
test('fullscreen 720p 140 percent platform tools retain vertical focus masked Y keyboard and one-layer Back', async () => {
  test.setTimeout(120000)
  await control('seed', { kind: 'key' })
  await application.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0]!.setContentSize(1280, 720),
  )
  await page.evaluate(async () => {
    const result = await window.winnow.request({
      route: 'preferences.presentation.put',
      params: { preference: 'FullscreenTextScale' },
      body: { value: '1.4' },
    })
    if (!result.ok) throw Error('Could not save source text scale')
  })
  await platforms()
  for (const bumper of ['LB', 'RB']) {
    const hint = page
      .getByRole('navigation', { name: 'Main navigation' })
      .locator(`[data-root-bumper="${bumper}"]`)
    await expect(hint).toBeVisible()
    await expect(hint.locator('svg')).toBeVisible()
  }
  await capture('fullscreen-720-platform-summary-bumper-hints')
  await provider('Steam')
  await activate(page.getByRole('button', { name: 'Sign in to Steam', exact: true }))
  const consent = page.getByRole('dialog', { name: 'Before you sign in', exact: true })
  await expect(consent).toBeVisible()
  await expect(
    consent.getByRole('checkbox', { name: 'Also capture purchase history and licences', exact: true }),
  ).not.toBeChecked()
  await verticalRoute(consent)
  await capture('fullscreen-720-steam-consent-vertical')
  await tap(1)
  await expect(consent).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Sign in to Steam', exact: true })).toBeFocused()
  await keyPage()
  const field = page.getByLabel('Steam Web API key', { exact: true }).and(page.locator('input'))
  await expect(field).toHaveAttribute('type', 'password')
  expect(await field.evaluate((node) => Number.parseFloat(getComputedStyle(node).fontSize))).toBeCloseTo(
    1280 * (28 / 1920) * 1.4,
    1,
  )
  await field.fill('unsaved-local-draft')
  await verticalRoute(page.locator('.fullscreen-platform-content'))
  await field.fill('')
  await field.focus()
  await field.scrollIntoViewIfNeeded()
  const hints = page.getByRole('group', { name: 'Platform controls', exact: true })
  await expect(hints).toContainText('Y Keyboard')
  for (const key of ['A', 'B', 'Y'])
    await expect(hints.locator(`[data-platform-glyph="${key}"] svg`)).toBeVisible()
  await visibleWithinClip(field)
  await capture('fullscreen-720-key-field-local-hints')
  await tap(3)
  const keyboard = page.getByRole('dialog', { name: 'Enter text', exact: true })
  await expect(keyboard).toBeVisible()
  const key = keyboard.getByRole('button', { name: '1', exact: true })
  await expect(key).toBeFocused()
  await visibleWithinClip(key)
  await tap(0)
  await expect(field).toHaveValue('1')
  await expect(keyboard.getByRole('status', { name: 'Current text' })).not.toContainText('1')
  await capture('fullscreen-720-key-masked-keyboard')
  await tap(1)
  await expect(keyboard).toHaveCount(0)
  await expect(field).toBeFocused()
  await keyBack()
  await keyPage()
  await expect(field).toHaveValue('')
  expect((await control('state')).keyWrites).toBe(1)
  await keyBack()
  await savedPages()
  await chooseSavedPage()
  await verticalRoute(page.locator('.fullscreen-platform-content'))
  await capture('fullscreen-720-saved-page-vertical')
  await activate(page.getByRole('button', { name: 'Back to Purchase history', exact: true }))
  await expect(page.getByRole('button', { name: 'Read saved pages', exact: true })).toBeFocused()
  await activate(page.getByRole('button', { name: 'Read saved pages', exact: true }))
  await expect(page.getByRole('list', { name: 'Selected Steam pages' })).toHaveCount(0)
  expect(await imports()).toEqual([])
  await expect(page.locator('.fullscreen-platform-page')).toBeVisible()
})
