import { useEffect, useRef, useState } from 'react'

export function useLogsFolder() {
  const [error, setError] = useState<unknown>(null)
  const alive = useRef(true)
  const generation = useRef(0)
  useEffect(() => {
    alive.current = true
    return () => {
      alive.current = false
      generation.current++
    }
  }, [])
  const open = async () => {
    const attempt = ++generation.current
    try {
      await window.winnow.openDataFolder?.('logs')
      if (alive.current && attempt === generation.current) setError(null)
    } catch (failure) {
      if (alive.current && attempt === generation.current) setError(failure)
    }
  }
  return { open, error }
}
