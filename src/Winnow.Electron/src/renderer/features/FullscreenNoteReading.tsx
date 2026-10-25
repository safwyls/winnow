import { useRef, type ReactNode } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import './fullscreen-note-reading.css'

export function FullscreenNoteReading({
  open,
  onOpenChange,
  title,
  note,
  restoreFocus,
  regionLabel = 'Session note',
  hints,
  children,
  scrollWithArrows = true,
}: {
  open: boolean
  onOpenChange(open: boolean): void
  title: string
  note?: string
  restoreFocus(): void
  regionLabel?: string
  hints?: ReactNode
  children?: ReactNode
  scrollWithArrows?: boolean
}) {
  const back = useRef<HTMLButtonElement>(null)
  const prose = useRef<HTMLDivElement>(null)
  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay" />
        <Dialog.Content
          className="dialog-content fullscreen-note-reading"
          aria-describedby={undefined}
          onOpenAutoFocus={(event) => {
            event.preventDefault()
            back.current?.focus()
          }}
          onCloseAutoFocus={(event) => {
            event.preventDefault()
            restoreFocus()
          }}
          onKeyDown={(event) => {
            if (!scrollWithArrows) return
            if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return
            event.preventDefault()
            event.stopPropagation()
            prose.current?.scrollBy({ top: event.key === 'ArrowDown' ? 160 : -160, behavior: 'instant' })
          }}
        >
          <header>
            <Dialog.Close ref={back}>Back</Dialog.Close>
            <Dialog.Title>{title}</Dialog.Title>
          </header>
          <div ref={prose} className="fullscreen-note-scroll" role="region" aria-label={regionLabel}>
            {children ?? <p>{note}</p>}
          </div>
          {hints}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
