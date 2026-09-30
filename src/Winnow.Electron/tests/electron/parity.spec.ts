import { closeFixture } from './fixture-cleanup'
import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp, readFile, access } from 'node:fs/promises'
import { resolve, join } from 'node:path'
import electronPath from 'electron'
import { spawn } from 'node:child_process'
import { DatabaseSync } from 'node:sqlite'

let application: ElectronApplication
let page: Page
let directory: string
const errors: string[] = []
test.beforeAll(async () => {
  const root = resolve('../..', '.tmp')
  await access(root)
  directory = await mkdtemp(join(root, 'winnow-electron-rendered-'))
  const environment = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/plugin-install-main.mjs'),
      '--data-dir',
      directory,
      '--seed-sample',
      '--no-sync',
    ],
    env: environment,
    chromiumSandbox: true,
    timeout: 60000,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  // Demo seeding can complete setup. A normal fresh library offers the wizard.
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
  if (await page.locator('[role="dialog"][aria-label="Winnow setup"]').count()) {
    await page.getByRole('button', { name: 'Skip setup', exact: true }).click()
    await expect(page.locator('[role="dialog"][aria-label="Winnow setup"]')).toHaveCount(0)
  }
  await expect(page.locator('.avalon-shell')).toBeVisible()
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
  // Only this fixture's fresh database receives synthetic account facts.
  const database = new DatabaseSync(join(directory, 'winnow.db'))
  try {
    const insert = database.prepare(
      "INSERT INTO account_transactions(source,account_ref,kind,transaction_type_raw,occurred_at,item_names_json,item_count,total_cents,currency_symbol,refunded,captured_at) VALUES('steam','10001',?,?,?,?,?,?,?,?,?)",
    )
    for (const [symbol, factor] of [
      ['$', 1],
      ['€', 2],
    ] as const) {
      for (const [name, amount, refunded, kind, count, year] of [
        ['Bundle', 1000, 0, 'purchase', 2, 2024],
        ['Single', 2000, 0, 'purchase', 1, 2025],
        ['Reversed', 3000, 1, 'purchase', 1, 2025],
        ['Missing price', null, 0, 'purchase', 1, 2025],
        ['Wallet', 90000, 0, 'wallet_credit_purchase', 1, 2025],
        ['Standalone refund', 3000, 0, 'refund', 1, 2025],
      ] as const)
        insert.run(
          kind,
          kind,
          `${year}-01-15 12:00:00`,
          JSON.stringify([`${name} ${symbol}`, ...(count === 2 ? ['Second game'] : [])]),
          count,
          amount === null ? null : amount * factor,
          symbol,
          refunded,
          '2026-09-28 12:00:00',
        )
    }
  } finally {
    database.close()
  }
})
test.afterAll(async () => closeFixture(application, directory))
async function surface(mode: 'desktop' | 'fullscreen', width: number, height: number) {
  await application.evaluate(
    ({ BrowserWindow }, value) => {
      const window = BrowserWindow.getAllWindows()[0]
      window.setFullScreen(false)
      window.setContentSize(value.width, value.height)
      window.webContents.send('winnow:fullscreen:changed', value.mode === 'fullscreen')
    },
    { mode, width, height },
  )
  await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
  await page.getByRole('button', { name: 'Winnow home', exact: true }).click()
  await page.evaluate(() => document.fonts.ready)
}
async function noHorizontalOverflow() {
  const measure = await page.evaluate(() => ({
    width: document.documentElement.clientWidth,
    scroll: document.documentElement.scrollWidth,
  }))
  expect(measure.scroll).toBeLessThanOrEqual(measure.width + 1)
}

test('native startup uses Avalon and keeps credentials behind the preload boundary', async () => {
  await expect(page).toHaveTitle('Winnow · Avalon')
  const exposed = await page.evaluate(() => ({
    node: typeof (window as unknown as { require?: unknown }).require,
    token: typeof (window.winnow as unknown as { token?: unknown }).token,
    bridge: Object.isFrozen(window.winnow),
  }))
  expect(exposed).toEqual({ node: 'undefined', token: 'undefined', bridge: true })
})

