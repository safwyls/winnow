import { useRef, type ButtonHTMLAttributes, type FocusEvent, type KeyboardEvent } from 'react'

/**
 * Desktop list row. Focus-driven selection reveals the selection actions above the results,
 * which moves every 44px row; a pointer press defers that until release so the click still
 * lands on the pressed row, as the desktop cover does.
 */
export function AvalonRecord({
  children,
  onFocus,
  onBlur,
  onKeyDownCapture,
  ...props
}: Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'onFocus'> & { onFocus?: () => void }) {
  const pointerFocus = useRef(false)
  return (
    <button
      {...props}
      className="avalon-record"
      onPointerDownCapture={() => {
        pointerFocus.current = true
      }}
      onKeyDownCapture={(event: KeyboardEvent<HTMLButtonElement>) => {
        pointerFocus.current = false
        onKeyDownCapture?.(event)
      }}
      onFocus={() => {
        if (!pointerFocus.current) onFocus?.()
      }}
      onBlur={(event: FocusEvent<HTMLButtonElement>) => {
        pointerFocus.current = false
        onBlur?.(event)
      }}
      onClickCapture={() => {
        if (pointerFocus.current) onFocus?.()
      }}
    >
      {children}
    </button>
  )
}
