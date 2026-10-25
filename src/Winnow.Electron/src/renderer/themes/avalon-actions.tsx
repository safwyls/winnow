import {
  createContext,
  useContext,
  useLayoutEffect,
  useRef,
  useState,
  type KeyboardEvent,
  type ReactNode,
} from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { ChevronRight, type LucideIcon } from 'lucide-react'
import './avalon-actions.css'

const inertOwners = new WeakMap<HTMLElement, { count: number; original: boolean }>()
const CloseActionsContext = createContext<() => void>(() => {})
function disableOrigin(element: HTMLElement) {
  const lease = inertOwners.get(element) ?? { count: 0, original: element.hasAttribute('inert') }
  lease.count++
  inertOwners.set(element, lease)
  element.setAttribute('inert', '')
  return () => {
    if (--lease.count) return
    if (!lease.original) element.removeAttribute('inert')
    inertOwners.delete(element)
  }
}

function actionsWithin(element: HTMLElement) {
  return [
    ...element.querySelectorAll<HTMLElement>('button:not(:disabled), a[href], summary, [tabindex="0"]'),
  ].filter((node) => !node.closest('[hidden], [inert]') && getComputedStyle(node).display !== 'none')
}

function move(event: KeyboardEvent<HTMLDivElement>) {
  if (event.defaultPrevented || !['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return
  if ((event.target as HTMLElement).matches('input, textarea, select, [contenteditable="true"]')) return
  const actions = actionsWithin(event.currentTarget)
  const index = actions.indexOf(document.activeElement as HTMLElement)
  if (index < 0) return
  event.preventDefault()
  event.stopPropagation()
  const next =
    event.key === 'Home'
      ? 0
      : event.key === 'End'
        ? actions.length - 1
        : Math.max(0, Math.min(actions.length - 1, index + (event.key === 'ArrowDown' ? 1 : -1)))
  const target = actions[next]
  target?.focus({ preventScroll: true })
  const body = target?.closest<HTMLElement>('.avalon-actions-body')
  if (target && body) {
    const row = target.getBoundingClientRect(),
      viewport = body.getBoundingClientRect()
    const distance =
      row.top < viewport.top
        ? row.top - viewport.top
        : row.bottom > viewport.bottom
          ? row.bottom - viewport.bottom
          : 0
    // Scrolling only the action list preserves the retained page during the edge animation.
    if (distance)
      body.scrollBy({
        top: distance / (body.getBoundingClientRect().height / body.offsetHeight),
        behavior: 'instant',
      })
  }
}

/** The panel lives beside the retained page, so its backdrop cannot clip or resize that page. */
export function AvalonActions({
  open,
  title,
  close,
  restoreFocus,
  children,
  footer,
}: {
  open: boolean
  title: string
  close(): void
  restoreFocus?(): void
  children: ReactNode
  footer?: ReactNode
}) {
  const panel = useRef<HTMLDivElement>(null)
  const shade = useRef<HTMLDivElement>(null)
  const origin = useRef<HTMLElement | null>(null)
  const previousTitle = useRef(title)
  const [host, setHost] = useState<HTMLElement | null>(null)
  useLayoutEffect(() => {
    if (!open) return
    setHost(document.querySelector<HTMLElement>('.avalon-shell.fullscreen') ?? document.body)
  }, [open])
  useLayoutEffect(() => {
    if (!open || !host || !panel.current) return
    const release = [...host.children]
      .filter(
        (element): element is HTMLElement =>
          element instanceof HTMLElement &&
          element !== panel.current &&
          element !== shade.current &&
          !element.hasAttribute('data-radix-focus-guard'),
      )
      .map(disableOrigin)
    return () => release.forEach((dispose) => dispose())
  }, [open, host])
  useLayoutEffect(() => {
    const changed = previousTitle.current !== title
    previousTitle.current = title
    if (changed && open && panel.current) actionsWithin(panel.current)[0]?.focus({ preventScroll: true })
  }, [open, host, title])
  return (
    <Dialog.Root
      open={open}
      onOpenChange={(value) => {
        if (!value) close()
      }}
    >
      {open && host && (
        <Dialog.Portal container={host}>
          <Dialog.Overlay
            ref={shade}
            className="avalon-actions-shade"
            onContextMenu={(event) => {
              event.preventDefault()
              event.stopPropagation()
              close()
            }}
          />
          <Dialog.Content
            ref={panel}
            className="avalon-actions-panel"
            aria-describedby={undefined}
            onKeyDown={move}
            onOpenAutoFocus={(event) => {
              event.preventDefault()
              origin.current = document.activeElement instanceof HTMLElement ? document.activeElement : null
              if (panel.current) actionsWithin(panel.current)[0]?.focus({ preventScroll: true })
            }}
            onCloseAutoFocus={(event) => {
              event.preventDefault()
              if (restoreFocus) restoreFocus()
              else if (origin.current?.isConnected) origin.current.focus({ preventScroll: true })
            }}
            onContextMenu={(event) => {
              event.preventDefault()
              event.stopPropagation()
              close()
            }}
          >
            <Dialog.Title className="avalon-actions-title">{title}</Dialog.Title>
            <div className="avalon-actions-body">
              <CloseActionsContext.Provider value={close}>{children}</CloseActionsContext.Provider>
            </div>
            <footer className="avalon-actions-hints">
              {footer ?? (
                <span>
                  A · Select <span>B · Close</span>
                </span>
              )}
            </footer>
          </Dialog.Content>
        </Dialog.Portal>
      )}
    </Dialog.Root>
  )
}

export function AvalonAction({
  label,
  description,
  icon: Icon = ChevronRight,
  disabled = false,
  current = false,
  onChoose,
  closeOnChoose = true,
}: {
  label: string
  description?: string
  icon?: LucideIcon
  disabled?: boolean
  current?: boolean
  closeOnChoose?: boolean
  onChoose(): void
}) {
  const close = useContext(CloseActionsContext)
  return (
    <button
      className="avalon-action"
      aria-current={current || undefined}
      aria-label={label}
      aria-description={description}
      disabled={disabled}
      onClick={() => {
        if (closeOnChoose) close()
        onChoose()
      }}
      title={description}
    >
      <Icon aria-hidden="true" />
      <span>
        {label}
        {description && <small>{description}</small>}
      </span>
    </button>
  )
}
