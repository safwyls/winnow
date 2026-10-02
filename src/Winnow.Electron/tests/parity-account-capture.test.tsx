// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AccountStatistics, type AccountStats } from '../src/renderer/features/Accounts'
import { GameplayDashboard } from '../src/renderer/features/activity-gameplay'
import { clearViewState } from '../src/renderer/viewState'
import type { Mode } from '../src/renderer/api/types'

const empty: AccountStats = {
  source: 'steam',
  hasAnything: false,
  isSingleCurrency: true,
  currencySymbol: null,
  currencyGroups: [],
  currencies: [],
  transactionCount: 0,
  licenseCount: 0,
  knownAccountCount: 0,
  unknownAccountFactCount: 0,
  transactionsWithoutCurrency: 0,
  transactionsWithoutDate: 0,
  grossProductSpendCents: 0,
  refundedProductSpendCents: 0,
  netProductSpendCents: 0,
  grossProductTransactionCount: 0,
  refundedProductTransactionCount: 0,
  netProductTransactionCount: 0,
  purchases: { count: 0, cents: 0 },
  giftPurchases: { count: 0, cents: 0 },
  inGamePurchases: { count: 0, cents: 0 },
  bundlePurchases: { count: 0, cents: 0 },
  refundTransactions: { count: 0, cents: 0 },
  walletCreditPurchases: { count: 0, cents: 0 },
  walletCreditRedemptions: { count: 0, cents: 0 },
  discountedPurchases: { count: 0, cents: 0 },
  discountedPurchaseListCents: 0,
  spendByYear: [],
  undatedNetSpendCents: 0,
  undatedNetTransactionCount: 0,
  licenseAcquisitions: [],
}
const clients: QueryClient[] = []
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  for (const mode of ['desktop', 'fullscreen']) {
    clearViewState(`${mode}:stats:section`)
    clearViewState(`${mode}:stats:currency`)
  }
  vi.restoreAllMocks()
})
function mount(mode: Mode, value: AccountStats, dashboard = false) {
  let current = value
  const read = vi.fn(() => current)
  const request = vi.fn(async ({ route }: { route: string }) => ({
    ok: true,
    status: 200,
    data:
      route === 'statistics.account'
        ? read()
        : route === 'library.get'
          ? { games: [], lists: [] }
          : { recordedSeconds: 0, gamesPlayedCount: 0, startedSessionCount: 0, periods: [], topGames: [] },
  }))
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request, cancelRequest: vi.fn(async () => {}) },
  })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  clients.push(client)
  const view = render(
    <QueryClientProvider client={client}>
      <div data-mode={mode} className={`mode-${mode}`}>
        {dashboard ? <GameplayDashboard mode={mode} /> : <AccountStatistics mode={mode} />}
      </div>
    </QueryClientProvider>,
  )
  return {
    ...view,
    read,
    update: (value: AccountStats) => {
      current = value
    },
  }
}
const fact = (scope: HTMLElement, label: string, expected: string) => {
  const term = within(scope).getByText(label, { selector: 'dt' })
  expect(term.parentElement?.querySelector('dd')?.textContent).toBe(expected)
}
async function openBreakdown(mode: Mode) {
  if (mode === 'desktop')
    fireEvent.click(await screen.findByText('Detailed spending breakdown', { selector: 'summary' }))
  else fireEvent.click(await screen.findByRole('button', { name: 'Read spending details' }))
}

