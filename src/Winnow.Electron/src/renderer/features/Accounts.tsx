import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { request } from '../api/client'
import { Empty, Notice } from './shared'
import './accounts.css'

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
        <p className="muted">This amount belongs to the whole bundle. No per-game price is inferred.</p>
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
      <p className="muted">Counts remain available. Amounts from different currencies are never combined.</p>
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
  const max = Math.max(1, ...rows.map((row) => Math.abs(row.value)))
  return (
    <section className="account-chart" aria-label={title}>
      <h3>{title}</h3>
      <ul>
        {rows.map((row) => (
          <li key={row.label}>
            <span>{row.label}</span>
            <span
              className={`account-chart-bar${row.value < 0 ? ' negative' : ''}`}
              style={{ width: `${(Math.abs(row.value) / max) * 100}%` }}
              aria-hidden="true"
            />
            <strong>{row.formatted}</strong>
          </li>
        ))}
      </ul>
    </section>
  )
}

function AccountCharts({ groups }: { groups: AccountStats[] }) {
  const [selected, setSelected] = useState<string | null>(null)
  const value = groups.find((group) => group.currencySymbol === selected) ?? groups[0]
  if (!value) return null
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
      {groups.length > 1 && (
        <label className="field">
          Chart currency
          <select value={value.currencySymbol ?? ''} onChange={(event) => setSelected(event.target.value)}>
            {groups.map((group) => (
              <option key={group.currencySymbol} value={group.currencySymbol ?? ''}>
                {group.currencySymbol}
              </option>
            ))}
          </select>
        </label>
      )}
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
          <AccountChart
            title="Product spending by kind"
            rows={kinds
              .filter(([, slice]) => slice.cents > 0)
              .map(([label, slice]) => ({ label, value: slice.cents, formatted: money(slice.cents) }))}
          />
        )}
      </div>
      {negativeKinds && (
        <p>Some categories have negative totals. Their signed amounts are shown in the spending breakdown.</p>
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

function SpendGroup({ value }: { value: AccountStats }) {
  const money = (cents: number) => capturedMoney(cents, value.currencySymbol)
  const productMoney = (cents: number) =>
    value.grossProductTransactionCount > 0 ? money(cents) : 'Not available'
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
      <table>
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
      <p className="muted">
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
        <p>
          On {value.discountedPurchases.count} purchases with a captured list price:{' '}
          {money(value.discountedPurchases.cents)} paid against {money(value.discountedPurchaseListCents)}{' '}
          listed. This is not total savings.
        </p>
      )}
    </section>
  )
}

export function AccountStatistics() {
  const stats = useQuery({
    queryKey: ['api', 'statistics.account', { source: 'steam' }],
    queryFn: ({ signal }) =>
      request<AccountStats>('statistics.account', { source: 'steam' }, undefined, signal),
    retry: false,
    refetchOnMount: 'always',
    staleTime: 30_000,
  })
  const [error, setError] = useState<unknown>(null)
  const [message, setMessage] = useState('')
  const data = stats.data
  const ambiguous = !!data && data.knownAccountCount > 0 && data.unknownAccountFactCount > 0
  const groups = data
    ? data.currencyGroups?.length
      ? data.currencyGroups
      : data.isSingleCurrency && data.currencySymbol
        ? [data]
        : []
    : []
  const percentage = (numerator: number, denominator: number) =>
    !ambiguous && denominator > 0 && numerator >= 0 && numerator <= denominator
      ? `${Number(((100 * numerator) / denominator).toFixed(1))}%`
      : '—'
  return (
    <section aria-label="Account spending" className="account-statistics">
      <header className="feature-heading">
        <div>
          <h2>What you brought home</h2>
          <p>Spending and licences from your captured Steam account pages.</p>
        </div>
        {window.winnow.exportAcquisitions && (
          <button
            onClick={() => {
              setError(null)
              setMessage('')
              void window.winnow.exportAcquisitions!()
                .then((saved) => {
                  if (saved) setMessage('Acquisitions exported.')
                })
                .catch(setError)
            }}
          >
            Export acquisitions
          </button>
        )}
      </header>
      <Notice error={stats.error || error} message={message} />
      {stats.isPending ? (
        <p role="status">Loading captured spending…</p>
      ) : data?.hasAnything ? (
        <>
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
          <CaptureCoverage value={data} />
          <p className="muted">
            These totals cover only the pages you captured. Purchases from other shops are absent. Currencies
            are never converted or added together.
          </p>
          {data.transactionsWithoutCurrency > 0 && (
            <p>
              {data.transactionsWithoutCurrency} transactions have no currency and are excluded from monetary
              totals.
            </p>
          )}
          {data.unknownAccountFactCount > 0 && (
            <p>
              {data.unknownAccountFactCount} facts have no captured account identity. Signing in does not
              assign them to an account.
            </p>
          )}
          {ambiguous ? (
            <p>
              Records with an unknown account may overlap identified captures. Monetary totals and percentages
              are unavailable; counts describe captured records.
            </p>
          ) : (
            <>
              <AccountCharts groups={groups} />
              <div className="feature-grid">
                {groups.map((group) => (
                  <SpendGroup key={group.currencySymbol} value={group} />
                ))}
              </div>
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
            <p className="muted">Licence counts describe packages, which may contain more than one game.</p>
          </section>
        </>
      ) : (
        !stats.error && (
          <Empty>
            No Steam spending has been captured. Import your saved purchase-history and licence pages in
            Settings → Platforms.
          </Empty>
        )
      )}
    </section>
  )
}

export { SteamPageImport } from './SteamAccountImport'
