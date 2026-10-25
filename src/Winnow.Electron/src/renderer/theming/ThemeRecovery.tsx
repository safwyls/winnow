import {
  createContext,
  useCallback,
  useContext,
  useLayoutEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react'
import { RotateCcw } from 'lucide-react'
import './theme-recovery.css'

interface Recovery {
  disabled: boolean
  restore(): void
  register(): () => void
}
const RecoveryContext = createContext<Recovery | null>(null)

export function ThemeRecoveryProvider({
  children,
  disabled,
  restore,
}: {
  children: ReactNode
  disabled: boolean
  restore(): void
}) {
  const [hosts, setHosts] = useState(0)
  const register = useCallback(() => {
    setHosts((count) => count + 1)
    return () => setHosts((count) => count - 1)
  }, [])
  const value = useMemo(() => ({ disabled, restore, register }), [disabled, restore, register])
  return (
    <RecoveryContext.Provider value={value}>
      {children}
      {hosts === 0 && <RecoveryButton recovery={value} fallback />}
    </RecoveryContext.Provider>
  )
}

function RecoveryButton({ recovery, fallback = false }: { recovery: Recovery; fallback?: boolean }) {
  return (
    <button
      className={fallback ? 'recovery-shortcut' : 'theme-recovery-button'}
      disabled={recovery.disabled}
      title="Restore default theme (Ctrl+Shift+T)"
      aria-label="Restore default theme"
      onClick={recovery.restore}
    >
      <RotateCcw aria-hidden="true" size={14} />
    </button>
  )
}

/** Built-in shells reserve space for recovery; a missing or crashed shell keeps the host fallback. */
export function ThemeRecoveryButton() {
  const recovery = useContext(RecoveryContext)
  const register = recovery?.register
  useLayoutEffect(() => register?.(), [register])
  return recovery && <RecoveryButton recovery={recovery} />
}
