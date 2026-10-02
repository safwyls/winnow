import { useEffect, useRef, useState, type ReactNode } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { useQuery } from '@tanstack/react-query'
import { request } from '../api/client'
import type { Mode } from '../api/types'
import { useViewState } from '../viewState'
import { Empty, Notice } from './shared'
import './accounts.css'
import { AcquisitionExport } from './AcquisitionExport'

interface Slice {
  count: number
  cents: number
}
export interface AccountStats {
  source: string
  hasAnything: boolean
  isSingleCurrency: boolean
  currencySymbol: string | null
  currencyGroups: AccountStats[]
  transactionCount: number
  licenseCount: number
  knownAccountCount: number
  unknownAccountFactCount: number
  transactionsWithoutCurrency: number
  currencies?: { symbol: string; transactionCount: number }[]
  transactionsWithoutDate?: number
  licensesWithoutDate?: number
  grossProductSpendCents: number
  refundedProductSpendCents: number
  netProductSpendCents: number
  grossProductTransactionCount: number
  refundedProductTransactionCount: number
  netProductTransactionCount: number
  purchases: Slice
  giftPurchases: Slice
  inGamePurchases: Slice
  bundlePurchases: Slice
  refundTransactions: Slice
  walletCreditPurchases: Slice
  walletCreditRedemptions: Slice
  discountedPurchases: Slice
  discountedPurchaseListCents: number
  firstTransactionAt?: string | null
  lastTransactionAt?: string | null
  firstLicenseAt?: string | null
  lastLicenseAt?: string | null
  spendByYear: { year: number; transactionCount: number; cents: number }[]
  undatedNetSpendCents: number
  undatedNetTransactionCount: number
  licenseAcquisitions: { kind: string | null; count: number }[]
  biggestPurchase?: {
    cents: number
    itemNames: string[]
    itemCount: number
    currencySymbol?: string | null
    occurredAt?: string | null
    isBundle: boolean
  } | null
}
export const capturedMoney = (cents: number, symbol: string | null) =>
  `${symbol ?? ''}${(cents / 100).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`

// Captured account dates are page dates, not instants to move into the viewer's timezone.
const capturedDate = (value: string) =>
  new Date(`${value.slice(0, 10)}T00:00:00Z`).toLocaleDateString('en-GB', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    timeZone: 'UTC',
  })
const licenceLabels: Record<string, string> = {
  steam_store: 'Steam Store',
  complimentary: 'Complimentary',
  gift: 'Gift or guest pass',
  retail: 'Retail key',
}
const licenceLabel = (kind: string | null) => licenceLabels[kind ?? ''] ?? 'Unrecognised'

function LargestTransaction({ value, allowMoney = true }: { value: AccountStats; allowMoney?: boolean }) {
  const purchase = value.biggestPurchase
  if (!purchase) return null
  const symbol = purchase.currencySymbol ?? (value.isSingleCurrency ? value.currencySymbol : null)
  return (
    <section aria-label="Largest transaction">
      <h4>Largest transaction</h4>
      {allowMoney && symbol && <p>{capturedMoney(purchase.cents, symbol)}</p>}
      <p>
        {purchase.itemNames.join(', ')}
        {purchase.isBundle ? ' (bundle)' : ''}
      </p>
      {purchase.occurredAt && <p>{capturedDate(purchase.occurredAt)}</p>}
      {purchase.isBundle && (
        <p className="muted reading-prose">
          This amount belongs to the whole bundle. No per-game price is inferred.
        </p>
      )}
    </section>
  )
}

