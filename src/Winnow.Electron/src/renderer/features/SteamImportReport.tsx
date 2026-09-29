import type { SteamCaptureResult } from '../../shared/bridge'
import './steam-import.css'

export interface SteamImportReportData {
  licensesOutcome: string
  historyOutcome: string
  licensesFailureReason?: string | null
  historyFailureReason?: string | null
  licensesReportedTotal?: number | null
  licenseRowsParsed: number
  licenseRowsSkippedByParser: number
  historyRowsParsed: number
  acquisitionsMatched: number
  pricesMatched: number
  ownershipsFilled: number
  ownershipsAlreadyComplete: number
  licensesTruncated: boolean
  historyTruncated: boolean
  skippedBundleRows: number
  skippedRefundedRows: number
  skippedNonPurchaseRows: number
  skippedNonProductRows: number
  skippedAmbiguousTitle: number
  skippedNoOwnershipMatch: number
  skippedConflictingRows: number
  transactionFactsRecorded: number
  licenseFactsRecorded: number
  transactionFactsAlreadyRecorded: number
  licenseFactsAlreadyRecorded: number
}

export function steamImportNotices(report: SteamImportReportData, capture?: SteamCaptureResult) {
  const notices: string[] = []
  for (const kind of ['licenses', 'history'] as const) {
    const name = kind === 'licenses' ? 'licences' : 'purchase history'
    if (capture) {
      const stop = kind === 'licenses' ? capture.licensesStoppedBecause : capture.historyStoppedBecause
      if (stop === 'cap')
        notices.push(
          `Winnow stopped reading ${name} at its safety limit. This capture is incomplete. You can run it again; existing rows stay unchanged.`,
        )
      else if (
        stop !== 'exhausted' &&
        (stop === 'stalled' ||
          stop === 'interrupted' ||
          stop === 'failed' ||
          report[`${kind}Truncated`] ||
          capture[`${kind}Truncated`])
      )
        notices.push(
          `This run did not get all your ${name}. Try capturing again; existing rows stay unchanged.`,
        )
    } else if (kind === 'history' && report.historyTruncated) {
      notices.push(
        'This capture is incomplete. Steam only saves the purchase history currently shown. Click “Load more transactions” until it disappears, then save the page again.',
      )
    } else if (
      kind === 'licenses' &&
      (report.licensesTruncated ||
        (report.licensesOutcome === 'Parsed' &&
          report.licensesReportedTotal == null &&
          (report.licenseRowsParsed ?? 0) + (report.licenseRowsSkippedByParser ?? 0) >= 90))
    ) {
      notices.push(
        'This capture is incomplete. Steam paginates licences; a saved file may hold only one page. Save and import the remaining licence pages, or capture them inside Winnow.',
      )
    }
  }
  return notices
}

export function SteamImportReport({
  report,
  capture,
}: {
  report: SteamImportReportData
  capture?: SteamCaptureResult
}) {
  const count = (value: number | undefined) => (value ?? 0).toLocaleString()
  const mismatch =
    report.licensesReportedTotal != null &&
    report.licensesReportedTotal !==
      (report.licenseRowsParsed ?? 0) + (report.licenseRowsSkippedByParser ?? 0)
  const rows: [string, number][] = [
    ['Licences found', report.licenseRowsParsed],
    ...(mismatch ? [['Licences reported', report.licensesReportedTotal!] as [string, number]] : []),
    ['Purchases found', report.historyRowsParsed],
    ['Licences matched', report.acquisitionsMatched],
    ['Prices matched', report.pricesMatched],
    ['Games updated', report.ownershipsFilled],
    ['Already complete', report.ownershipsAlreadyComplete],
  ]
  const skipped: [string, number][] = [
    ['Bundle purchases', report.skippedBundleRows],
    ['Refunded purchases', report.skippedRefundedRows],
    ['Gifts and in-game purchases', report.skippedNonPurchaseRows],
    ['Wallet and gift cards', report.skippedNonProductRows],
    ['Ambiguous titles', report.skippedAmbiguousTitle],
    ['No match in library', report.skippedNoOwnershipMatch],
    ['Disagreeing rows', report.skippedConflictingRows],
  ].filter(([, value]) => Number(value) > 0) as [string, number][]
  return (
    <section className="steam-import-report" aria-label="Steam import results" role="status">
      <h3>Import results</h3>
      <p>
        Licences: {report.licensesOutcome}. Purchase history: {report.historyOutcome}.
      </p>
      <dl className="steam-import-counts">
        {rows.map(([label, value]) => (
          <div key={label}>
            <dt>{label}</dt>
            <dd>{count(value)}</dd>
          </div>
        ))}
      </dl>
      {mismatch && (
        <p>
          Steam reports a different licence total from the rows it renders. The difference alone does not mean
          licences were missed.
        </p>
      )}
      {report.ownershipsFilled === 0 && <p>Every row was read but nothing new was filled in.</p>}
      <p>
        {count(report.licenseFactsRecorded)} licence facts and {count(report.transactionFactsRecorded)}{' '}
        transaction facts recorded; {count(report.ownershipsFilled)} library entries filled.
      </p>
      <p>
        {count((report.licenseFactsAlreadyRecorded ?? 0) + (report.transactionFactsAlreadyRecorded ?? 0))}{' '}
        facts were already recorded.
      </p>
      {skipped.length > 0 && (
        <section aria-label="Rows not applied">
          <h4>Rows not applied</h4>
          <dl className="steam-import-counts">
            {skipped.map(([label, value]) => (
              <div key={label}>
                <dt>{label}</dt>
                <dd>{count(value)}</dd>
              </div>
            ))}
          </dl>
        </section>
      )}
      {steamImportNotices(report, capture).map((notice) => (
        <p key={notice}>{notice}</p>
      ))}
      {(['licenses', 'history'] as const).map(
        (kind) =>
          (['NotRecognized', 'Unrecognized'].includes(report[`${kind}Outcome`]) ||
            report[`${kind}FailureReason`]) && (
            <p role="alert" key={kind}>
              {kind === 'licenses' ? 'The licences page' : 'The purchase-history page'} was not recognized.{' '}
              {report[`${kind}FailureReason`]}
            </p>
          ),
      )}
    </section>
  )
}
