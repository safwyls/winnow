import { createContext, useContext, useEffect, useId } from 'react'

/** Setup navigation must wait for explicit credential saves and active account consent. */
export const SetupBusyContext = createContext<((id: string, busy: boolean) => void) | null>(null)
export const SetupErrorContext = createContext<((id: string, failed: boolean) => void) | null>(null)

export function useSetupBusy(busy: boolean) {
  const report = useContext(SetupBusyContext)
  const id = useId()
  useEffect(() => {
    report?.(id, busy)
    return () => report?.(id, false)
  }, [busy, report, id])
}

export function useSetupPreferenceError(error: unknown) {
  const report = useContext(SetupErrorContext)
  const id = useId()
  useEffect(() => {
    report?.(id, !!error)
    return () => report?.(id, false)
  }, [error, report, id])
}