function CaptureCoverage({ value }: { value: AccountStats }) {
  const dates = [
    ['First transaction', value.firstTransactionAt],
    ['Last transaction', value.lastTransactionAt],
    ['First licence', value.firstLicenseAt],
    ['Last licence', value.lastLicenseAt],
  ] as const
  const currencies = [
    ...(value.currencies ?? []).map((row) => [row.symbol, row.transactionCount] as const),
    ...(value.transactionsWithoutCurrency > 0
      ? [['No currency symbol', value.transactionsWithoutCurrency] as const]
      : []),
  ]
  return (
    <section aria-label="Capture coverage">
      <dl className="capture-facts">
        {dates
          .filter(([, date]) => date)
          .map(([label, date]) => (
            <div key={label}>
              <dt>{label}</dt>
              <dd>{capturedDate(date!)}</dd>
            </div>
          ))}
        {!!value.transactionsWithoutDate && (
          <div>
            <dt>Transactions without a date</dt>
            <dd>{value.transactionsWithoutDate}</dd>
          </div>
        )}
        {!!value.licensesWithoutDate && (
          <div>
            <dt>Licences without a date</dt>
            <dd>{value.licensesWithoutDate}</dd>
          </div>
        )}
      </dl>
      {(currencies.length > 1 || (currencies.length > 0 && !value.isSingleCurrency)) && (
        <section aria-label="Captured currencies">
          <h3>Currencies</h3>
          <dl className="capture-facts">
            {currencies.map(([label, count]) => (
              <div key={label}>
                <dt>{label}</dt>
                <dd>{count}</dd>
              </div>
            ))}
          </dl>
        </section>
      )}
    </section>
  )
}

function CapturedCounts({ value }: { value: AccountStats }) {
  const counts: [string, number][] = [
    ['Net product transactions', value.netProductTransactionCount],
    ['Before refunds', value.grossProductTransactionCount],
    ['Refunded purchases', value.refundedProductTransactionCount],
    ['Single-item purchases', value.purchases.count],
    ['Bundles', value.bundlePurchases.count],
    ['Gifts given', value.giftPurchases.count],
    ['In-game purchases', value.inGamePurchases.count],
    ['Separate refund transactions', value.refundTransactions.count],
    ['Wallet top-ups', value.walletCreditPurchases.count],
    ['Redeemed wallet credit', value.walletCreditRedemptions.count],
    ...value.spendByYear.map((year) => [String(year.year), year.transactionCount] as [string, number]),
    ...(value.undatedNetTransactionCount > 0
      ? [['Undated', value.undatedNetTransactionCount] as [string, number]]
      : []),
  ]
  return (
    <section className="feature-panel" aria-label="Captured transaction counts">
      <h3>Captured transactions</h3>
      <p className="muted reading-prose">
        Counts remain available. Amounts from different currencies are never combined.
      </p>
      <dl className="capture-facts">
        {counts
          .filter(([, count]) => count > 0)
          .map(([label, count]) => (
            <div key={label}>
              <dt>{label}</dt>
              <dd>{count}</dd>
            </div>
          ))}
      </dl>
    </section>
  )
}

function AccountChart({
  title,
  rows,
}: {
  title: string
  rows: { label: string; value: number; formatted: string }[]
}) {
  const minimum = Math.min(0, ...rows.map((row) => row.value))
  const maximum = Math.max(0, ...rows.map((row) => row.value))
  const range = Math.max(1, maximum - minimum)
  const zero = (-minimum / range) * 100
  return (
    <section className="account-chart" aria-label={title}>
      <h3>{title}</h3>
      <ul>
        {rows.map((row) => (
          <li key={row.label}>
            <span>{row.label}</span>
            <span className="account-chart-track" aria-hidden="true">
              <span className="account-chart-zero" style={{ left: `${zero}%` }} />
              <span
                className={`account-chart-bar${row.value < 0 ? ' negative' : ''}`}
                style={{
                  left: `${((Math.min(0, row.value) - minimum) / range) * 100}%`,
                  width: `${(Math.abs(row.value) / range) * 100}%`,
                }}
              />
            </span>
            <strong>{row.formatted}</strong>
          </li>
        ))}
      </ul>
    </section>
  )
}

