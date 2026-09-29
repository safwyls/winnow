// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AccountStatistics, SteamPageImport, type AccountStats } from '../src/renderer/features/Accounts'

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})
const slice = { count: 0, cents: 0 }
const base: AccountStats = {
  source: 'steam',
  hasAnything: true,
  isSingleCurrency: true,
  currencySymbol: '$',
  currencyGroups: [],
  transactionCount: 3,
  licenseCount: 4,
  knownAccountCount: 1,
  unknownAccountFactCount: 0,
  transactionsWithoutCurrency: 0,
  grossProductSpendCents: 6000,
  refundedProductSpendCents: 3000,
  netProductSpendCents: 3000,
  grossProductTransactionCount: 3,
  refundedProductTransactionCount: 1,
  netProductTransactionCount: 2,
  purchases: { count: 1, cents: 2000 },
  bundlePurchases: { count: 1, cents: 1000 },
  giftPurchases: slice,
  inGamePurchases: slice,
  refundTransactions: slice,
  walletCreditPurchases: { count: 1, cents: 90000 },
  walletCreditRedemptions: slice,
  discountedPurchases: slice,
  discountedPurchaseListCents: 0,
  spendByYear: [],
  undatedNetSpendCents: 0,
  undatedNetTransactionCount: 0,
  licenseAcquisitions: [],
}
function mount(node: React.ReactNode, handler: (route: string, body: unknown) => unknown) {
  const request = vi.fn(async ({ route, body }) => ({ ok: true, status: 200, data: handler(route, body) }))
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request, openExternal: vi.fn(async () => {}) },
  })
  render(
    <QueryClientProvider
      client={new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })}
    >
      {node}
    </QueryClientProvider>,
  )
  return request
}
describe('account summary parity with AccountStatsSummaryTests', () => {
  it.each(['desktop', 'fullscreen'])(
    'Statistics_render_known_and_unknown_account_scope_without_ambiguous_money_totals on %s',
    async (mode) => {
      mount(<div className={`mode-${mode}`}><AccountStatistics /></div>, () => ({
        ...base, knownAccountCount: 2, unknownAccountFactCount: 1, transactionCount: 3,
        purchases: { count: 3, cents: 1500 }, netProductSpendCents: 1500,
        grossProductSpendCents: 1500, refundedProductSpendCents: 0,
        spendByYear: [{ year: 2026, transactionCount: 3, cents: 1500 }],
      }))
      await screen.findByText(/2 identified accounts/)
      expect(screen.getByText(/unknown account may overlap identified/)).toBeTruthy()
      expect(screen.getByText(/1 facts have no captured account identity/)).toBeTruthy()
      expect(screen.queryByText('$15.00')).toBeNull()
      expect(screen.queryByRole('table')).toBeNull()
      expect(screen.queryByRole('region', { name: 'Captured spending charts' })).toBeNull()
      expect(screen.getAllByText('—')).toHaveLength(2)
    },
  )
  it.each(['desktop', 'fullscreen'])('keeps currencies and wallet funding separate on %s', async (mode) => {
    const value = {
      ...base,
      isSingleCurrency: false,
      currencySymbol: null,
      netProductSpendCents: 999999,
      currencyGroups: [
        { ...base, currencySymbol: '$' },
        { ...base, currencySymbol: '€', netProductSpendCents: 1200 },
      ],
    }
    const request = mount(
      <div className={`mode-${mode}`}>
        <AccountStatistics />
      </div>,
      () => value,
    )
    await screen.findByText('33.3%')
    expect(screen.getByText('50%')).toBeTruthy()
    expect(within(screen.getByRole('region', { name: 'Spending in $' })).getAllByText('$30.00')).toHaveLength(
      2,
    )
    expect(within(screen.getByRole('region', { name: 'Spending in €' })).getByText('€12.00')).toBeTruthy()
    expect(screen.queryByText(/9,999.99/)).toBeNull()
    expect(request).toHaveBeenCalledWith({
      route: 'statistics.account',
      params: { source: 'steam' },
      body: undefined,
      requestId: expect.stringMatching(/^[a-f0-9]{32}$/),
    })
  })
  it('does not invent percentages or currency totals for unknown prices', async () => {
    mount(<AccountStatistics />, () => ({
      ...base,
      isSingleCurrency: false,
      currencySymbol: null,
      grossProductTransactionCount: 0,
      netProductTransactionCount: 0,
      transactionsWithoutCurrency: 3,
      unknownAccountFactCount: 3,
    }))
    await screen.findByText(/3 transactions have no currency/)
    expect(screen.getAllByText('—')).toHaveLength(2)
    expect(screen.queryByRole('table')).toBeNull()
    expect(screen.getByText(/Signing in does not assign/)).toBeTruthy()
  })
  it('shows an empty state for a library without captures', async () => {
    mount(<AccountStatistics />, () => ({ ...base, hasAnything: false }))
    await screen.findByText(/No Steam spending has been captured/)
  })
  it('does not represent missing product prices as zero spend', async () => {
    mount(<AccountStatistics />, () => ({
      ...base,
      grossProductTransactionCount: 0,
      netProductTransactionCount: 0,
      refundedProductTransactionCount: 0,
      grossProductSpendCents: 0,
      refundedProductSpendCents: 0,
      netProductSpendCents: 0,
      purchases: slice,
      bundlePurchases: slice,
    }))
    const spending = await screen.findByRole('region', { name: 'Spending in $' })
    expect(within(spending).getAllByText('Not available')).toHaveLength(3)
    expect(within(spending).getByText('$900.00')).toBeTruthy()
    expect(screen.queryByText('0%')).toBeNull()
  })
  it('Negative_amounts_stay_signed_and_zero_years_remain_recorded_facts', async () => {
    mount(<AccountStatistics />, () => ({
      ...base,
      purchases: { count: 1, cents: 10000 },
      inGamePurchases: { count: 1, cents: -2000 },
      spendByYear: [
        { year: 2024, transactionCount: 1, cents: -2000 },
        { year: 2025, transactionCount: 1, cents: 0 },
      ],
    }))
    const years = await screen.findByRole('region', { name: 'Spending by year' })
    expect(within(years).getByText('$-20.00')).toBeTruthy()
    expect(within(years).getByText('$0.00')).toBeTruthy()
    expect(screen.queryByRole('region', { name: 'Product spending by kind' })).toBeNull()
    expect(screen.getByText(/Some categories have negative totals/)).toBeTruthy()
    expect(screen.getByText('Highest recorded year: 2025 · $0.00')).toBeTruthy()
  })
  it('Overlapping_accounts_with_currency_groups_suppress_money_charts_but_keep_licence_counts', async () => {
    mount(<AccountStatistics />, () => ({
      ...base,
      unknownAccountFactCount: 1,
      currencyGroups: [base],
      licenseAcquisitions: [{ kind: 'retail', count: 5 }],
    }))
    const licences = await screen.findByRole('region', { name: 'Licence acquisition methods' })
    expect(within(licences).getByText('5')).toBeTruthy()
    expect(screen.queryByRole('region', { name: 'Captured spending charts' })).toBeNull()
    expect(screen.queryByRole('region', { name: 'Spending in $' })).toBeNull()
    expect(screen.queryByText('33.3%')).toBeNull()
  })
  it('withholds monetary totals and percentages when unknown-account captures may overlap known accounts', async () => {
    mount(<AccountStatistics />, () => ({ ...base, unknownAccountFactCount: 1 }))
    await screen.findByText(/may overlap identified captures/)
    expect(screen.getAllByText('—')).toHaveLength(2)
    expect(screen.queryByText('$30.00')).toBeNull()
    expect(screen.queryByRole('region', { name: 'Captured spending charts' })).toBeNull()
  })
  it.each(['desktop', 'fullscreen'])(
    'switches chart currencies without blending amounts or losing focus on %s',
    async (mode) => {
      const usd = { ...base, spendByYear: [{ year: 2025, transactionCount: 2, cents: 3000 }] }
      const euro = {
        ...base,
        currencySymbol: '€',
        spendByYear: [{ year: 2025, transactionCount: 2, cents: 1200 }],
      }
      mount(
        <div className={`mode-${mode}`}>
          <AccountStatistics />
        </div>,
        () => ({ ...base, isSingleCurrency: false, currencySymbol: null, currencyGroups: [usd, euro] }),
      )
      const currency = await screen.findByLabelText('Chart currency')
      currency.focus()
      fireEvent.change(currency, { target: { value: '€' } })
      const chart = screen.getByRole('region', { name: 'Spending by year' })
      expect(within(chart).getByText('€12.00')).toBeTruthy()
      expect(within(chart).queryByText('$30.00')).toBeNull()
      expect(document.activeElement).toBe(currency)
    },
  )
})
describe('saved licence-page import parity', () => {
  it('imports selected saved pages immediately and reports partial results', async () => {
    const pages = { licensesHtml: '<html>fixture</html>', source: 1, capturedAt: '2026-09-01T00:00:00Z' }
    const request = mount(<SteamPageImport />, (route) =>
      route === 'imports.steam.load'
        ? { pages, anythingLoaded: true, files: [{ path: 'licenses.html', outcome: 0, kind: 0 }] }
        : {
            licensesOutcome: 'Imported',
            historyOutcome: 'NotSupplied',
            licenseFactsRecorded: 2,
            transactionFactsRecorded: 0,
            ownershipsFilled: 1,
            licenseFactsAlreadyRecorded: 1,
            transactionFactsAlreadyRecorded: 0,
            licensesTruncated: true,
          },
    )
    const file = new File(['<html>fixture</html>'], 'licenses.html', { type: 'text/html' })
    Object.defineProperty(file, 'arrayBuffer', {
      value: async () => new TextEncoder().encode('<html>fixture</html>').buffer,
    })
    fireEvent.change(screen.getByLabelText('Saved Steam pages'), { target: { files: [file] } })
    await screen.findByText('licenses.html: Licence page ready')
    await screen.findByText(/This capture is incomplete/)
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith({ route: 'imports.steam.pages', params: undefined, body: pages }),
    )
  })
})
