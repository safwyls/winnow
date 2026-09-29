import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { dateLabel, openExternal, request } from '../api/client'
import { useApiQuery } from '../api/hooks'
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
  firstTransactionAt?: string
  lastTransactionAt?: string
  spendByYear: { year: number; transactionCount: number; cents: number }[]
  undatedNetSpendCents: number
  undatedNetTransactionCount: number
  licenseAcquisitions: { kind: string | null; count: number }[]
  biggestPurchase?: {
    cents: number
    itemNames: string[]
    itemCount: number
    occurredAt?: string
    isBundle: boolean
  } | null
}
export const capturedMoney = (cents: number, symbol: string | null) =>
  `${symbol ?? ''}${(cents / 100).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`

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
        </div>
        <div>
          <dt>Before refunds</dt>
          <dd>{productMoney(value.grossProductSpendCents)}</dd>
        </div>
        <div>
          <dt>Refunded purchases</dt>
          <dd>{productMoney(value.refundedProductSpendCents)}</dd>
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
      {(value.spendByYear?.length ?? 0) > 0 && (
        <>
          <h4>By year</h4>
          <table>
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
            </tbody>
          </table>
        </>
      )}
      {value.undatedNetTransactionCount > 0 && (
        <p>
          {value.undatedNetTransactionCount} undated transactions: {money(value.undatedNetSpendCents)}.
        </p>
      )}
      {value.biggestPurchase && (
        <p>
          Largest transaction: {money(value.biggestPurchase.cents)} ·{' '}
          {value.biggestPurchase.itemNames.join(', ')}
          {value.biggestPurchase.isBundle ? ' (bundle)' : ''}.
        </p>
      )}
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
  const stats = useApiQuery<AccountStats>('statistics.account', { source: 'steam' })
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
            {data.knownAccountCount > 1 ? ` · ${data.knownAccountCount} captured accounts` : ''}.
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
          {data.firstTransactionAt && (
            <p>
              {dateLabel(data.firstTransactionAt)} –{' '}
              {data.lastTransactionAt ? dateLabel(data.lastTransactionAt) : 'unknown'}
            </p>
          )}
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
          {!!data.licenseAcquisitions?.length && (
            <AccountChart
              title="Licence acquisition methods"
              rows={data.licenseAcquisitions.map((row) => ({
                label: row.kind || 'Unrecognized method',
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
                  {item.kind || 'Unrecognized method'}: {item.count}
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
            Connections.
          </Empty>
        )
      )}
    </section>
  )
}

interface LoadedPages {
  pages: Record<string, unknown>
  anythingLoaded: boolean
  files: { path: string; outcome: number; kind: number | null; detail: string | null }[]
}
interface ImportReport {
  licensesOutcome: string
  historyOutcome: string
  licensesFailureReason?: string
  historyFailureReason?: string
  licenseRowsParsed: number
  historyRowsParsed: number
  licensesTruncated: boolean
  historyTruncated: boolean
  transactionFactsRecorded: number
  licenseFactsRecorded: number
  ownershipsFilled: number
  transactionFactsAlreadyRecorded: number
  licenseFactsAlreadyRecorded: number
}
async function encodeFile(file: File): Promise<string> {
  const bytes = new Uint8Array(await file.arrayBuffer())
  let binary = ''
  for (let offset = 0; offset < bytes.length; offset += 16384)
    binary += String.fromCharCode(...bytes.subarray(offset, offset + 16384))
  return btoa(binary)
}
export function SteamPageImport() {
  const client = useQueryClient()
  const [loaded, setLoaded] = useState<LoadedPages | null>(null)
  const [report, setReport] = useState<ImportReport | null>(null)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<unknown>(null)
  async function load(files: File[]) {
    setPending(true)
    setError(null)
    setLoaded(null)
    setReport(null)
    try {
      if (
        files.some((file) => file.size > 64 * 1024 * 1024) ||
        files.reduce((n, f) => n + f.size, 0) > 128 * 1024 * 1024
      )
        throw new Error(
          'Choose pages up to 64 MiB each and 128 MiB in total. Import larger captures in smaller groups.',
        )
      const uploads = await Promise.all(
        files.map(async (file) => ({ name: file.name, content: await encodeFile(file) })),
      )
      setLoaded(await request<LoadedPages>('imports.steam.load', undefined, { files: uploads }))
    } catch (failure) {
      setError(failure)
    } finally {
      setPending(false)
    }
  }
  async function commit() {
    if (!loaded?.anythingLoaded) return
    setPending(true)
    setError(null)
    try {
      setReport(await request<ImportReport>('imports.steam.pages', undefined, loaded.pages))
      setLoaded(null)
      await client.invalidateQueries({ queryKey: ['api'] })
    } catch (failure) {
      setError(failure)
    } finally {
      setPending(false)
    }
  }
  return (
    <section className="steam-page-import" aria-label="Steam purchase and licence import">
      <h3>Purchase and licence history</h3>
      <p>
        Save your Steam account pages as HTML in your browser, then select the saved files. You can import
        several licence pages together.
      </p>
      <div className="form-actions">
        <button
          onClick={() => {
            setError(null)
            void openExternal('https://store.steampowered.com/account/licenses/', {
              failure: 'inline',
            }).catch(setError)
          }}
        >
          Open licences
        </button>
        <button
          onClick={() => {
            setError(null)
            void openExternal('https://store.steampowered.com/account/history/', { failure: 'inline' }).catch(
              setError,
            )
          }}
        >
          Open purchase history
        </button>
      </div>
      <label className="field">
        Saved Steam pages
        <input
          type="file"
          accept=".html,.htm"
          multiple
          disabled={pending}
          onChange={(event) => {
            const files = Array.from(event.currentTarget.files ?? [])
            event.currentTarget.value = ''
            if (files.length) void load(files)
          }}
        />
      </label>
      <Notice error={error} />
      {pending && <p role="status">Reading your capture…</p>}
      {loaded && (
        <>
          <ul>
            {loaded.files.map((file, index) => (
              <li key={index}>
                {file.path}:{' '}
                {file.outcome === 0
                  ? file.kind === 0
                    ? 'Licence page ready'
                    : 'Purchase history ready'
                  : file.detail || 'This page could not be used'}
              </li>
            ))}
          </ul>
          <p>Importing records only what these pages contain. Missing amounts remain unknown.</p>
          <div className="form-actions">
            <button disabled={pending || !loaded.anythingLoaded} onClick={() => void commit()}>
              Import captured pages
            </button>
            <button disabled={pending} onClick={() => setLoaded(null)}>
              Discard capture
            </button>
          </div>
        </>
      )}
      {report && (
        <div role="status">
          <p>
            Licences: {report.licensesOutcome}. Purchase history: {report.historyOutcome}.
          </p>
          <p>
            {report.licenseFactsRecorded} licence facts and {report.transactionFactsRecorded} transaction
            facts recorded; {report.ownershipsFilled} library entries filled.
          </p>
          <p>
            {report.licenseFactsAlreadyRecorded + report.transactionFactsAlreadyRecorded} facts were already
            recorded.
          </p>
          {(report.licensesTruncated || report.historyTruncated) && (
            <p>This capture is incomplete. Import the remaining pages to include more history.</p>
          )}
          {report.licensesFailureReason && <p>{report.licensesFailureReason}</p>}
          {report.historyFailureReason && <p>{report.historyFailureReason}</p>}
        </div>
      )}
    </section>
  )
}