function AccountCharts({ value }: { value: AccountStats }) {
  const money = (cents: number) => capturedMoney(cents, value.currencySymbol)
  const kinds = [
    ['Purchases', value.purchases],
    ['Gifts bought for others', value.giftPurchases],
    ['In-game purchases', value.inGamePurchases],
  ] as const
  const negativeKinds = kinds.some(([, slice]) => slice.cents < 0)
  const peak = [...value.spendByYear].sort((a, b) => b.cents - a.cents)[0]
  return (
    <section className="account-charts" aria-label="Captured spending charts">
      <div className="feature-grid">
        <AccountChart
          title="Spending by year"
          rows={value.spendByYear.map((row) => ({
            label: String(row.year),
            value: row.cents,
            formatted: money(row.cents),
          }))}
        />
        {!negativeKinds && (
          <CompositionChart
            rows={kinds
              .filter(([, slice]) => slice.cents > 0)
              .map(([label, slice]) => ({ label, value: slice.cents, formatted: money(slice.cents) }))}
          />
        )}
      </div>
      {negativeKinds && (
        <p className="reading-prose">
          Some categories have negative totals. Their signed amounts are shown in the spending breakdown.
        </p>
      )}
      {peak && (
        <p>
          Highest recorded year: {peak.year} · {money(peak.cents)}
        </p>
      )}
      {value.netProductTransactionCount > 0 && (
        <p>
          Average kept transaction: {money(value.netProductSpendCents / value.netProductTransactionCount)} ·
          bundles count as one transaction.
        </p>
      )}
    </section>
  )
}

function CompositionChart({ rows }: { rows: { label: string; value: number; formatted: string }[] }) {
  const total = rows.reduce((sum, row) => sum + row.value, 0)
  let offset = 0
  return (
    <section className="account-chart account-composition" aria-label="Product spending by kind">
      <h3>Where the money went</h3>
      <p className="muted reading-prose">Kept product transactions. Wallet credit is excluded.</p>
      {total > 0 ? (
        <div className="account-donut-layout">
          <svg className="account-donut" viewBox="0 0 160 160" aria-hidden="true">
            {rows.map((row, index) => {
              const start = offset
              const share = (row.value / total) * 100
              offset += share
              return (
                <circle
                  key={row.label}
                  className={`account-ink-${index}`}
                  cx="80"
                  cy="80"
                  r="64"
                  pathLength="100"
                  fill="none"
                  strokeWidth="24"
                  strokeDasharray={`${share} ${100 - share}`}
                  strokeDashoffset={-start}
                  transform="rotate(-90 80 80)"
                />
              )
            })}
          </svg>
          <ul>
            {rows.map((row, index) => (
              <li key={row.label}>
                <span>
                  <i className={`account-ink-${index}`} aria-hidden="true" />
                  {row.label}
                </span>
                <strong>
                  {row.formatted} · {Number(((100 * row.value) / total).toFixed(1))}%
                </strong>
              </li>
            ))}
          </ul>
        </div>
      ) : (
        <p>No kept product amounts were recorded.</p>
      )}
    </section>
  )
}

function SpendSummary({ value }: { value: AccountStats }) {
  const money = (cents: number) => capturedMoney(cents, value.currencySymbol)
  const productMoney = (cents: number) =>
    value.grossProductTransactionCount > 0 ? money(cents) : 'Not available'
  return (
    <section
      className="feature-panel"
      aria-label={`Spending in ${value.currencySymbol ?? 'captured currency'}`}
    >
      <h3>{value.currencySymbol ?? 'Captured currency'}</h3>
      <dl className="facts-grid">
        <div>
          <dt>Net product spend</dt>
          <dd>{productMoney(value.netProductSpendCents)}</dd>
          <dd className="transaction-count">{value.netProductTransactionCount} transactions</dd>
        </div>
        <div>
          <dt>Before refunds</dt>
          <dd>{productMoney(value.grossProductSpendCents)}</dd>
          <dd className="transaction-count">{value.grossProductTransactionCount} transactions</dd>
        </div>
        <div>
          <dt>Refunded purchases</dt>
          <dd>{productMoney(value.refundedProductSpendCents)}</dd>
          <dd className="transaction-count">{value.refundedProductTransactionCount} transactions</dd>
        </div>
      </dl>
    </section>
  )
}

