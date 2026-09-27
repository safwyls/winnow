import { useCallback, useRef, useSyncExternalStore, type Dispatch, type SetStateAction } from 'react'

// Navigation state lasts for this frontend session. It never changes backend preferences.
const positions = new Map<string, unknown>()
const subscribers = new Map<string, Set<() => void>>()
function notify(key: string): void {
  subscribers.get(key)?.forEach((listener) => listener())
}
export function clearViewState(key: string): void {
  if (positions.delete(key)) notify(key)
}
export function useViewState<T>(key: string, initial: T): [T, Dispatch<SetStateAction<T>>] {
  const fallback = useRef({ key, value: initial })
  if (fallback.current.key !== key) fallback.current = { key, value: initial }
  const initialForKey = fallback.current.value
  const snapshot = useCallback(
    () => (positions.has(key) ? (positions.get(key) as T) : initialForKey),
    [key, initialForKey],
  )
  const subscribe = useCallback(
    (listener: () => void) => {
      let listeners = subscribers.get(key)
      if (!listeners) {
        listeners = new Set()
        subscribers.set(key, listeners)
      }
      listeners.add(listener)
      return () => {
        listeners.delete(listener)
        if (listeners.size === 0) subscribers.delete(key)
      }
    },
    [key],
  )
  const state = useSyncExternalStore(subscribe, snapshot, snapshot)
  // Writes from a former screen's pending request still reach the current screen.
  const update: Dispatch<SetStateAction<T>> = useCallback(
    (value) => {
      const existed = positions.has(key)
      const previous = existed ? (positions.get(key) as T) : initialForKey
      const next = typeof value === 'function' ? (value as (previous: T) => T)(previous) : value
      positions.set(key, next)
      if (!existed || !Object.is(previous, next)) notify(key)
    },
    [key, initialForKey],
  )
  return [state, update]
}
export const libraryScroll = new Map<string, number>()