describe.each<Mode>(['desktop', 'fullscreen'])('captured account facts on %s', (mode) => {
  it('With_no_facts_the_screen_is_an_empty_state_and_not_a_table_of_zeroes', async () => {
    mount(mode, empty)
    await screen.findByText(/No Steam spending has been captured/)
    expect(screen.getByText(/purchase-history and licence pages in Settings → Platforms/)).toBeTruthy()
    expect(screen.queryByRole('table')).toBeNull()
    expect(
      screen.queryByRole('region', {
        name: /Largest transaction|Captured currencies|Licence acquisition methods|Spending by year/,
      }),
    ).toBeNull()
    expect(screen.queryByText(/0\.00/)).toBeNull()
  })
  it('Licences_alone_are_enough_to_leave_the_empty_state', async () => {
    mount(mode, {
      ...empty,
      hasAnything: true,
      licenseCount: 4,
      licenseAcquisitions: [{ kind: 'steam_store', count: 4 }],
    })
    const licences = await screen.findByRole('region', { name: 'Licence acquisition methods' })
    expect(within(licences).getByText('Steam Store')).toBeTruthy()
    expect(within(licences).getByText('4')).toBeTruthy()
    expect(screen.queryByText(/No Steam spending has been captured/)).toBeNull()
    expect(screen.queryByRole('table')).toBeNull()
    expect(screen.getAllByText('—')).toHaveLength(2)
  })
  it('A_mixed_currency_capture_renders_no_money_at_all_and_keeps_its_counts', async () => {
    mount(mode, {
      ...empty,
      hasAnything: true,
      isSingleCurrency: false,
      transactionCount: 5,
      grossProductSpendCents: 9900,
      grossProductTransactionCount: 5,
      spendByYear: [{ year: 2024, transactionCount: 3, cents: 5000 }],
      purchases: { count: 4, cents: 7400 },
      walletCreditPurchases: { count: 1, cents: 2500 },
      currencies: [
        { symbol: '$', transactionCount: 3 },
        { symbol: '£', transactionCount: 2 },
      ],
      biggestPurchase: {
        cents: 4000,
        itemNames: ['Disco Elysium'],
        itemCount: 1,
        currencySymbol: '£',
        isBundle: false,
      },
    })
    const counts = await screen.findByRole('region', { name: 'Captured transaction counts' })
    fact(counts, 'Before refunds', '5')
    fact(counts, '2024', '3')
    fact(counts, 'Single-item purchases', '4')
    fact(counts, 'Wallet top-ups', '1')
    const currencies = screen.getByRole('region', { name: 'Captured currencies' })
    fact(currencies, '$', '3')
    fact(currencies, '£', '2')
    expect(screen.queryByRole('region', { name: 'Captured spending charts' })).toBeNull()
    expect(screen.queryByText(/99\.00|50\.00|74\.00|25\.00/)).toBeNull()
    expect(
      within(screen.getByRole('region', { name: 'Largest transaction' })).getByText('£40.00'),
    ).toBeTruthy()
  })
  it('One_symbol_plus_a_symbol_less_row_is_still_mixed', async () => {
    mount(mode, {
      ...empty,
      hasAnything: true,
      isSingleCurrency: false,
      transactionCount: 3,
      grossProductSpendCents: 3000,
      grossProductTransactionCount: 3,
      currencies: [{ symbol: '$', transactionCount: 2 }],
      transactionsWithoutCurrency: 1,
    })
    const currencies = await screen.findByRole('region', { name: 'Captured currencies' })
    fact(currencies, '$', '2')
    fact(currencies, 'No currency symbol', '1')
    expect(screen.queryByText(/30\.00/)).toBeNull()
    expect(screen.queryByRole('region', { name: 'Captured spending charts' })).toBeNull()
  })
  it('A_single_currency_capture_renders_its_totals_with_the_symbol_as_stored', async () => {
    mount(mode, {
      ...empty,
      hasAnything: true,
      currencySymbol: '€',
      currencies: [{ symbol: '€', transactionCount: 12 }],
      transactionCount: 12,
      grossProductSpendCents: 123456,
      grossProductTransactionCount: 12,
      refundedProductSpendCents: 1999,
      refundedProductTransactionCount: 1,
      netProductSpendCents: 121457,
      netProductTransactionCount: 11,
    })
    const spending = await screen.findByRole('region', { name: 'Spending in €' })
    fact(spending, 'Before refunds', '€1,234.56')
    fact(spending, 'Refunded purchases', '€19.99')
    fact(spending, 'Net product spend', '€1,214.57')
    expect(within(spending).getByText('11 transactions')).toBeTruthy()
    expect(screen.queryByRole('region', { name: 'Captured currencies' })).toBeNull()
  })
  it('Wallet_credit_is_its_own_fact_and_never_joins_the_spend_figures', async () => {
    mount(mode, {
      ...empty,
      hasAnything: true,
      currencySymbol: '$',
      transactionCount: 6,
      grossProductTransactionCount: 4,
      netProductTransactionCount: 4,
      grossProductSpendCents: 5000,
      netProductSpendCents: 5000,
      purchases: { count: 4, cents: 5000 },
      walletCreditPurchases: { count: 1, cents: 2000 },
      walletCreditRedemptions: { count: 1, cents: 1000 },
    })
    const spending = await screen.findByRole('region', { name: 'Spending in $' })
    fact(spending, 'Before refunds', '$50.00')
    fact(spending, 'Net product spend', '$50.00')
    await openBreakdown(mode)
    expect(screen.getByRole('row', { name: 'Single-item purchases 4 $50.00' })).toBeTruthy()
    expect(screen.getByRole('row', { name: 'Wallet top-ups 1 $20.00' })).toBeTruthy()
    expect(screen.getByRole('row', { name: 'Redeemed wallet credit 1 $10.00' })).toBeTruthy()
    expect(screen.queryByText(/\$70\.00|\$80\.00/)).toBeNull()
  })
  it('The_year_table_lists_the_undated_slice_as_its_own_line', async () => {
    mount(mode, {
      ...empty,
      hasAnything: true,
      currencySymbol: '$',
      transactionCount: 9,
      spendByYear: [
        { year: 2019, transactionCount: 2, cents: 3000 },
        { year: 2023, transactionCount: 4, cents: 8050 },
      ],
      undatedNetSpendCents: 1500,
      undatedNetTransactionCount: 3,
    })
    await openBreakdown(mode)
    const years = await screen.findByRole('table', { name: 'Spending by year details' })
    expect(
      within(years)
        .getAllByRole('row')
        .slice(1)
        .map((row) => row.textContent),
    ).toEqual(['20192$30.00', '20234$80.50', 'Undated3$15.00'])
  })
  it('With_every_row_dated_the_year_table_has_no_extra_line', async () => {
    mount(mode, {
      ...empty,
      hasAnything: true,
      currencySymbol: '$',
      transactionCount: 2,
      spendByYear: [{ year: 2021, transactionCount: 2, cents: 4000 }],
    })
    await openBreakdown(mode)
    const years = await screen.findByRole('table', { name: 'Spending by year details' })
    expect(within(years).getAllByRole('row')).toHaveLength(2)
    expect(within(years).getByRole('row', { name: '2021 2 $40.00' })).toBeTruthy()
    expect(screen.queryByText('Undated')).toBeNull()
  })
  it('A_bundle_shows_its_total_and_flags_itself_rather_than_being_split', async () => {
    mount(mode, {
      ...empty,
      hasAnything: true,
      currencySymbol: '$',
      transactionCount: 1,
      bundlePurchases: { count: 1, cents: 12000 },
      biggestPurchase: {
        cents: 12000,
        occurredAt: '2023-11-24T00:00:00Z',
        itemNames: ['Portal', 'Portal 2', 'Half-Life 2'],
        itemCount: 3,
        currencySymbol: '$',
        isBundle: true,
      },
    })
    await openBreakdown(mode)
    const biggest = await screen.findByRole('region', { name: 'Largest transaction' })
    expect(within(biggest).getByText('$120.00')).toBeTruthy()
    expect(within(biggest).getByText('24 Nov 2023')).toBeTruthy()
    expect(within(biggest).getByText('Portal, Portal 2, Half-Life 2 (bundle)')).toBeTruthy()
    expect(screen.getByRole('row', { name: 'Bundles 1 $120.00' })).toBeTruthy()
    expect(screen.queryByText(/40\.00/)).toBeNull()
  })
  it('An_absent_date_is_an_absent_row', async () => {
    mount(mode, { ...empty, hasAnything: true, transactionCount: 2, transactionsWithoutDate: 2 })
    const coverage = await screen.findByRole('region', { name: 'Capture coverage' })
    fact(coverage, 'Transactions without a date', '2')
    expect(
      within(coverage).queryByText(/First transaction|Last transaction|First licence|Last licence|unknown/),
    ).toBeNull()
  })
  it('renders each supplied coverage date independently and names licence acquisition methods', async () => {
    mount(mode, {
      ...empty,
      hasAnything: true,
      licenseCount: 4,
      lastTransactionAt: '2023-11-24T00:00:00Z',
      firstLicenseAt: '2019-01-01T00:00:00Z',
      licensesWithoutDate: 2,
      licenseAcquisitions: [
        { kind: 'gift', count: 1 },
        { kind: 'retail', count: 1 },
        { kind: 'complimentary', count: 1 },
        { kind: 'future', count: 1 },
      ],
    })
    const coverage = await screen.findByRole('region', { name: 'Capture coverage' })
    fact(coverage, 'Last transaction', '24 Nov 2023')
    fact(coverage, 'First licence', '1 Jan 2019')
    fact(coverage, 'Licences without a date', '2')
    expect(within(coverage).queryByText('First transaction')).toBeNull()
    const methods = screen.getByRole('region', { name: 'Licence acquisition methods' })
    for (const label of ['Gift or guest pass', 'Retail key', 'Complimentary', 'Unrecognised'])
      expect(within(methods).getByText(label)).toBeTruthy()
  })
  it('reopening Spending recomputes captured figures without waiting for the query cache to expire', async () => {
    const view = mount(mode, empty, true)
    expect(view.read).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Spending' }))
    await screen.findByText(/No Steam spending has been captured/)
    expect(view.read).toHaveBeenCalledTimes(1)
    fireEvent.click(screen.getByRole('button', { name: 'Gameplay' }))
    expect(screen.queryByRole('region', { name: 'Account spending' })).toBeNull()
    view.update({ ...empty, hasAnything: true, licenseCount: 3 })
    expect(view.read).toHaveBeenCalledTimes(1)
    fireEvent.click(screen.getByRole('button', { name: 'Spending' }))
    await screen.findByText('0 transactions · 3 licences.')
    expect(view.read).toHaveBeenCalledTimes(2)
    expect(screen.queryByText(/No Steam spending has been captured/)).toBeNull()
  })
  it('all refunded transactions retain a real 100 percent refund share without inventing a bundle denominator', async () => {
    mount(mode, {
      ...empty,
      hasAnything: true,
      currencySymbol: '$',
      transactionCount: 2,
      grossProductTransactionCount: 2,
      refundedProductTransactionCount: 2,
      grossProductSpendCents: 3000,
      refundedProductSpendCents: 3000,
    })
    await screen.findByText('100%')
    expect(screen.getAllByText('—')).toHaveLength(1)
    fact(screen.getByRole('region', { name: 'Spending in $' }), 'Net product spend', '$0.00')
    expect(screen.queryByText(/Average kept transaction/)).toBeNull()
  })
})