function SpendDetails({ value }: { value: AccountStats }) {
  const money = (cents: number) => capturedMoney(cents, value.currencySymbol)
  const slices: [string, Slice][] = [
    ['Single-item purchases', value.purchases],
    ['Bundles', value.bundlePurchases],
    ['Gifts given', value.giftPurchases],
    ['In-game purchases', value.inGamePurchases],
    ['Separate refund transactions', value.refundTransactions],
    ['Wallet top-ups', value.walletCreditPurchases],
    ['Redeemed wallet credit', value.walletCreditRedemptions],
  ]
  return (
    <section aria-label={`Spending details in ${value.currencySymbol}`}>
      <p className="reading-prose">
        Currency: {value.currencySymbol}. Amounts describe captured transactions.
      </p>
      <table aria-label="Spending by kind details">
        <thead>
          <tr>
            <th scope="col">Kind</th>
            <th scope="col">Transactions</th>
            <th scope="col">Amount</th>
          </tr>
        </thead>
        <tbody>
          {slices
            .filter(([, slice]) => slice?.count > 0)
            .map(([label, slice]) => (
              <tr key={label}>
                <th scope="row">{label}</th>
                <td>{slice.count}</td>
                <td>{money(slice.cents)}</td>
              </tr>
            ))}
        </tbody>
      </table>
      <p className="muted reading-prose">
        Wallet credit stays separate from product spend. Separate refund rows are not subtracted a second
        time. Bundle amounts belong to the whole transaction.
      </p>
      {((value.spendByYear?.length ?? 0) > 0 || value.undatedNetTransactionCount > 0) && (
        <>
          <h4>By year</h4>
          <table aria-label="Spending by year details">
            <thead>
              <tr>
                <th>Year</th>
                <th>Transactions</th>
                <th>Net spend</th>
              </tr>
            </thead>
            <tbody>
              {value.spendByYear.map((year) => (
                <tr key={year.year}>
                  <th scope="row">{year.year}</th>
                  <td>{year.transactionCount}</td>
                  <td>{money(year.cents)}</td>
                </tr>
              ))}
              {value.undatedNetTransactionCount > 0 && (
                <tr>
                  <th scope="row">Undated</th>
                  <td>{value.undatedNetTransactionCount}</td>
                  <td>{money(value.undatedNetSpendCents)}</td>
                </tr>
              )}
            </tbody>
          </table>
        </>
      )}
      <LargestTransaction value={value} />
      {value.discountedPurchases?.count > 0 && (
        <p className="reading-prose">
          On {value.discountedPurchases.count} purchases with a captured list price:{' '}
          {money(value.discountedPurchases.cents)} paid against {money(value.discountedPurchaseListCents)}{' '}
          listed. This is not total savings.
        </p>
      )}
    </section>
  )
}

