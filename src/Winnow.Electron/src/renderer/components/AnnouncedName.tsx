import { useEffect, useRef, useState, type ReactNode } from 'react'

/** Announce a displayed choice's changed name without announcing its initial arrival. */
export function AnnouncedName({ name, children }: { name: string; children: ReactNode }) {
  const previous = useRef(name)
  const [message, setMessage] = useState('')
  useEffect(() => {
    if (previous.current === name) return
    previous.current = name
    setMessage(name)
  }, [name])
  return (
    <>
      {children}
      <span className="sr-only" role="status" aria-live="polite" aria-atomic="true">
        {message}
      </span>
    </>
  )
}
