import { useEffect, useRef, type ReactNode } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { AvalonActions } from './avalon-actions'
import './avalon-library-panel.css'

export function AvalonLibraryPanel({
  kind,
  close,
  restoreFocus,
  children,
}: {
  kind: 'options' | 'lists' | null
  close(): void
  restoreFocus(): void
  children: ReactNode
}) {
  const content = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (kind) content.current?.querySelector<HTMLButtonElement>('button:not(:disabled)')?.focus()
  }, [kind])
  if (kind === 'options')
    return (
      <AvalonActions
        open
        title="Library options"
        close={close}
        restoreFocus={restoreFocus}
        footer={
          <button className="avalon-library-panel-back" onClick={close}>
            Back to library
          </button>
        }
      >
        {children}
      </AvalonActions>
    )
  return (
    <Dialog.Root
      open={kind !== null}
      onOpenChange={(open) => {
        if (!open) close()
      }}
    >
      {kind && (
        <>
          <Dialog.Overlay className="avalon-library-panel-shade" />
          <Dialog.Content
            ref={content}
            className={`avalon-library-panel ${kind}`}
            aria-describedby={undefined}
            onCloseAutoFocus={(event) => {
              event.preventDefault()
              restoreFocus()
            }}
          >
            <Dialog.Title>My lists</Dialog.Title>
            <div className="avalon-library-panel-body">{children}</div>
            <Dialog.Close asChild>
              <button className="avalon-library-panel-back">Back to library</button>
            </Dialog.Close>
          </Dialog.Content>
        </>
      )}
    </Dialog.Root>
  )
}
