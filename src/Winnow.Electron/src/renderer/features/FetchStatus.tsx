import { createContext, useContext, useEffect, useState } from 'react'
import { request } from '../api/client'
import type { Mode } from '../api/types'
import { FetchProgressObserver, FetchStatus, type FetchProgress } from './fetch-status'
import './fetch-status.css'

export const FetchStatusContext = createContext(new FetchStatus().snapshot)

export function useFetchStatusHost(enabled: boolean) {
  const [status, setStatus] = useState(() => new FetchStatus().snapshot)
  useEffect(() => {
    if (!enabled) return
    let connected = true
    const observer = new FetchProgressObserver(
      (signal) => request<FetchProgress>('progress.get', undefined, undefined, signal),
      setStatus,
    )
    const stopEvents = window.winnow.onEvent((event) => {
      if (connected && (event.kind === 'progress.changed' || event.kind === 'resync-required'))
        observer.refresh()
    })
    const stopConnection = window.winnow.onConnection((state) => {
      connected = state.connected
      if (connected) observer.refresh()
      else observer.clear()
    })
    observer.refresh()
    return () => {
      stopEvents()
      stopConnection()
      observer.dispose()
    }
  }, [enabled])
  return status
}

export function FetchCaption({ mode }: { mode: Mode }) {
  const status = useContext(FetchStatusContext)
  if (mode === 'fullscreen' || !status.active) return null
  return (
    <div
      className="fetch-status"
      role="status"
      aria-live="polite"
      aria-atomic="true"
      aria-label={status.automationName}
      data-item-status={status.automationName}
      title={status.automationName}
    >
      <span className="fetch-status-label">{status.label}</span>
      <span aria-hidden="true">·</span>
      <span className="fetch-status-count">{status.remainingText}</span>
      <span className="fetch-status-note">{status.remainingNote}</span>
    </div>
  )
}
