import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Locator,
  type Page,
} from '@playwright/test'
import electronPath from 'electron'
import { mkdtemp, mkdir, readFile, readdir, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { prebuiltActivationHelper, prebuiltFixture } from './prebuilt-backend'
import { closeFixture } from './fixture-cleanup'
import { profileDirectory } from '../../src/main/storage'
import { DEFAULT_PROFILE } from '../../src/shared/theme'

let application: ElectronApplication, page: Page, directory: string
let endpoint: { address: string; token: string }
let mode: 'desktop' | 'fullscreen'
const errors: string[] = []
const csvHeader =
  'schema_version,ownership_id,release_id,title,store,acquired_at,license_type,price_paid_cents,price_source,account_ref\r\n'
async function control(action: string, body?: unknown): Promise<any> {
  const response = await fetch(new URL(`/__fixture/steam-account-reading/${action}`, endpoint.address), {
    method: body === undefined ? 'GET' : 'POST',
    headers: { Authorization: `Bearer ${endpoint.token}`, 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(15000),
  })
  if (!response.ok) throw Error(`Steam account fixture ${action}: ${response.status}`)
  return response.status === 204 ? null : response.json()
}
async function api(route: string, body?: unknown, params?: Record<string, string | number>): Promise<any> {
  return page.evaluate(
    async ({ route, body, params }) => {
      const response = await window.winnow.request({ route, params, ...(body === undefined ? {} : { body }) })
      if (!response.ok || !response.data) throw Error(`Failed actual API: ${route}`)
      return response.data
    },
    { route, body, params },
  )
}
const nativeState = () =>
  application.evaluate(() => {
    const { releaseWrite: _release, ...state } = (globalThis as any).__steamAccountReading
    return state
  })
async function tap(button: number) {
  for (const pressed of [[], [button], []])
    await page.evaluate(async (pressed) => {
      ;(window as any).__readingPad.pressed = pressed
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
}
async function activate(target: Locator) {
  await expect(target).toBeEnabled()
  await target.scrollIntoViewIfNeeded()
  await target.focus()
  await expect(target).toBeFocused()
  if (mode === 'fullscreen') await tap(0)
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
async function spending() {
  await settings(mode === 'desktop' ? 'Spending' : 'Library')
  if (mode === 'fullscreen') await activate(page.getByRole('button', { name: 'Spending', exact: true }))
  await expect(page.getByRole('region', { name: 'Account spending' })).toBeVisible()
}
async function capture(name: string) {
  await page.screenshot({ path: test.info().outputPath(`${mode}-${name}.png`) })
}
async function size(width: number, height: number) {
  await application.evaluate(
    ({ BrowserWindow }, dimensions) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setMinimumSize(0, 0)
      window.setContentSize(dimensions.width, dimensions.height)
    },
    { width, height },
  )
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
  directory = await mkdtemp(join(resolve('../..', '.tmp'), `winnow-electron-steam-account-reading-${mode}-`))
  await mkdir(join(directory, 'docs'), { recursive: true })
  await writeFile(join(directory, 'docs/unchanged.csv'), 'existing destination bytes')
  const profile = structuredClone(DEFAULT_PROFILE)
  profile.appearance.scale = 100
  profile.appearance.reducedMotion = true
  const profileRoot = profileDirectory(join(directory, 'electron-userdata'), directory)
  await mkdir(profileRoot, { recursive: true })
  await writeFile(join(profileRoot, 'preferences.json'), JSON.stringify(profile))
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/steam-account-reading-main.mjs'),
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
  expect((await fetch(new URL('/__fixture/steam-account-reading/state', endpoint.address))).status).toBe(401)
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
    const state = ((window as any).__readingPad = { pressed: [] as number[] })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          id: 'Steam account standard simulated controller',
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
    if (application)
      await application
        .evaluate(() => (globalThis as any).__steamAccountReading.releaseWrite?.())
        .catch(() => {})
    if (endpoint)
      await test.info().attach('steam-account-backend', {
        body: JSON.stringify(await control('state')),
        contentType: 'application/json',
      })
    if (application)
      await test.info().attach('steam-account-native', {
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
async function exportTo(button: Locator, filename: string | null, outcome?: 'hold' | 'fail') {
  await application.evaluate(
    (_, input) => {
      const state = (globalThis as any).__steamAccountReading
      state.nextSave = input.path
      state.holdNextWrite = input.outcome === 'hold'
      state.failNextWrite = input.outcome === 'fail'
    },
    { path: filename ? join(directory, 'docs', filename) : null, outcome },
  )
  await activate(button)
  if (mode === 'fullscreen') {
    const picker = page.getByRole('dialog', { name: 'Export acquisitions', exact: true })
    await expect(picker.getByRole('textbox', { name: 'File name', exact: true })).toHaveValue(
      'winnow-acquisitions.csv',
    )
    await expect(picker.getByRole('group', { name: 'File chooser controls' })).toBeVisible()
    if (!filename) await back()
    else {
      await picker.getByRole('textbox', { name: 'File name', exact: true }).fill(filename)
      await activate(picker.getByRole('button', { name: 'Save here', exact: true }))
    }
    await expect(picker).toHaveCount(0)
  }
}
async function exactBytes(filename: string, expected: { content: string; ownershipCount: number }) {
  const bytes = await readFile(join(directory, 'docs', filename))
  expect([...bytes.subarray(0, 3)]).toEqual([239, 187, 191])
  expect(bytes.subarray(3).equals(Buffer.from(expected.content, 'utf8'))).toBe(true)
  expect(expected.content.startsWith(csvHeader)).toBe(true)
  expect((await nativeState()).exports.at(-1).result).toEqual({
    saved: true,
    ownershipCount: expected.ownershipCount,
  })
}
async function savedPages() {
  await settings('Platforms')
  if (mode === 'fullscreen') {
    await activate(
      page.locator('.fullscreen-platform-summary').getByRole('button', { name: 'Steam', exact: true }),
    )
    await activate(page.getByRole('button', { name: 'Purchase history', exact: true }))
    await activate(page.getByRole('button', { name: 'Read saved pages', exact: true }))
  } else {
    await activate(
      page.getByRole('navigation', { name: 'Platforms' }).getByRole('button', { name: 'STEAM', exact: true }),
    )
    await activate(page.getByRole('button', { name: 'Import purchase history', exact: true }))
  }
  await expect(page.getByRole('region', { name: 'Steam purchase and licence import' })).toBeVisible()
}
async function choosePages(names: string[]) {
  if (mode === 'desktop') {
    const input = page.getByLabel('Saved Steam pages', { exact: true })
    await expect(input).toHaveAttribute('accept', '.html,.htm')
    await input.setInputFiles(names.map((name) => join(directory, 'docs', name)))
  } else
    for (const name of names) {
      await activate(page.getByRole('button', { name: 'Choose a page', exact: true }))
      const picker = page.getByRole('dialog', { name: 'Choose a saved Steam page', exact: true })
      await expect(picker.getByRole('group', { name: 'File chooser controls' })).toBeVisible()
      await activate(picker.getByRole('button', { name: `File ${name}`, exact: true }))
      await expect(picker).toHaveCount(0)
      await expect(page.getByRole('button', { name: 'Choose a page', exact: true })).toBeFocused()
    }
}
const readPages = () =>
  activate(
    page.getByRole('button', { name: mode === 'fullscreen' ? 'Read selected pages' : 'Read', exact: true }),
  )
async function closePages() {
  await expect(page.getByText('Reading and importing your capture\u2026', { exact: true })).toHaveCount(0)
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
    await activate(page.getByRole('button', { name: 'Back to Steam', exact: true }))
    await activate(page.getByRole('button', { name: 'Back to Platforms', exact: true }))
  }
}
async function steamTypography(projection: Locator, host: string) {
  const metrics = await projection.evaluate((root, host) => {
    const selectors = [
      '.feature-heading h2',
      '.feature-heading p',
      '.steam-observation h3',
      '.steam-observation strong',
      '.steam-observation p.muted',
      '.steam-observation-bounds',
      '.steam-observation time',
      '.feature-heading button',
    ]
    return selectors.map((selector) => {
      const node =
        selector === '.feature-heading h2' && host === 'per-game'
          ? root.closest('[role="dialog"]')?.querySelector<HTMLElement>(':scope > header h2')
          : root.querySelector<HTMLElement>(selector)
      if (!node) throw Error(`Missing ${host} Steam typography element: ${selector}`)
      const range = document.createRange()
      range.selectNodeContents(node)
      return {
        selector,
        font: Number.parseFloat(getComputedStyle(node).fontSize),
        zoom: Number.parseFloat(getComputedStyle(document.body).zoom),
        paintedTextHeight: range.getBoundingClientRect().height,
      }
    })
  }, host)
  await test
    .info()
    .attach(`${host}-steam-typography`, { body: JSON.stringify(metrics), contentType: 'application/json' })
  // The responsive 1.458vw action role computes 27.9936px at the 1920px reference.
  // Preserve the role values within that authored subpixel rounding.
  for (const [index, expected] of [32, 24, 32, 24, 24, 22, 22, 28].entries())
    expect(metrics[index]!.font).toBeCloseTo(expected, 1)
  for (const item of metrics)
    expect(item.paintedTextHeight).toBeGreaterThanOrEqual(item.font * item.zoom * 0.8)
}

for (const surface of ['desktop', 'fullscreen'] as const) {
  test(`${surface} Library acquisition export preserves exact three-owner quoted CSV and retained Accounts entry`, async () => {
    await control('seed', { kind: 'export' })
    const expected = await api('acquisitions.export')
    expect(expected.ownershipCount).toBe(3)
    expect(expected.content).toContain('"A, ""game""\r\npart two"')
    expect(expected.content).toContain(
      '"steam","2024-02-03T00:00:00.0000000Z","purchase","1299","steam_purchase_history",\r\n',
    )
    expect(expected.content).toContain('"gog",,,"0",,\r\n')
    expect(expected.content).toContain('"epic",,,,,\r\n')
    await settings('Library')
    await exportTo(page.getByRole('button', { name: 'Export acquisition CSV', exact: true }), 'library.csv')
    await expect(page.getByText('Exported 3 ownership records.', { exact: true })).toBeVisible()
    await exactBytes('library.csv', expected)
    if (mode === 'fullscreen') {
      const layout = await page.locator('.settings-page').evaluate((root) => {
        const elements = [root, ...root.querySelectorAll<HTMLElement>('*')]
        return elements
          .filter((element) => /auto|scroll/.test(getComputedStyle(element).overflowX))
          .map((element) => ({
            selector: `${element.tagName}.${element.className}`,
            clientWidth: element.clientWidth,
            scrollWidth: element.scrollWidth,
            bounds: element.getBoundingClientRect().toJSON(),
            oversized: [...element.children]
              .filter(
                (child) => child.getBoundingClientRect().right > element.getBoundingClientRect().right + 1,
              )
              .map((child) => ({
                selector: `${child.tagName}.${child.className}`,
                bounds: child.getBoundingClientRect().toJSON(),
              })),
          }))
      })
      await test.info().attach('fullscreen-library-horizontal-layout', {
        body: JSON.stringify(layout),
        contentType: 'application/json',
      })
      expect(layout.filter((element) => element.scrollWidth > element.clientWidth + 1)).toEqual([])
    }
    await capture('library-export-three-records')
    await spending()
    await exportTo(page.getByRole('button', { name: 'Export acquisitions', exact: true }), 'accounts.csv')
    await expect(page.getByText('Exported 3 ownership records.', { exact: true })).toBeVisible()
    await exactBytes('accounts.csv', expected)
    expect((await api('acquisitions.export')).content).toBe(expected.content)
    await capture('accounts-export-retained')
  })
  test(`${surface} empty acquisition export reports held saved zero cancelled and safe failed destinations`, async () => {
    await control('seed', { kind: 'empty' })
    const expected = await api('acquisitions.export')
    expect(expected).toMatchObject({ ownershipCount: 0, content: csvHeader })
    await settings('Library')
    const button = page.getByRole('button', { name: 'Export acquisition CSV', exact: true })
    await exportTo(button, 'empty.csv', 'hold')
    await expect.poll(async () => (await nativeState()).writes.length).toBe(1)
    await expect(page.getByText('Preparing acquisition CSV\u2026', { exact: true })).toBeVisible()
    await expect(button).toBeDisabled()
    expect(await readdir(join(directory, 'docs'))).not.toContain('empty.csv')
    await capture('export-pending')
    await application.evaluate(() => (globalThis as any).__steamAccountReading.releaseWrite())
    await expect(page.getByText('Exported 0 ownership records.', { exact: true })).toBeVisible()
    await expect(button).toBeEnabled()
    await exactBytes('empty.csv', expected)
    const priorFiles = await readdir(join(directory, 'docs'))
    await exportTo(button, null)
    await expect(page.getByText('Export cancelled.', { exact: true })).toBeVisible()
    expect((await nativeState()).exports.at(-1).result).toEqual({ saved: false, ownershipCount: 0 })
    expect(await readdir(join(directory, 'docs'))).toEqual(priorFiles)
    await exportTo(button, 'failed.csv', 'fail')
    await expect(
      page.getByText('Could not save the export. Try another location.', { exact: true }),
    ).toBeVisible()
    await expect(page.locator('body')).not.toContainText('Disk full')
    await expect(button).toBeEnabled()
    expect(await readdir(join(directory, 'docs'))).toEqual(priorFiles)
    expect(await readFile(join(directory, 'docs/unchanged.csv'), 'utf8')).toBe('existing destination bytes')
    expect((await api('acquisitions.export')).ownershipCount).toBe(0)
    await capture('export-safe-failure')
  })
  test(`${surface} exact Alpha and Beta saved licence pages require Read and repeat without duplicate facts`, async () => {
    test.setTimeout(90000)
    await control('seed', { kind: 'saved' })
    await savedPages()
    for (let pass = 1; pass <= 2; pass++) {
      await choosePages(['first.html', 'second.html'])
      expect((await control('state')).importCount).toBe(pass - 1)
      await expect(
        page.getByRole('button', {
          name: mode === 'fullscreen' ? 'Read selected pages' : 'Read',
          exact: true,
        }),
      ).toBeEnabled()
      await readPages()
      const results = page.getByRole('status', { name: 'Steam import results', exact: true })
      await expect(results).toBeVisible()
      await expect(results).toContainText(
        pass === 1
          ? '2 licence facts and 0 transaction facts recorded'
          : '0 licence facts and 0 transaction facts recorded',
      )
      await expect(results).toContainText(
        pass === 1 ? '0 facts were already recorded.' : '2 facts were already recorded.',
      )
      const importer = page.getByRole('region', { name: 'Steam purchase and licence import' })
      if (mode === 'fullscreen') {
        await expect(importer).toContainText('first.html \u00b7 LOADED')
        await expect(importer).toContainText('second.html \u00b7 LOADED')
      } else {
        await expect(importer).toContainText('first.html: Licence page ready')
        await expect(importer).toContainText('second.html: Licence page ready')
      }
      await expect(importer).not.toContainText('Duplicate licence pages were skipped')
      await expect.poll(async () => (await control('state')).importCount).toBe(pass)
      const state = await control('state')
      expect(state.licenses).toHaveLength(2)
      expect(state.licenses.map((row: any) => row.itemName).sort()).toEqual(['Alpha', 'Beta'])
      expect(state.transactions).toEqual([])
      const loads = (await nativeState()).requests.filter(
        (call: any) => call.path === '/api/v1/imports/steam/load-files' && call.completed,
      )
      expect(loads).toHaveLength(pass)
      expect(loads.at(-1).result.files.map((file: any) => file.outcome)).toEqual([0, 0])
      await expect(page.getByText('Reading and importing your capture\u2026', { exact: true })).toHaveCount(0)
      if (mode === 'fullscreen') {
        await importer
          .getByText('first.html \u00b7 LOADED', { exact: true })
          .evaluate((element) => element.scrollIntoView({ block: 'start' }))
        await expect(importer.getByText('second.html \u00b7 LOADED', { exact: true })).toBeInViewport()
      }
      await capture(`saved-pages-pass-${pass}`)
    }
    await closePages()
    expect((await nativeState()).openDialogs).toEqual([])
  })
  test(`${surface} exact reported Steam bounds and observation reading leave the recorded 600 seconds unchanged`, async () => {
    test.setTimeout(90000)
    await control('seed', { kind: 'activity' })
    expect(await control('state')).toMatchObject({ sessionCount: 1, sessionSeconds: 600 })
    if (mode === 'desktop') await size(800, 700)
    await navigate('Library')
    const tile = page.locator('.avalon-library [data-avalon-game="1"]')
    await activate(tile)
    const details = page.locator('.avalon-details')
    await expect(details.locator('h1')).toHaveText('Dragonwilds')
    if (mode === 'fullscreen') await activate(details.locator('[data-details-reading="History"]'))
    else await activate(details.getByRole('tab', { name: 'Activity', exact: true }))
    const total = details.locator('.activity-total')
    await expect(total).toBeVisible()
    const before = await total.textContent()
    if (mode === 'fullscreen')
      await activate(details.getByRole('button', { name: 'Steam-reported activity', exact: true }))
    const projection = page.getByRole('region', { name: 'Steam-reported activity', exact: true })
    await expect(projection.locator('.timeline-entry')).toHaveCount(2)
    for (const text of [
      'not exact sessions',
      'not added to recorded-session totals',
      'Observed between',
      'About 31 min not matched',
      'may overlap recorded sessions',
    ])
      await expect(projection).toContainText(text)
    for (const row of await projection.locator('.timeline-entry').all()) {
      const values = await row
        .locator('time')
        .evaluateAll((times) => times.map((time) => Date.parse(time.getAttribute('datetime')!)))
      expect(values).toEqual([Date.parse('2026-09-14T18:00:00Z'), Date.parse('2026-09-14T19:00:00Z')])
    }
    if (mode === 'fullscreen') {
      await expect(page.getByRole('group', { name: 'Steam activity controls', exact: true })).toBeVisible()
      await steamTypography(projection, 'per-game')
      const typography = await projection
        .locator('.steam-observation time')
        .first()
        .evaluate((node) => {
          const range = document.createRange()
          range.selectNodeContents(node)
          return {
            font: Number.parseFloat(getComputedStyle(node).fontSize),
            boundsFont: Number.parseFloat(getComputedStyle(node.parentElement!).fontSize),
            zoom: Number.parseFloat(getComputedStyle(document.body).zoom),
            paintedTextHeight: range.getBoundingClientRect().height,
          }
        })
      expect(typography.font).toBeCloseTo(22, 3)
      expect(typography.boundsFont).toBeCloseTo(22, 3)
      expect(typography.paintedTextHeight).toBeGreaterThanOrEqual(22 * typography.zoom * 0.8)
      await test.info().attach('steam-observation-typography', {
        body: JSON.stringify(typography),
        contentType: 'application/json',
      })
      const origin = projection.getByRole('button', { name: /Dragonwilds\. About 31 min not matched/ })
      await activate(origin)
      const reader = page.getByRole('region', { name: 'Steam activity observation', exact: true })
      await expect(reader).toBeVisible()
      await expect(reader).toContainText('About 31 min not matched')
      await expect(reader).toContainText('Observed between')
      const dialog = reader.locator('..')
      await expect(dialog.getByRole('button', { name: 'Back', exact: true })).toBeFocused()
      const bounds = await dialog.boundingBox()
      expect(bounds!.x).toBeCloseTo(0, 0)
      expect(bounds!.y).toBeCloseTo(0, 0)
      expect(bounds!.width).toBeCloseTo(1920, 0)
      expect(bounds!.height).toBeCloseTo(1080, 0)
      await expect(
        page.getByRole('group', { name: 'Steam activity reading controls', exact: true }),
      ).toContainText('Read')
      await capture('reported-observation-reader')
      await tap(1)
      await expect(reader).toHaveCount(0)
      await expect(origin).toBeFocused()
      await back()
      await expect(page.getByRole('region', { name: 'Steam activity projection', exact: true })).toHaveCount(
        0,
      )
      await expect(
        details.getByRole('button', { name: 'Steam-reported activity', exact: true }),
      ).toBeFocused()
    } else await capture('reported-observations-inline')
    await expect(total).toHaveText(before!)
    expect(await control('state')).toMatchObject({ sessionCount: 1, sessionSeconds: 600 })
    if (mode === 'fullscreen') {
      await back()
      await expect(details).not.toHaveAttribute('data-reading', 'History')
    }
    await back()
    await expect(details).toHaveCount(0)
    await expect(tile).toBeFocused()
    if (mode === 'desktop') await size(1920, 1080)
    await navigate('Activity')
    await activate(
      page
        .getByRole('navigation', { name: 'Activity pages' })
        .getByRole('button', { name: 'Steam-reported activity', exact: true }),
    )
    if (mode === 'fullscreen') {
      // Back must work immediately while the tab still owns focus, before any
      // observation is entered; it must not fall through to the root quick menu.
      await tap(1)
      const history = page
        .getByRole('navigation', { name: 'Activity pages' })
        .getByRole('button', { name: 'History', exact: true })
      await expect(history).toHaveAttribute('aria-pressed', 'true')
      await expect(history).toBeFocused()
      await expect(page.getByRole('dialog')).toHaveCount(0)
      await activate(
        page
          .getByRole('navigation', { name: 'Activity pages' })
          .getByRole('button', { name: 'Steam-reported activity', exact: true }),
      )
    }
    await expect(projection.locator('.timeline-entry')).toHaveCount(2)
    if (mode === 'fullscreen') {
      await steamTypography(projection, 'global')
      await expect(page.locator('[data-root-bumper="LB"] svg')).toBeVisible()
      await expect(page.locator('[data-root-bumper="RB"] svg')).toBeVisible()
    }
    await capture('global-reported-activity')
    const after = await control('state')
    expect(after).toMatchObject({ sessionCount: 1, sessionSeconds: 600 })
    expect(after.activityCalls).toContainEqual(
      expect.objectContaining({ ownershipIds: [1], accountRef: '123' }),
    )
  })
  test(`${surface} complete unknown saved pages never borrow the confirmed account and mixed provenance withholds money`, async () => {
    test.setTimeout(90000)
    await control('seed', { kind: 'unknown' })
    await savedPages()
    await choosePages(['licenses.html', 'history.html'])
    expect((await control('state')).importCount).toBe(0)
    await readPages()
    await expect(page.getByRole('status', { name: 'Steam import results', exact: true })).toBeVisible()
    await expect.poll(async () => (await control('state')).importCount).toBe(1)
    const unknown = await control('state')
    expect(unknown.transactions.length).toBeGreaterThan(0)
    expect(unknown.licenses.length).toBeGreaterThan(0)
    expect(unknown.acquisitions).toHaveLength(1)
    for (const fact of [...unknown.transactions, ...unknown.licenses, ...unknown.acquisitions])
      expect(fact.accountRef).toBeNull()
    for (const fact of [...unknown.transactions, ...unknown.licenses])
      expect(Date.parse(fact.capturedAt)).toBe(Date.parse('2026-09-10T00:00:00Z'))
    expect(unknown.ownerships[0].acquiredAt).not.toBeNull()
    let own = await api('game.details', undefined, { workId: 1 })
    expect(own.ownerships[0].acquiredAt).toBeNull()
    expect(own.ownerships[0].pricePaidCents).toBeNull()
    await control('change', { stage: 'all' })
    const all = await api('game.details', undefined, { workId: 1 })
    expect(all.ownerships[0].acquiredAt).not.toBeNull()
    await control('change', { stage: 'own' })
    const imported = await api('imports.steam.pages', unknown.namedPages)
    expect(imported.ownershipsFilled).toBe(0)
    const named = await control('state')
    expect(named.acquisitions).toHaveLength(1)
    expect(named.acquisitions[0].accountRef).toBeNull()
    expect(named.transactions.some((fact: any) => fact.accountRef === '10002')).toBe(true)
    own = await api('game.details', undefined, { workId: 1 })
    expect(own.ownerships[0].acquiredAt).toBeNull()
    expect(own.ownerships[0].pricePaidCents).toBeNull()
    await closePages()
    await spending()
    const stats = await api('statistics.account', undefined, { source: 'steam' })
    expect(stats.knownAccountCount).toBe(1)
    expect(stats.unknownAccountFactCount).toBeGreaterThan(0)
    const view = page.getByRole('region', { name: 'Account spending', exact: true })
    await expect(view).toContainText('no captured account identity')
    await expect(view).toContainText('Signing in does not assign them to an account.')
    await expect(view).toContainText('Monetary totals and percentages are unavailable')
    expect(await view.innerText()).not.toMatch(/\$\s*\d|\d+(?:[.,]\d+)?%/)
    await capture('unknown-account-money-withheld')
  })
}
