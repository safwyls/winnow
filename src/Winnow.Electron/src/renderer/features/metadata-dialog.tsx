import { useRef, useState } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { ArrowLeft } from 'lucide-react'
import type { Mode } from '../api/types'
import { MetadataEditor, type MetadataEditorNavigation } from './parity-details'
import './metadata-dialog.css'

export function MetadataDialog({
  workId,
  coverWorkId,
  title,
  mode,
  onClose,
  editText,
}: {
  workId: number
  coverWorkId?: number
  title: string
  mode: Mode
  onClose(): void
  editText?(input: HTMLInputElement | HTMLTextAreaElement): void
}) {
  const busy = useRef(false),
    navigation = useRef<MetadataEditorNavigation>(null)
  const [isBusy, setIsBusy] = useState(false)
  const origin = useRef(document.activeElement instanceof HTMLElement ? document.activeElement : null)
  function back() {
    if (busy.current) return
    if (!navigation.current?.back()) onClose()
  }
  return (
    <Dialog.Root
      open
      onOpenChange={(open) => {
        if (!open) back()
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay metadata-dialog-scrim" />
        <Dialog.Content
          className={`metadata-dialog mode-${mode}`}
          aria-describedby={undefined}
          onPointerDownOutside={(event) => event.preventDefault()}
          onEscapeKeyDown={(event) => {
            event.preventDefault()
            event.stopPropagation()
            back()
          }}
          onOpenAutoFocus={(event) => {
            event.preventDefault()
            navigation.current?.focus()
          }}
          onCloseAutoFocus={(event) => {
            event.preventDefault()
            if (origin.current?.isConnected) origin.current.focus({ preventScroll: true })
          }}
        >
          <header className="metadata-dialog-header">
            <button onClick={back} disabled={isBusy}>
              <ArrowLeft size={18} aria-hidden="true" /> Back
            </button>
            <Dialog.Title>Edit metadata · {title}</Dialog.Title>
          </header>
          <div className="metadata-dialog-body">
            <MetadataEditor
              workId={workId}
              coverWorkId={coverWorkId}
              mode={mode}
              navigation={mode === 'fullscreen' ? 'fields' : 'form'}
              navigationRef={navigation}
              onBusyChange={(value) => {
                busy.current = value
                setIsBusy(value)
              }}
              editText={editText}
            />
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