for (const mode of ['desktop', 'fullscreen'] as const) {
  for (const [width, height] of [
    [1280, 720],
    [1920, 1080],
  ]) {
    test(`${mode} Home and Library remain inside a ${width}×${height} viewport`, async () => {
      await surface(mode, width, height)
      await expect(page.locator('.avalon-cover').first()).toBeVisible()
      await noHorizontalOverflow()
      const dimensions = await page.locator('.avalon-cover').first().boundingBox()
      expect(dimensions!.width).toBeGreaterThan(60)
      expect(dimensions!.height).toBeGreaterThan(dimensions!.width)
      await page
        .getByRole('navigation', { name: 'Main navigation' })
        .getByRole('button', { name: 'Library', exact: true })
        .click()
      if (mode === 'desktop') await expect(page.locator('[data-library-search]')).toBeVisible()
      else await expect(page.getByRole('button', { name: 'Filter & sort', exact: true })).toBeVisible()
      await noHorizontalOverflow()
      const navigation = await page.getByRole('navigation', { name: 'Main navigation' }).boundingBox()
      expect(navigation!.y).toBeGreaterThanOrEqual(0)
      expect(navigation!.y + navigation!.height).toBeLessThanOrEqual(height)
      await page.screenshot({
        path: join(resolve('../..', '.tmp/electron-rendered-results'), `${mode}-${width}-library.png`),
      })
    })
  }
  test(`${mode} searches, opens details, and returns to the same query`, async () => {
    await surface(mode, 1280, 720)
    await page.keyboard.press('Control+k')
    const search =
      mode === 'fullscreen'
        ? page.getByRole('searchbox', { name: 'Search games' })
        : page.locator('[data-library-search]')
    if (mode === 'fullscreen')
      await expect(page.getByRole('button', { name: 'Enter search', exact: true })).toBeFocused()
    else await expect(search).toBeFocused()
    await search.fill('Hades')
    const card = page.locator('.avalon-cover').filter({ hasText: 'Hades' }).first()
    await expect(card).toBeVisible()
    await card.click({ modifiers: [] })
    await expect(page.locator('.avalon-details')).toBeVisible()
    await page.keyboard.press('Escape')
    await expect(search).toHaveValue('Hades')
    await search.fill('')
  })
  test(`${mode} exposes account and application settings and keeps Activity in bounds`, async () => {
    await surface(mode, 1280, 720)
    const navigation = page.getByRole('navigation', { name: 'Main navigation' })
    await page.getByRole('button', { name: 'Settings', exact: true }).click()
    await expect(
      page.getByRole('heading', {
        name: mode === 'fullscreen' ? 'Make yourself comfortable' : 'Make yourself at home.',
      }),
    ).toBeVisible()
    await page
      .getByRole('navigation', { name: 'Settings section' })
      .getByRole('button', { name: 'Application', exact: true })
      .click()
    await expect(
      page.getByRole(mode === 'fullscreen' ? 'switch' : 'checkbox', {
        name: mode === 'fullscreen' ? 'Close to tray' : /Close to notification area/,
      }),
    ).toBeVisible()
    await noHorizontalOverflow()
    await navigation.getByRole('button', { name: 'Activity', exact: true }).click()
    await expect(page.locator('.journal-page')).toBeVisible()
    await noHorizontalOverflow()
    await page.screenshot({
      path: join(resolve('../..', '.tmp/electron-rendered-results'), `${mode}-activity.png`),
    })
  })
  test(`${mode} spending preserves currency boundaries and chart focus at large text sizes`, async () => {
    await surface(mode, 1280, 720)
    const navigation = page.getByRole('navigation', { name: 'Main navigation' })
    await page.getByRole('button', { name: 'Settings', exact: true }).click()
    await page
      .getByRole('navigation', { name: 'Settings section' })
      .getByRole('button', { name: mode === 'fullscreen' ? 'Library' : 'Spending', exact: true })
      .click()
    if (mode === 'fullscreen') await page.getByRole('button', { name: 'Spending', exact: true }).click()
    const statistics = page.getByRole('region', { name: 'Account spending' })
    await expect(statistics.getByText('33.3%', { exact: true })).toBeVisible()
    await expect(statistics.getByText('50%', { exact: true })).toBeVisible()
    await expect(
      statistics.getByRole('region', { name: 'Spending in $' }).getByText('$30.00', { exact: true }).first(),
    ).toBeVisible()
    await expect(
      statistics.getByRole('region', { name: 'Spending in €' }).getByText('€60.00', { exact: true }).first(),
    ).toBeVisible()
    const currency =
      mode === 'desktop'
        ? statistics.getByLabel('Chart and detail currency')
        : statistics.getByRole('button', { name: /^€ ·/ })
    if (mode === 'desktop') await currency.selectOption('€')
    else await currency.click()
    await currency.focus()
    await expect(currency).toBeFocused()
    await expect(
      statistics.getByRole('region', { name: 'Spending by year' }).getByText('€40.00', { exact: true }),
    ).toBeVisible()
    await page.evaluate(() => {
      document.documentElement.style.setProperty('--theme-text-scale', '1.2')
      const panel = document.querySelector<HTMLElement>('.account-statistics')!
      panel.style.maxWidth = '600px'
    })
    for (const chart of await statistics.locator('.account-chart').all()) {
      await chart.scrollIntoViewIfNeeded()
      expect(await chart.evaluate((element) => element.scrollWidth <= element.clientWidth + 1)).toBe(true)
    }
    await noHorizontalOverflow()
    const font = await statistics
      .getByText('33.3%', { exact: true })
      .evaluate((element) => parseFloat(getComputedStyle(element).fontSize))
    expect(font).toBeGreaterThanOrEqual(mode === 'fullscreen' ? 40 : 22)
    await page.screenshot({
      path: join(resolve('../..', '.tmp/electron-rendered-results'), `${mode}-spending.png`),
    })
    await page.evaluate(() => {
      document.documentElement.style.removeProperty('--theme-text-scale')
    })
  })
}

