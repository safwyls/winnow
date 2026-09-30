import { useEffect, useId, useLayoutEffect, useRef, useState, type CSSProperties } from 'react'
import { createPortal } from 'react-dom'
import { ChevronDown } from 'lucide-react'
import './SortMenu.css'

export interface SortOption {
  value: string
  label: string
}

/** Library and Merges share the same compact desktop sort control. */
export function SortMenu({
  value,
  options,
  onChange,
}: {
  value: string
  options: readonly SortOption[]
  onChange(value: string): void
}) {
  const [open, setOpen] = useState(false)
  const trigger = useRef<HTMLButtonElement>(null),
    menu = useRef<HTMLDivElement>(null)
  const anchor = useRef<DOMRect | null>(null)
  const id = useId(),
    initialFocus = useRef<'selected' | 'first' | 'last'>('selected')
  const selected = options.find((option) => option.value === value)
  const [style, setStyle] = useState<CSSProperties>({})
  const [portal, setPortal] = useState<HTMLElement>(document.body)
  function close(restore = false) {
    setOpen(false)
    if (restore) trigger.current?.focus({ preventScroll: true })
  }
  function show(focus: 'selected' | 'first' | 'last' = 'selected') {
    initialFocus.current = focus
    // Stay inside an owning dialog's focus boundary; the top layer avoids its clipping and transforms.
    setPortal(trigger.current!.closest<HTMLElement>('[role="dialog"]') ?? document.body)
    const inherited = getComputedStyle(trigger.current!)
    const values: Record<string, string> = {}
    for (const token of [
      'surface',
      'raised',
      'text',
      'muted',
      'line',
      'accent',
      'accent-foreground',
      'font-body',
      'theme-text-scale',
    ]) {
      const content = inherited.getPropertyValue(`--${token}`)
      if (content.trim()) values[`--${token}`] = content
    }
    setStyle({ ...values, fontFamily: inherited.fontFamily })
    setOpen(true)
  }
  useLayoutEffect(() => {
    if (!open || !menu.current || !trigger.current) return
    const element = menu.current,
      bounds = trigger.current.getBoundingClientRect()
    anchor.current = bounds
    if (element.showPopover) element.showPopover()
    else element.style.display = 'block'
    const zoom = Number.parseFloat(getComputedStyle(document.body).zoom) || 1
    const width = innerWidth / zoom,
      height = innerHeight / zoom
    element.style.maxWidth = `${Math.max(0, Math.min(320, width - 16))}px`
    element.style.maxHeight = `${Math.max(0, height - 16)}px`
    const measured = element.getBoundingClientRect()
    element.style.left = `${Math.max(8, Math.min(bounds.right / zoom - measured.width / zoom, width - measured.width / zoom - 8))}px`
    element.style.top = `${Math.max(8, bounds.bottom / zoom + measured.height / zoom + 6 <= height - 8 ? bounds.bottom / zoom + 6 : bounds.top / zoom - measured.height / zoom - 6)}px`
    const items = [...element.querySelectorAll<HTMLButtonElement>('[role="menuitemradio"]')]
    const chosen =
      initialFocus.current === 'first'
        ? items[0]
        : initialFocus.current === 'last'
          ? items.at(-1)
          : (items.find((item) => item.value === value) ?? items[0])
    chosen?.focus({ preventScroll: true })
    chosen?.scrollIntoView?.({ block: 'nearest' })
  }, [open])
  useEffect(() => {
    if (!open) return
    const outside = (event: Event) => {
      if (!menu.current?.contains(event.target as Node) && !trigger.current?.contains(event.target as Node))
        close()
    }
    const hide = () => close()
    const scroll = (event: Event) => {
      if (menu.current?.contains(event.target as Node)) return
      const bounds = trigger.current?.getBoundingClientRect(), previous = anchor.current
      // A scroll queued before the opening click must not dismiss the newly placed menu.
      if (bounds && previous && (Math.abs(bounds.top - previous.top) > 0.5 || Math.abs(bounds.left - previous.left) > 0.5)) close(true)
    }
    document.addEventListener('pointerdown', outside)
    document.addEventListener('focusin', outside)
    document.addEventListener('scroll', scroll, true)
    window.addEventListener('resize', hide)
    window.addEventListener('blur', hide)
    return () => {
      document.removeEventListener('pointerdown', outside)
      document.removeEventListener('focusin', outside)
      document.removeEventListener('scroll', scroll, true)
      window.removeEventListener('resize', hide)
      window.removeEventListener('blur', hide)
    }
  }, [open])
  return (
    <>
      <button
        ref={trigger}
        className="shared-sort-trigger"
        type="button"
        value={value}
        title="Sort order"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? id : undefined}
        onClick={() => (open ? close(true) : show())}
        onKeyDown={(event) => {
          if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault()
            event.stopPropagation()
            show(event.key === 'ArrowDown' ? 'first' : 'last')
          }
        }}
      >
        Sort · {selected?.label ?? value}
        <ChevronDown size={12} aria-hidden />
      </button>
      {open &&
        createPortal(
          <div
            id={id}
            ref={menu}
            role="menu"
            popover="manual"
            aria-label="Sort order"
            className="shared-sort-menu"
            style={style}
            onKeyDown={(event) => {
              const items = [...menu.current!.querySelectorAll<HTMLButtonElement>('[role="menuitemradio"]')]
              const index = items.indexOf(document.activeElement as HTMLButtonElement)
              if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
                event.preventDefault()
                event.stopPropagation()
                const next =
                  event.key === 'Home'
                    ? 0
                    : event.key === 'End'
                      ? items.length - 1
                      : (index + (event.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length
                items[next]?.focus({ preventScroll: true })
                items[next]?.scrollIntoView?.({ block: 'nearest' })
              } else if (event.key === 'Escape' || event.key === 'Tab') {
                if (event.key === 'Escape') {
                  event.preventDefault()
                  event.stopPropagation()
                }
                close(true)
              } else if (
                event.key.length === 1 &&
                !event.ctrlKey &&
                !event.metaKey &&
                !event.altKey &&
                event.key !== ' '
              ) {
                const match = [...items.slice(index + 1), ...items.slice(0, index + 1)].find((item) =>
                  item.textContent?.trim().toLocaleLowerCase().startsWith(event.key.toLocaleLowerCase()),
                )
                if (match) {
                  event.preventDefault()
                  match.focus()
                  match.scrollIntoView?.({ block: 'nearest' })
                }
              }
            }}
          >
            {options.map((option) => (
              <button
                key={option.value}
                type="button"
                value={option.value}
                role="menuitemradio"
                aria-checked={value === option.value}
                tabIndex={-1}
                onClick={() => {
                  close(true)
                  onChange(option.value)
                }}
              >
                <span className="shared-sort-dot" aria-hidden />
                {option.label}
              </button>
            ))}
          </div>,
          portal,
        )}
    </>
  )
}
