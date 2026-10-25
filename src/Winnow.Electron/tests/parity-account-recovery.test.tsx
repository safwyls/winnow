// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AccountStatistics, type AccountStats } from '../src/renderer/features/Accounts'
import type { Mode } from '../src/renderer/api/types'
import type { ApiRequest } from '../src/shared/bridge'

// ActivityRecoveryInteractionTests' reader returns one transaction and no monetary facts.
const facts: AccountStats = {
  source: 'steam',
  hasAnything: true,
  isSingleCurrency: true,
  currencySymbol: null,
  currencyGroups: [],
  transactionCount: 1,
  licenseCount: 0,
  knownAccountCount: 0,
  unknownAccountFactCount: 0,
  transactionsWithoutCurrency: 0,
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
const key = ['api', 'statistics.account', { source: 'steam' }]
const clients: QueryClient[] = []
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
})
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((complete) => {
    resolve = complete
  })
  return { promise, resolve }
}
type Response = { ok: boolean; status: number; data?: AccountStats; message?: string }
function mount(mode: Mode, read: (input: ApiRequest) => Promise<Response>) {
  const request = vi.fn(read)
  const cancelRequest = vi.fn(async () => undefined)
  const back = vi.fn()
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request, cancelRequest } })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  clients.push(client)
  const element = (visible: boolean) => (
    <QueryClientProvider client={client}>
      <button onClick={back}>Back</button>
      {visible && <AccountStatistics mode={mode} />}
    </QueryClientProvider>
  )
  const view = render(element(true))
  return { client, request, cancelRequest, back, leave: () => view.rerender(element(false)) }
}

describe.each<Mode>(['desktop', 'fullscreen'])('%s account recovery source contracts', (mode) => {
  it('keeps Back focused through loading and failure then retries once with an enabled focused control', async () => {
    const first = deferred<Response>(),
      second = deferred<Response>()
    let reads = 0
    const view = mount(mode, () => (++reads === 1 ? first.promise : second.promise))
    await screen.findByText('Reading your account statistics…')
    const back = screen.getByRole('button', { name: 'Back' })
    back.focus()
    fireEvent.click(back)
    expect(view.back).toHaveBeenCalledOnce()
    await act(async () => first.resolve({ ok: false, status: 500, message: 'injected' }))
    await screen.findByText("Couldn't read Steam spending. Try again.")
    expect(document.activeElement).toBe(back)
    expect(screen.queryByText(/No Steam spending has been captured/)).toBeNull()
    const retry = screen.getByRole('button', { name: 'Try again' })
    retry.focus()
    fireEvent.click(retry)
    await waitFor(() => expect(view.request).toHaveBeenCalledTimes(2))
    expect(screen.getByRole('button', { name: 'Reading Steam spending…' })).toBe(retry)
    expect((retry as HTMLButtonElement).disabled).toBe(true)
    // jsdom does not reproduce Chromium blurring a button when it becomes disabled.
    document.body.tabIndex = -1
    document.body.focus()
    document.body.removeAttribute('tabindex')
    expect(document.activeElement).toBe(document.body)
    await act(async () => second.resolve({ ok: true, status: 200, data: facts }))
    await screen.findByText('1 transactions · 0 licences.')
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.getByRole('button', { name: 'Refresh Steam spending' })).toBe(retry)
    expect((retry as HTMLButtonElement).disabled).toBe(false)
    expect(document.activeElement).toBe(retry)
    expect(view.request).toHaveBeenCalledTimes(2)
  })

  it('keeps the focus chosen while a retry is still reading', async () => {
    const response = deferred<Response>()
    let reads = 0
    mount(mode, async () =>
      ++reads === 1 ? { ok: false, status: 500, message: 'injected' } : response.promise,
    )
    const retry = await screen.findByRole('button', { name: 'Try again' })
    retry.focus()
    fireEvent.click(retry)
    await waitFor(() => expect((retry as HTMLButtonElement).disabled).toBe(true))
    const back = screen.getByRole('button', { name: 'Back' })
    back.focus()
    await act(async () => response.resolve({ ok: true, status: 200, data: facts }))
    await screen.findByText('1 transactions · 0 licences.')
    expect(document.activeElement).toBe(back)
    expect(reads).toBe(2)
  })

  it.each(['cancel', 'dispose'] as const)(
    'cancels a held account read on %s and rejects an ignored late response',
    async (operation) => {
      const response = deferred<Response>()
      const view = mount(mode, () => response.promise)
      await screen.findByText('Reading your account statistics…')
      await waitFor(() => expect(view.request).toHaveBeenCalledOnce())
      const back = screen.getByRole('button', { name: 'Back' })
      back.focus()
      fireEvent.click(back)
      expect(view.back).toHaveBeenCalledOnce()
      const input = view.request.mock.calls[0]![0]
      expect(input.route).toBe('statistics.account')
      expect(input.params).toEqual({ source: 'steam' })
      expect(input.requestId).toMatch(/^[0-9a-f]{32}$/)
      if (operation === 'dispose') view.leave()
      else
        await act(async () => {
          await view.client.cancelQueries({ queryKey: key })
        })
      await waitFor(() => expect(view.cancelRequest).toHaveBeenCalledWith(input.requestId))
      await act(async () => response.resolve({ ok: true, status: 200, data: facts }))
      expect(view.client.getQueryData(key)).toBeUndefined()
      expect(screen.queryByText('1 transactions · 0 licences.')).toBeNull()
      expect(document.activeElement).toBe(back)
      expect(view.request).toHaveBeenCalledOnce()
      expect(view.cancelRequest).toHaveBeenCalledOnce()
    },
  )
})
