import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useId,
  useRef,
  useState,
  type ReactNode,
} from 'react'

const OperationContext = createContext<{
  busy: boolean
  report(id: string, pending: boolean): void
  attempt: { id: string } | null
  start(id: string): void
} | null>(null)

/** Both import routes and connection changes share the same Steam account state. */
export function SteamAccountOperation({ children }: { children: ReactNode }) {
  const [operations, setOperations] = useState<Set<string>>(() => new Set())
  const [attempt, setAttempt] = useState<{ id: string } | null>(null)
  const start = useCallback((id: string) => setAttempt({ id }), [])
  const report = useCallback((id: string, pending: boolean) => {
    setOperations((previous) => {
      if (previous.has(id) === pending) return previous
      const next = new Set(previous)
      if (pending) next.add(id)
      else next.delete(id)
      return next
    })
  }, [])
  return (
    <OperationContext.Provider value={{ busy: operations.size > 0, report, attempt, start }}>
      {children}
    </OperationContext.Provider>
  )
}

export function useSteamImportAttempt(clear: () => void) {
  const context = useContext(OperationContext)
  const callback = useRef(clear)
  callback.current = clear
  const id = useId()
  const attempt = context?.attempt
  useEffect(() => {
    if (attempt && attempt.id !== id) callback.current()
  }, [attempt, id])
  return () => context?.start(id)
}

export function useSteamAccountBusy(pending = false) {
  const context = useContext(OperationContext)
  const report = context?.report
  const id = useId()
  useEffect(() => {
    report?.(id, pending)
    return () => report?.(id, false)
  }, [report, id, pending])
  return pending || !!context?.busy
}
