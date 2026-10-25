import { useLayoutEffect, useRef, useState } from 'react'
import { useIsMutating, useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { FullscreenSettingsAction } from './FullscreenSettingRows'

const exporting = new WeakSet<QueryClient>()
const mutationKey = ['acquisition-export']
export const acquisitionExportDescription =
  "Save a CSV with each copy's title, store, acquisition date, licence and price paid. Missing facts stay blank. Prices use the stored cents; currency is not recorded."

export function AcquisitionExport({
  label = 'Export acquisition CSV',
  row = false,
}: {
  label?: string
  row?: boolean
}) {
  const client = useQueryClient()
  const pending = useIsMutating({ mutationKey }) > 0
  const root = useRef<HTMLDivElement>(null)
  const restore = useRef(false)
  useLayoutEffect(() => {
    if (pending || !restore.current) return
    restore.current = false
    if (document.activeElement === document.body)
      root.current?.querySelector('button')?.focus({ preventScroll: true })
  }, [pending])
  const [status, setStatus] = useState('')
  const [failed, setFailed] = useState(false)
  const mutation = useMutation({
    mutationKey,
    retry: false,
    mutationFn: () => window.winnow.exportAcquisitions!(),
    onSuccess: ({ saved, ownershipCount }) => {
      setStatus(
        saved
          ? `Exported ${ownershipCount.toLocaleString()} ownership ${ownershipCount === 1 ? 'record' : 'records'}.`
          : 'Export cancelled.',
      )
    },
    onError: () => {
      setFailed(true)
      setStatus('Could not save the export. Try another location.')
    },
    onSettled: () => {
      exporting.delete(client)
    },
  })
  const start = () => {
    if (!window.winnow.exportAcquisitions || exporting.has(client)) return
    exporting.add(client)
    restore.current = root.current?.contains(document.activeElement) ?? false
    setFailed(false)
    setStatus('Preparing acquisition CSV…')
    mutation.mutate()
  }
  const disabled = pending || !window.winnow.exportAcquisitions
  return (
    <div ref={root} className="acquisition-export">
      {row ? (
        <FullscreenSettingsAction label={label} kind="Run" disabled={disabled} onClick={start}>
          <span className="fullscreen-setting-current">{acquisitionExportDescription}</span>
        </FullscreenSettingsAction>
      ) : (
        <button type="button" disabled={disabled} onClick={start}>
          {label}
        </button>
      )}
      {status && (
        <p
          role="status"
          aria-live="polite"
          aria-atomic="true"
          className={failed ? 'error-message' : undefined}
        >
          {status}
        </p>
      )}
    </div>
  )
}
