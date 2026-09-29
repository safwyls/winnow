import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import * as Dialog from '@radix-ui/react-dialog'
import type { SteamCaptureResult } from '../../shared/bridge'
import { request } from '../api/client'
import { Notice } from './shared'
import { useSetupBusy } from './settingsState'

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
export function SteamCaptureReview({ capture, discard }: { capture: SteamCaptureResult; discard(): void }) {
  const [pending, setPending] = useState(false),
    [error, setError] = useState<unknown>(null)
  const [report, setReport] = useState<ImportReport | null>(null)
  const client = useQueryClient()
  useSetupBusy(pending)
  async function commit() {
    if (pending || !capture.pages || report) return
    setPending(true)
    setError(null)
    try {
      setReport(await request<ImportReport>('imports.steam.pages', undefined, capture.pages))
      await client.invalidateQueries({ queryKey: ['api'] })
    } catch (failure) {
      setError(failure)
    } finally {
      setPending(false)
    }
  }
  return (
    <section aria-label="Review Steam capture" className="feature-panel">
      <h3>Review account-page capture</h3>
      <p>{capture.captureDetail}</p>
      {capture.pages && !report && (
        <>
          <p>
            {capture.pages.licensesHtml
              ? `${1 + capture.pages.additionalLicensesHtml.length} licence pages ready.`
              : 'No licence page captured.'}{' '}
            {capture.pages.historyHtml ? 'Purchase history ready.' : 'No purchase history captured.'}
          </p>
          <p>
            {capture.pages.steamId
              ? 'Both page types are checked against the observed Steam account.'
              : 'The captured pages did not identify an account. These facts will remain under an unknown account.'}
          </p>
          <p>
            Import records what you bought, what you paid and how licences arrived. It does not fill in
            missing amounts or replace facts from other accounts.
          </p>
          {(capture.licensesTruncated || capture.historyTruncated) && (
            <p>This capture is incomplete. You can import these pages and capture the remainder later.</p>
          )}
          <div className="form-actions">
            <button disabled={pending} onClick={() => void commit()}>
              Import captured pages
            </button>
            <button disabled={pending} onClick={discard}>
              Discard capture
            </button>
          </div>
        </>
      )}
      {pending && <p role="status">Importing captured pages…</p>}
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
            <p>The imported pages are incomplete; remaining history can be imported later.</p>
          )}
          {report.licensesFailureReason && <p>{report.licensesFailureReason}</p>}
          {report.historyFailureReason && <p>{report.historyFailureReason}</p>}
        </div>
      )}
      <Notice error={error} />
    </section>
  )
}

export function SteamCapture() {
  const [consent, setConsent] = useState(false),
    [pending, setPending] = useState(false)
  const [result, setResult] = useState<SteamCaptureResult | null>(null),
    [error, setError] = useState<unknown>(null)
  useSetupBusy(consent || pending)
  async function capture() {
    if (!window.winnow.steamCapturePages || pending) return
    setPending(true)
    setError(null)
    setResult(null)
    try {
      setResult(await window.winnow.steamCapturePages({ consentGranted: true }))
      setConsent(false)
    } catch (failure) {
      setError(failure)
    } finally {
      setPending(false)
    }
  }
  if (!window.winnow.steamCapturePages) return null
  return (
    <section className="steam-account-capture">
      <Dialog.Root
        open={consent}
        onOpenChange={(open) => {
          if (!pending) setConsent(open)
        }}
      >
        <Dialog.Trigger asChild>
          <button>Capture account pages in Winnow</button>
        </Dialog.Trigger>
        <Dialog.Portal>
          <Dialog.Overlay className="setup-overlay consent-overlay" />
          <Dialog.Content
            className="setup-dialog consent-dialog"
            onEscapeKeyDown={(event) => {
              event.stopPropagation()
              if (pending) event.preventDefault()
            }}
          >
            <div className="setup-body">
              <Dialog.Title>Read your Steam account pages?</Dialog.Title>
              <Dialog.Description>
                Use Steam’s own sign-in page, then review the capture before importing.
              </Dialog.Description>
              <p>
                Winnow reads your purchase history and licence pages: what you bought, what you paid and how
                each licence arrived. It does not read your password or keep a sign-in session from this
                capture.
              </p>
              <p>
                The capture stays on this computer. Closing the private window stops the capture. Already
                captured pages can still be reviewed or discarded.
              </p>
              <div className="form-actions">
                <button disabled={pending} onClick={() => void capture()}>
                  Agree and capture pages
                </button>
                <button
                  disabled={pending && !window.winnow.cancelSteamWindow}
                  onClick={() => {
                    if (pending) void window.winnow.cancelSteamWindow?.().catch(setError)
                    else setConsent(false)
                  }}
                >
                  Cancel capture
                </button>
              </div>
              {pending && (
                <p role="status">
                  Sign in in the window that opened. Its title reports progress; close that window to stop.
                </p>
              )}
              <Notice error={error} />
            </div>
          </Dialog.Content>
        </Dialog.Portal>
      </Dialog.Root>
      {result && (
        <SteamCaptureReview
          key={result.pages?.capturedAt ?? result.captureDetail}
          capture={result}
          discard={() => setResult(null)}
        />
      )}
      {!consent && <Notice error={error} />}
    </section>
  )
}
