import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import * as Dialog from '@radix-ui/react-dialog'
import type { SteamCaptureResult } from '../../shared/bridge'
import { request } from '../api/client'
import { Notice } from './shared'
import { useSetupBusy } from './settingsState'

import { SteamImportReport, type SteamImportReportData } from './SteamImportReport'
import { useSteamAccountBusy, useSteamImportAttempt } from './SteamAccountOperation'

export function SteamCaptureReview({ capture, discard }: { capture: SteamCaptureResult; discard(): void }) {
  const [pending, setPending] = useState(false),
    [error, setError] = useState<unknown>(null)
  const [report, setReport] = useState<SteamImportReportData | null>(null)
  const client = useQueryClient()
  useSetupBusy(pending)
  const busy = useSteamAccountBusy(pending)
  async function commit() {
    if (busy || !capture.pages || report) return
    setPending(true)
    setError(null)
    try {
      setReport(await request<SteamImportReportData>('imports.steam.pages', undefined, capture.pages))
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
      <p role={capture.captureOutcome === 'failed' ? 'alert' : 'status'}>
        {capture.captureDetail || captureOutcomeMessage(capture.captureOutcome)}
      </p>
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
            <button disabled={busy} onClick={() => void commit()}>
              Import captured pages
            </button>
            <button disabled={busy} onClick={discard}>
              Discard capture
            </button>
          </div>
        </>
      )}
      {pending && <p role="status">Importing captured pages…</p>}
      {report && <SteamImportReport report={report} capture={capture} />}
      <Notice error={error} />
    </section>
  )
}

export function SteamCapture() {
  const client = useQueryClient()
  const [consent, setConsent] = useState(false),
    [pending, setPending] = useState(false)
  const [result, setResult] = useState<SteamCaptureResult | null>(null),
    [error, setError] = useState<unknown>(null)
  const [report, setReport] = useState<SteamImportReportData | null>(null)
  const alive = useRef(true)
  const active = useRef(false)
  useEffect(() => {
    alive.current = true
    return () => {
      alive.current = false
      if (active.current) void window.winnow.cancelSteamWindow?.().catch(() => {})
    }
  }, [])
  const busy = useSteamAccountBusy(pending)
  useSetupBusy(consent || pending)
  const startAttempt = useSteamImportAttempt(() => {
    setResult(null)
    setReport(null)
    setError(null)
  })
  async function capture() {
    if (!window.winnow.steamCapturePages || busy) return
    startAttempt()
    active.current = true
    setPending(true)
    setError(null)
    setResult(null)
    setReport(null)
    try {
      const captured = await window.winnow.steamCapturePages({ consentGranted: true })
      if (!alive.current) return
      setResult(captured)
      setConsent(false)
      active.current = false
      if (captured.pages) await importPages(captured)
    } catch (failure) {
      if (alive.current) setError(failure)
    } finally {
      active.current = false
      if (alive.current) setPending(false)
    }
  }
  async function importPages(captured: SteamCaptureResult) {
    const imported = await request<SteamImportReportData>('imports.steam.pages', undefined, captured.pages)
    if (alive.current) setReport(imported)
    await client.invalidateQueries({ queryKey: ['api'] })
  }
  async function retry() {
    if (busy || !result?.pages || report) return
    setPending(true)
    setError(null)
    try {
      await importPages(result)
    } catch (failure) {
      if (alive.current) setError(failure)
    } finally {
      if (alive.current) setPending(false)
    }
  }
  if (!window.winnow.steamCapturePages)
    return (
      <p>
        The Steam capture window is unavailable in this frontend. You can still import saved account pages.
      </p>
    )
  return (
    <section className="steam-account-capture">
      <h3>Read account pages in Winnow</h3>
      <p>
        Sign in to Steam in a private window to capture and import purchase history and licences. No API key
        or saved session is needed.
      </p>
      <Dialog.Root
        open={consent}
        onOpenChange={(open) => {
          if (!pending) setConsent(open)
        }}
      >
        <Dialog.Trigger asChild>
          <button disabled={busy}>Capture account pages in Winnow</button>
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
                Use Steam’s own sign-in page to capture and import your account pages.
              </Dialog.Description>
              <p>
                Winnow reads your purchase history and licence pages: what you bought, what you paid and how
                each licence arrived. It does not read your password or keep a sign-in session from this
                capture.
              </p>
              <p>
                The capture stays on this computer. Closing the private window stops the capture. Already
                captured pages will still be imported. Missing amounts remain unknown.
              </p>
              <div className="form-actions">
                <button disabled={busy} onClick={() => void capture()}>
                  Agree and import pages
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
        <section aria-label="Steam capture result" className="feature-panel">
          <h3>Account-page import</h3>
          {!result.pages && (
            <p role={result.captureOutcome === 'failed' ? 'alert' : 'status'}>
              {result.captureDetail || captureOutcomeMessage(result.captureOutcome)}
            </p>
          )}
          {pending && <p role="status">Importing captured pages…</p>}
          {report && <SteamImportReport report={report} capture={result} />}
          {!pending && !report && result.pages && error != null && (
            <button disabled={busy} onClick={() => void retry()}>
              Retry import
            </button>
          )}
        </section>
      )}
      {!consent && <Notice error={error} />}
    </section>
  )
}

function captureOutcomeMessage(outcome: SteamCaptureResult['captureOutcome']) {
  switch (outcome) {
    case 'cancelled':
      return 'The capture window closed. Nothing was imported.'
    case 'no-session':
      return 'No Steam account was signed in. You can try again.'
    case 'unavailable':
      return 'The Steam capture window is unavailable. You can still import saved pages.'
    case 'failed':
      return 'Steam account pages could not be read. Try again or import saved pages.'
    case 'partial':
      return 'Some account pages were captured. Review them before importing.'
    default:
      return 'Account pages captured. Review them before importing.'
  }
}