function SpendingDetails({ value, mode }: { value: AccountStats; mode: Mode }) {
  if (mode === 'desktop')
    return (
      <details className="feature-panel account-breakdown">
        <summary>Detailed spending breakdown</summary>
        <SpendDetails value={value} />
      </details>
    )
  return (
    <Dialog.Root>
      <Dialog.Trigger asChild>
        <button>Read spending details</button>
      </Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay" />
        <Dialog.Content
          className="dialog-content account-reading mode-fullscreen"
          aria-describedby={undefined}
        >
          <header className="feature-heading">
            <Dialog.Title>Spending details · {value.currencySymbol}</Dialog.Title>
            <Dialog.Close asChild>
              <button>Back</button>
            </Dialog.Close>
          </header>
          <div className="account-reading-body" role="region" tabIndex={0} aria-label="Spending breakdown">
            <SpendDetails value={value} />
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}

export function AccountStatistics({
  mode = 'desktop',
  toolbar,
}: {
  mode?: Mode
  toolbar?: (actions: ReactNode) => ReactNode
}) {
  const refreshButton = useRef<HTMLButtonElement>(null)
  const restoreRefreshFocus = useRef(false)
  const stats = useQuery({
    queryKey: ['api', 'statistics.account', { source: 'steam' }],
    queryFn: ({ signal }) =>
      request<AccountStats>('statistics.account', { source: 'steam' }, undefined, signal),
    retry: false,
    refetchOnMount: 'always',
    staleTime: 30_000,
  })
  useEffect(() => {
    const preserveUserFocus = (event: FocusEvent) => {
      if (event.target !== refreshButton.current && event.target !== document.body)
        restoreRefreshFocus.current = false
    }
    document.addEventListener('focusin', preserveUserFocus)
    return () => document.removeEventListener('focusin', preserveUserFocus)
  }, [])
  useEffect(() => {
    if (stats.isFetching || !restoreRefreshFocus.current) return
    restoreRefreshFocus.current = false
    // Chromium can blur a newly disabled button. Restore only that lost focus;
    // a user who moved to another control keeps their chosen position.
    if (document.activeElement === document.body || document.activeElement === refreshButton.current)
      refreshButton.current?.focus({ preventScroll: true })
  }, [stats.isFetching])
  const [selected, setSelected] = useViewState<string | null>(`${mode}:stats:currency`, null)
  const data = stats.data
  const ambiguous = !!data && data.knownAccountCount > 0 && data.unknownAccountFactCount > 0
  const groups = data
    ? data.currencyGroups?.length
      ? data.currencyGroups
      : data.isSingleCurrency && data.currencySymbol
        ? [data]
        : []
    : []
  const selectedGroup = groups.find((group) => group.currencySymbol === selected) ?? groups[0]
  const selectedSymbol = selectedGroup?.currencySymbol ?? null
  useEffect(() => {
    if (data && selected !== selectedSymbol) setSelected(selectedSymbol)
  }, [data, selected, selectedSymbol, setSelected])
  const percentage = (numerator: number, denominator: number) =>
    !ambiguous && denominator > 0 && numerator >= 0 && numerator <= denominator
      ? `${Number(((100 * numerator) / denominator).toFixed(1))}%`
      : '—'
  const actions = (
    <div className="account-actions">
      <button
        ref={refreshButton}
        disabled={stats.isFetching}
        onClick={() => {
          restoreRefreshFocus.current = document.activeElement === refreshButton.current
          void stats.refetch()
        }}
      >
        {stats.isFetching
          ? 'Reading Steam spending…'
          : stats.isError
            ? 'Try again'
            : 'Refresh Steam spending'}
      </button>
      <AcquisitionExport label="Export acquisitions" />
    </div>
  )
  const figures = (
    <div className="feature-grid account-spending-figures">
      {groups.map((group) => (
        <SpendSummary key={group.currencySymbol} value={group} />
      ))}
    </div>
  )
  return (
    <section
      aria-label="Account spending"
      className={`account-statistics${toolbar ? ' account-statistics-embedded' : ''}`}
      data-mode={mode}
    >
      {toolbar ? (
        <>
          {toolbar(actions)}
          <div className="account-spending-intro">
            <p>Source: Steam account pages. Spending imports for other stores are not available.</p>
            <p>Totals of the Steam account pages that were read, not of the whole account.</p>
          </div>
        </>
      ) : (
        <header className="feature-heading">
          <div>
            <h2>What you brought home</h2>
            <p className="reading-prose">Spending and licences from your captured Steam account pages.</p>
          </div>
          {actions}
        </header>
      )}
      {stats.isError && (
        <p className="error-message" role="alert">
          Couldn't read Steam spending. Try again.
        </p>
      )}
      {stats.isPending ? (
        <p role="status">Reading your account statistics…</p>
      ) : data?.hasAnything ? (
        <>
          {toolbar && !ambiguous && figures}
          <p>
            {data.transactionCount} transactions · {data.licenseCount} licences
            {data.knownAccountCount > 1 ? ` · ${data.knownAccountCount} identified accounts` : ''}.
          </p>
          <dl className="facts-grid">
            <div>
              <dt>Refunded share of product purchases</dt>
              <dd>{percentage(data.refundedProductTransactionCount, data.grossProductTransactionCount)}</dd>
            </div>
            <div>
              <dt>Bundle share of net product purchases</dt>
              <dd>{percentage(data.bundlePurchases.count, data.netProductTransactionCount)}</dd>
            </div>
          </dl>
          <p className="muted reading-prose">
            Product transactions with recorded prices only. Wallet credit and standalone refund rows are
            excluded. Percentages count transactions, not games or money; missing-price rows and uncaptured
            pages are outside these figures.
          </p>
          <CaptureCoverage value={data} />
          <p className="muted reading-prose">
            These totals cover only the pages you captured. Purchases from other shops are absent. Currencies
            are never converted or added together.
          </p>
          {data.transactionsWithoutCurrency > 0 && (
            <p className="reading-prose">
              {data.transactionsWithoutCurrency} transactions have no currency and are excluded from monetary
              totals.
            </p>
          )}
          {data.unknownAccountFactCount > 0 && (
            <p className="reading-prose">
              {data.unknownAccountFactCount} {data.unknownAccountFactCount === 1 ? 'fact has' : 'facts have'}{' '}
              no captured account identity. Signing in does not assign them to an account.
            </p>
          )}
          {ambiguous ? (
            <p className="reading-prose">
              Records with an unknown account may overlap identified captures. Monetary totals and percentages
              are unavailable; counts describe captured records.
            </p>
          ) : (
            <>
              {!toolbar && figures}
              {groups.length > 1 &&
                (mode === 'desktop' ? (
                  <label className="field account-currency">
                    Chart and detail currency
                    <select
                      value={selectedSymbol ?? ''}
                      onChange={(event) => setSelected(event.target.value)}
                    >
                      {groups.map((group) => (
                        <option key={group.currencySymbol} value={group.currencySymbol ?? ''}>
                          {group.currencySymbol}
                        </option>
                      ))}
                    </select>
                  </label>
                ) : (
                  <div
                    className="account-currency account-actions"
                    role="group"
                    aria-label="Chart and detail currency"
                  >
                    {groups.map((group) => (
                      <button
                        key={group.currencySymbol}
                        aria-pressed={group.currencySymbol === selectedSymbol}
                        onClick={() => setSelected(group.currencySymbol)}
                      >
                        {group.currencySymbol} ·{' '}
                        {group.currencySymbol === selectedSymbol ? 'selected' : 'show charts'}
                      </button>
                    ))}
                  </div>
                ))}
              {selectedGroup && (
                <>
                  <AccountCharts value={selectedGroup} />
                  <SpendingDetails value={selectedGroup} mode={mode} />
                </>
              )}
            </>
          )}
          {(ambiguous || groups.length === 0) && (
            <>
              <CapturedCounts value={data} />
              <LargestTransaction value={data} allowMoney={!ambiguous} />
            </>
          )}
          {!!data.licenseAcquisitions?.length && (
            <AccountChart
              title="Licence acquisition methods"
              rows={data.licenseAcquisitions.map((row) => ({
                label: licenceLabel(row.kind),
                value: row.count,
                formatted: String(row.count),
              }))}
            />
          )}
          <section className="feature-panel">
            <h3>How licences arrived</h3>
            <ul>
              {data.licenseAcquisitions?.map((item) => (
                <li key={item.kind ?? 'unknown'}>
                  {licenceLabel(item.kind)}: {item.count}
                </li>
              ))}
            </ul>
            <p className="muted reading-prose">
              Licence counts describe packages, which may contain more than one game.
            </p>
          </section>
        </>
      ) : (
        !stats.error && (
          <Empty className="reading-prose">
            No Steam spending has been captured. Import your saved purchase-history and licence pages in
            Settings → Platforms.
          </Empty>
        )
      )}
    </section>
  )
}

export { SteamPageImport } from './SteamAccountImport'
