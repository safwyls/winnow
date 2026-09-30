import { closeFixture } from './fixture-cleanup'
import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { DatabaseSync } from 'node:sqlite'
import electronPath from 'electron'
import type { AccountStats } from '../../src/renderer/features/Accounts'
import { accountChartFixture, accountChartGroup } from '../account-fixtures'

let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-accounts-'))
  const env = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/accounts-main.mjs'),
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
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
})
test.afterAll(async () => closeFixture(application, directory))
async function fixture(value: AccountStats | null) {
  await application.evaluate((_, value) => {
    ;(globalThis as unknown as { __accountFixture: unknown }).__accountFixture = value
  }, value)
}
async function surface(mode: 'desktop' | 'fullscreen', width: number, scale = 1) {
  await page.reload()
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
  await application.evaluate(
    ({ BrowserWindow }, { mode, width }) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setFullScreen(false)
      window.setContentSize(mode === 'desktop' ? width + 360 : width, 1080)
      window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
    },
    { mode, width },
  )
  await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
  await page.evaluate(
    (scale) => document.documentElement.style.setProperty('--theme-text-scale', String(scale)),
    scale,
  )
  await page.getByRole('button', { name: 'Settings', exact: true }).click()
  await page
    .getByRole('navigation', { name: 'Settings section' })
    .getByRole('button', { name: mode === 'fullscreen' ? 'Library' : 'Spending', exact: true })
    .click()
  if (mode === 'fullscreen') await page.getByRole('button', { name: 'Spending', exact: true }).click()
  const stats = page.getByRole('region', { name: 'Account spending' })
  await expect(stats.getByRole('button', { name: 'Refresh Steam spending' })).toBeEnabled()
  if (mode === 'desktop') {
    // The source fixture measures the isolated statistics surface, without shell navigation.
    await stats.evaluate((node, width) => {
      ;(node as HTMLElement).style.width = `${width}px`
    }, width)
    expect(await stats.evaluate((node) => node.clientWidth)).toBe(width)
  } else expect(await page.evaluate(() => innerWidth)).toBe(width)
  await page.evaluate(() => document.fonts.ready)
  return stats
}
async function installPad() {
  await page.evaluate(() => {
    const state = { pressed: [] as number[] }
    ;(window as unknown as { accountPad: typeof state }).accountPad = state
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          connected: true,
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, index) => ({ pressed: state.pressed.includes(index) })),
        },
      ],
    })
  })
}
async function tap(index: number) {
  for (const pressed of [[], [index], []])
    await page.evaluate(async (pressed) => {
      ;(window as unknown as { accountPad: { pressed: number[] } }).accountPad.pressed = pressed
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
}
async function noOverflow() {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true)
  for (const element of await page
    .locator('.account-statistics, .account-chart, .account-reading-body')
    .all())
    expect(await element.evaluate((node) => node.scrollWidth <= node.clientWidth + 1)).toBe(true)
}
for (const mode of ['desktop', 'fullscreen'] as const) {
  for (const mixed of [false, true]) {
    test(`${mode} original ${mixed ? 'mixed' : 'single'} currency summary preserves proportions, exclusions and font size`, async ({}, info) => {
      await fixture(null)
      const database = new DatabaseSync(join(directory, 'winnow.db'))
      try {
        database.exec('DELETE FROM account_transactions')
        const insert = database.prepare(
          "INSERT INTO account_transactions(source,account_ref,kind,transaction_type_raw,item_names_json,item_count,total_cents,currency_symbol,refunded,captured_at) VALUES('steam','10001',?,?,?,?,?,?,?,?)",
        )
        for (const [name, cents, refunded, kind, currency] of [
          ['Bundle', 1000, 0, 'purchase', '$'],
          ['Single', 2000, 0, 'purchase', mixed ? '€' : '$'],
          ['Reversed purchase', 3000, 1, 'purchase', '$'],
          ['Missing price', null, 0, 'purchase', '$'],
          ['Wallet', 90000, 0, 'wallet_credit_purchase', '$'],
          ['Standalone refund', 3000, 0, 'refund', '$'],
        ] as const)
          insert.run(
            kind,
            kind,
            JSON.stringify(name === 'Bundle' ? [name, 'Second game'] : [name]),
            name === 'Bundle' ? 2 : 1,
            cents,
            currency,
            refunded,
            '2026-09-29 12:00:00',
          )
      } finally {
        database.close()
      }
      const stats = await surface(mode, mode === 'desktop' ? 700 : 1920)
      const percentage = stats.getByText('33.3%', { exact: true })
      await expect(percentage).toBeVisible()
      await expect(stats.getByText('50%', { exact: true })).toBeVisible()
      await expect(
        stats
          .getByRole('region', { name: 'Spending in $' })
          .getByText(mixed ? '$10.00' : '$30.00', { exact: true })
          .first(),
      ).toBeVisible()
      await expect(
        stats.getByText(
          'Product transactions with recorded prices only. Wallet credit and standalone refund rows are excluded. Percentages count transactions, not games or money; missing-price rows and uncaptured pages are outside these figures.',
        ),
      ).toBeVisible()
      const size = await percentage.evaluate((node) => ({
        font: parseFloat(getComputedStyle(node).fontSize),
        width: node.getBoundingClientRect().width,
        parent: node.parentElement!.getBoundingClientRect().width,
      }))
      expect(size.font).toBeGreaterThanOrEqual(mode === 'fullscreen' ? 40 : 22)
      expect(size.width).toBeGreaterThan(0)
      expect(size.width).toBeLessThanOrEqual(size.parent)
      await noOverflow()
      await percentage.scrollIntoViewIfNeeded()
      await page.screenshot({ path: info.outputPath('summary.png') })
      expect(errors).toEqual([])
    })
  }
  for (const width of mode === 'desktop' ? [1200, 600] : [1920, 1280]) {
    test(`${mode} ${width} currency charts retain focus, refresh selection, exact composition and readable details`, async ({}, info) => {
      await fixture(accountChartFixture())
      const stats = await surface(mode, width, mode === 'fullscreen' && width === 1280 ? 1.4 : 1)
      await expect(stats.getByText(/Average kept transaction: \$16.00/)).toBeVisible()
      await expect(stats.getByText('Highest recorded year: 2023 · $350.00')).toBeVisible()
      await expect(
        stats.getByRole('region', { name: 'Spending in €' }).getByText('€240.00', { exact: true }),
      ).toBeVisible()
      const composition = stats.getByRole('region', { name: 'Product spending by kind' })
      await expect(composition.locator('circle')).toHaveCount(3)
      await expect(composition.getByText('$900.00 · 75%', { exact: true })).toBeVisible()
      const currency =
        mode === 'desktop'
          ? stats.getByLabel('Chart and detail currency')
          : stats.getByRole('button', { name: /^€ ·/ })
      if (mode === 'desktop') {
        await currency.focus()
        await currency.selectOption('€')
      } else {
        await installPad()
        await stats.getByRole('button', { name: '$ · selected' }).focus()
        await tap(15)
        await expect(currency).toBeFocused()
        await tap(0)
        await expect(currency).toHaveAttribute('aria-pressed', 'true')
      }
      await expect(currency).toBeFocused()
      await expect(composition.getByText('€180.00 · 75%', { exact: true })).toBeVisible()
      await expect(composition.getByText('€40.00 · 16.7%', { exact: true })).toBeVisible()
      await expect(composition.getByText('€20.00 · 8.3%', { exact: true })).toBeVisible()
      const years = stats.getByRole('region', { name: 'Spending by year' })
      for (const text of await years.locator('strong').allTextContents())
        expect(text.startsWith('€')).toBe(true)
      const reads = await application.evaluate(
        () => (globalThis as unknown as { __accountReads: number }).__accountReads,
      )
      await stats.getByRole('button', { name: 'Refresh Steam spending' }).click()
      await expect
        .poll(() =>
          application.evaluate(() => (globalThis as unknown as { __accountReads: number }).__accountReads),
        )
        .toBe(reads + 1)
      await expect(stats.getByRole('button', { name: 'Refresh Steam spending' })).toBeEnabled()
      await expect(stats.getByText('Highest recorded year: 2023 · €70.00')).toBeVisible()
      if (mode === 'desktop') await expect(currency).toHaveValue('€')
      else await expect(currency).toHaveAttribute('aria-pressed', 'true')
      await composition.scrollIntoViewIfNeeded()
      if (mode === 'fullscreen') {
        expect(
          await composition
            .getByRole('heading')
            .evaluate((node) => parseFloat(getComputedStyle(node).fontSize)),
        ).toBeGreaterThanOrEqual(28)
        expect(
          await composition
            .getByText('Kept product transactions. Wallet credit is excluded.')
            .evaluate((node) => parseFloat(getComputedStyle(node).fontSize)),
        ).toBeGreaterThanOrEqual(24)
      }
      await noOverflow()
      await page.screenshot({ path: info.outputPath('composition.png') })
      const opener =
        mode === 'desktop'
          ? stats.locator('summary', { hasText: 'Detailed spending breakdown' })
          : stats.getByRole('button', { name: 'Read spending details' })
      if (mode === 'desktop') {
        await expect(stats.getByRole('table')).toHaveCount(0)
        await opener.click()
      } else {
        await opener.focus()
        await tap(0)
      }
      const details = page.getByRole('region', { name: 'Spending details in €' })
      await expect(details.getByRole('row', { name: '2023 20 €70.00' })).toBeVisible()
      await expect(details.getByRole('row', { name: 'Single-item purchases 60 €180.00' })).toBeVisible()
      await noOverflow()
      if (mode === 'fullscreen') {
        const dialog = page.getByRole('dialog', { name: 'Spending details · €' })
        expect(
          await dialog
            .getByText(/Wallet credit stays separate/)
            .evaluate((node) => parseFloat(getComputedStyle(node).fontSize)),
        ).toBeGreaterThanOrEqual(22)
        const bounds = (await dialog.boundingBox())!
        expect(bounds.x).toBeGreaterThanOrEqual(0)
        expect(bounds.y).toBeGreaterThanOrEqual(0)
        expect(bounds.x + bounds.width).toBeLessThanOrEqual(width)
        expect(bounds.y + bounds.height).toBeLessThanOrEqual(1080)
        await page.screenshot({ path: info.outputPath('reading.png') })
        await tap(1)
        await expect(dialog).toHaveCount(0)
        await expect(opener).toBeFocused()
      }
      expect(errors).toEqual([])
    })
  }
  test(`${mode} signed year bars straddle zero and zero-valued years remain captured facts`, async () => {
    const value = accountChartGroup('$', 1)
    value.spendByYear = [
      { year: 2023, transactionCount: 1, cents: -2000 },
      { year: 2024, transactionCount: 1, cents: 10000 },
      { year: 2025, transactionCount: 1, cents: 0 },
    ]
    value.inGamePurchases = { count: 1, cents: -2000 }
    await fixture(value)
    const stats = await surface(mode, mode === 'desktop' ? 700 : 1280)
    const years = stats.getByRole('region', { name: 'Spending by year' })
    await expect(years.getByText('$-20.00', { exact: true })).toBeVisible()
    await expect(years.getByText('$0.00', { exact: true })).toBeVisible()
    const geometry = await years.locator('li').evaluateAll((rows) =>
      rows.map((row) => {
        const zero = row.querySelector('.account-chart-zero')!.getBoundingClientRect()
        const bar = row.querySelector('.account-chart-bar')!.getBoundingClientRect()
        return { zero: zero.x, left: bar.x, right: bar.right, width: bar.width }
      }),
    )
    expect(geometry[0].width).toBeGreaterThan(0)
    expect(geometry[0].left).toBeLessThan(geometry[0].zero)
    expect(Math.abs(geometry[0].right - geometry[0].zero)).toBeLessThan(1)
    expect(Math.abs(geometry[1].left - geometry[1].zero)).toBeLessThan(1)
    expect(geometry[1].right).toBeGreaterThan(geometry[1].zero)
    expect(geometry[2].width).toBe(0)
    await expect(stats.getByRole('region', { name: 'Product spending by kind' })).toHaveCount(0)
    await expect(stats.getByText(/Some categories have negative totals/)).toBeVisible()
    await noOverflow()
  })
}