test('a second process delivers fullscreen and starts plugin installation in the same library session', async () => {
  const before = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
  const secondary = async (args: string[]) => {
    const child = spawn(electronPath as unknown as string, [resolve('.'), '--data-dir', directory, ...args], {
      windowsHide: true,
      stdio: 'ignore',
      env: { ...process.env, ELECTRON_RUN_AS_NODE: undefined },
    })
    await new Promise<void>((done, reject) => {
      const timer = setTimeout(() => {
        child.kill()
        reject(new Error('Second instance did not exit'))
      }, 15000)
      child.once('error', (error) => {
        clearTimeout(timer)
        reject(error)
      })
      child.once('exit', (code) => {
        clearTimeout(timer)
        code === 0 ? done() : reject(new Error(`Second instance exited ${code}`))
      })
    })
  }
  await surface('desktop', 1280, 720)
  await secondary(['--jump-list-fullscreen'])
  await expect(page.locator('.avalon-shell.fullscreen')).toBeVisible()
  await secondary(['--uri', 'winnow://plugins/install?id=xbox&release=v1.2.3'])
  const installation = page.getByRole('region', { name: 'Plugin installation' })
  await expect(installation).toBeVisible()
  await expect(installation.getByRole('status')).toHaveText('The download failed. Try again.')
  const starts = await application.evaluate(
    () =>
      (globalThis as unknown as { __pluginInstallFixture: { starts: { request: unknown }[] } })
        .__pluginInstallFixture.starts,
  )
  expect(starts.map((value) => value.request)).toEqual([{ pluginId: 'xbox', releaseTag: 'v1.2.3' }])
  await installation.getByRole('button', { name: 'Back', exact: true }).click()
  const after = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
  expect({ processId: after.processId, epoch: after.epoch }).toEqual({
    processId: before.processId,
    epoch: before.epoch,
  })
})

test('controller input opens the keyboard and quick menu and restores focus without duplicate overlays', async () => {
  await surface('fullscreen', 1280, 720)
  await page.keyboard.press('Control+k')
  // The modal keyboard intentionally hides the underlying page from the accessibility tree.
  const search = page.locator('.avalon-search-inputs input')
  await expect(page.getByRole('button', { name: 'Enter search', exact: true })).toBeFocused()
  await page.evaluate(() => {
    const state = { pressed: [] as number[] }
    ;(window as unknown as { testController: typeof state }).testController = state
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          connected: true,
          mapping: 'standard',
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, index) => ({
            pressed: state.pressed.includes(index),
            touched: false,
            value: state.pressed.includes(index) ? 1 : 0,
          })),
        },
      ],
    })
  })
  const buttons = async (pressed: number[]) =>
    page.evaluate(async (value) => {
      ;(window as unknown as { testController: { pressed: number[] } }).testController.pressed = value
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
  const tap = async (button: number) => {
    await buttons([])
    await buttons([button])
    await buttons([])
  }
  await tap(0)
  const keyboard = page.getByRole('dialog', { name: 'Enter text' })
  await expect(keyboard).toBeVisible()
  await keyboard.getByRole('button', { name: 'a', exact: true }).click()
  await expect(search).toHaveValue('a')
  await tap(2)
  await expect(search).toHaveValue('')
  await tap(1)
  await expect(keyboard).toHaveCount(0)
  await expect(search).toBeFocused()
  await tap(9)
  await expect(page.getByRole('dialog', { name: 'Quick menu' })).toBeVisible()
  await tap(9)
  await expect(page.getByRole('dialog', { name: 'Quick menu' })).toHaveCount(1)
  await tap(1)
  await expect(page.getByRole('dialog', { name: 'Quick menu' })).toHaveCount(0)
  await expect(search).toBeFocused()
  await page.evaluate(() =>
    Object.defineProperty(navigator, 'getGamepads', { configurable: true, value: () => [] }),
  )
})

test('rendered screens report no uncaught exceptions', async () => expect(errors).toEqual([]))
