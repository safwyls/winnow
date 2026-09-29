import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { request, openExternal } from '../api/client'
import { Notice } from './shared'
import { SteamImportReport, type SteamImportReportData } from './SteamImportReport'
import { useSteamAccountBusy, useSteamImportAttempt } from './SteamAccountOperation'
import { useSetupBusy } from './settingsState'

interface LoadedPages {
  pages: Record<string, unknown>
  anythingLoaded: boolean
  files: { path: string; outcome: number; kind: number | null; detail: string | null }[]
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
  const [report, setReport] = useState<SteamImportReportData | null>(null)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const [pickerNotice, setPickerNotice] = useState('')
  const picker = useRef<HTMLInputElement>(null)
  useEffect(() => {
    const input = picker.current
    const cancelled = () => setPickerNotice('No pages were selected. Nothing was imported.')
    input?.addEventListener('cancel', cancelled)
    return () => input?.removeEventListener('cancel', cancelled)
  }, [])
  const busy = useSteamAccountBusy(pending)
  const startAttempt = useSteamImportAttempt(() => {
    setLoaded(null)
    setReport(null)
    setError(null)
    setPickerNotice('')
  })
  useSetupBusy(pending)
  async function load(files: File[]) {
    if (busy) return
    startAttempt()
    setPending(true)
    setError(null)
    setPickerNotice('')
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
      const pages = await request<LoadedPages>('imports.steam.load', undefined, { files: uploads })
      setLoaded(pages)
      if (pages.anythingLoaded) await importPages(pages)
    } catch (failure) {
      setError(failure)
    } finally {
      setPending(false)
    }
  }
  async function importPages(pages: LoadedPages) {
    setReport(await request<SteamImportReportData>('imports.steam.pages', undefined, pages.pages))
    await client.invalidateQueries({ queryKey: ['api'] })
  }
  async function commit() {
    if (busy || report || !loaded?.anythingLoaded) return
    setPending(true)
    setError(null)
    try {
      await importPages(loaded)
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
        Save your Steam account pages as HTML in your browser, then select the saved files to import them.
        You can import several licence pages together. Missing amounts remain unknown.
      </p>
      <details>
        <summary>Before you save the pages</summary>
        <p>Steam paginates licences. Save every licence page you want to import.</p>
        <p>For purchase history, click “Load more transactions” until it disappears before saving.</p>
      </details>
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
          ref={picker}
          type="file"
          accept=".html,.htm"
          multiple
          disabled={busy}
          onClick={() => {
            startAttempt()
            setLoaded(null)
            setReport(null)
            setError(null)
            setPickerNotice('')
          }}
          onChange={(event) => {
            const files = Array.from(event.currentTarget.files ?? [])
            event.currentTarget.value = ''
            if (files.length) void load(files)
            else setPickerNotice('No pages were selected. Nothing was imported.')
          }}
        />
      </label>
      <Notice error={error} />
      {pickerNotice && <p role="status">{pickerNotice}</p>}
      {pending && <p role="status">Reading and importing your capture…</p>}
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
                  : {
                      1: 'Not found',
                      2: 'Unreadable',
                      3: 'Not recognized',
                      4: 'Already read',
                      5: 'Different account',
                    }[file.outcome] || 'Not recognized'}
                {file.outcome !== 0 && file.detail && <span> — {file.detail}</span>}
              </li>
            ))}
          </ul>
          {loaded.files.some((file) => file.outcome === 4) && (
            <p>Duplicate licence pages were skipped. Purchase history uses the first file selected.</p>
          )}
          {!loaded.anythingLoaded && (
            <p>None of the selected files were recognized as Steam account pages. Nothing was imported.</p>
          )}
          {!report && !pending && loaded.anythingLoaded && error != null && (
            <div className="form-actions">
              <button disabled={busy || !loaded.anythingLoaded} onClick={() => void commit()}>
                Retry import
              </button>
              <button disabled={busy} onClick={() => setLoaded(null)}>
                Discard capture
              </button>
            </div>
          )}
        </>
      )}
      {report && <SteamImportReport report={report} />}
    </section>
  )
}
