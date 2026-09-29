// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AccountStatistics } from '../src/renderer/features/Accounts'
import { clearViewState } from '../src/renderer/viewState'
import type { Mode } from '../src/renderer/api/types'
import { accountChartFixture, accountChartGroup } from './account-fixtures'

const clients: QueryClient[] = []
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  for (const mode of ['desktop', 'fullscreen']) clearViewState(`${mode}:stats:currency`)
})
function mount(mode: Mode) {
  let data = accountChartFixture()
  const read = vi.fn(async () => ({ ok: true, status: 200, data }))
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request: read } })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  clients.push(client)
  const view = (mode: Mode) => (
    <QueryClientProvider client={client}>
      <AccountStatistics mode={mode} />
    </QueryClientProvider>
  )
  const rendered = render(view(mode))
  return {
    read,
    update: (value: typeof data) => {
      data = value
    },
    mode: (mode: Mode) => rendered.rerender(view(mode)),
  }
}
async function choose(mode: Mode, symbol: string) {
  if (mode === 'desktop')
    fireEvent.change(await screen.findByLabelText('Chart and detail currency'), { target: { value: symbol } })
  else fireEvent.click(await screen.findByRole('button', { name: `${symbol} · show charts` }))
}
async function refresh() {
  fireEvent.click(screen.getByRole('button', { name: 'Refresh Steam spending' }))
  await screen.findByRole('button', { name: 'Refresh Steam spending' })
}
describe.each<Mode>(['desktop', 'fullscreen'])('account dashboard on %s', (mode) => {
  it('shares the chosen currency with charts and details, excludes wallet credit, and restores reading focus', async () => {
    mount(mode)
    await screen.findByText(/Average kept transaction: \$16.00/)
    const composition = screen.getByRole('region', { name: 'Product spending by kind' })
    expect(within(composition).getByText('$900.00 · 75%')).toBeTruthy()
    expect(within(composition).getByText('$200.00 · 16.7%')).toBeTruthy()
    expect(within(composition).getByText('$100.00 · 8.3%')).toBeTruthy()
    if (mode === 'desktop')
      expect(
        screen.getByText('Detailed spending breakdown', { selector: 'summary' }).closest('details')?.open,
      ).toBe(false)
    else expect(screen.queryByRole('table')).toBeNull()
    await choose(mode, '€')
    expect(screen.getByText('Highest recorded year: 2023 · €70.00')).toBeTruthy()
    expect(within(composition).getByText('€180.00 · 75%')).toBeTruthy()
    const opener =
      mode === 'desktop'
        ? screen.getByText('Detailed spending breakdown', { selector: 'summary' })
        : screen.getByRole('button', { name: 'Read spending details' })
    opener.focus()
    fireEvent.click(opener)
    const details = screen.getByRole('region', { name: 'Spending details in €' })
    expect(within(details).getByRole('row', { name: '2023 20 €70.00' })).toBeTruthy()
    expect(within(details).queryByText(/\$/)).toBeNull()
    if (mode === 'fullscreen') {
      fireEvent.click(screen.getByRole('button', { name: 'Back' }))
      await waitFor(() => expect(document.activeElement).toBe(opener))
    } else {
      fireEvent.click(opener)
      expect(opener.closest('details')?.open).toBe(false)
    }
    expect(screen.getByText(/Percentages count transactions, not games or money/)).toBeTruthy()
  })
  it('keeps currency on refresh, selects a remaining currency when removed, and has independent surface state', async () => {
    const view = mount(mode)
    await choose(mode, '€')
    await refresh()
    expect(view.read).toHaveBeenCalledTimes(2)
    expect(screen.getByText('Highest recorded year: 2023 · €70.00')).toBeTruthy()
    const other = mode === 'desktop' ? 'fullscreen' : 'desktop'
    view.mode(other)
    expect(screen.getByText('Highest recorded year: 2023 · $350.00')).toBeTruthy()
    view.mode(mode)
    expect(screen.getByText('Highest recorded year: 2023 · €70.00')).toBeTruthy()
    view.update({ ...accountChartFixture(), currencyGroups: [accountChartGroup('$', 10)] })
    await refresh()
    await screen.findByText('Highest recorded year: 2023 · $350.00')
    view.update(accountChartFixture())
    await refresh()
    expect(screen.getByText('Highest recorded year: 2023 · $350.00')).toBeTruthy()
  })
})
