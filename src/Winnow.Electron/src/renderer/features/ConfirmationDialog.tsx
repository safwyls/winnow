import { useRef, type ReactElement } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { Notice } from './shared'

/** Destructive prompts begin on the safe action and retain their origin while saving. */
export function ConfirmationDialog({
  open, onOpenChange, trigger, title, description, confirmLabel, cancelLabel,
  pending, error, onConfirm,
}: {
  open: boolean
  onOpenChange(value: boolean): void
  trigger: ReactElement
  title: string
  description: string
  confirmLabel: string
  cancelLabel: string
  pending: boolean
  error?: unknown
  onConfirm(): void
}) {
  const cancel = useRef<HTMLButtonElement>(null)
  return <Dialog.Root open={open} onOpenChange={(value) => { if (!pending) onOpenChange(value) }}>
    <Dialog.Trigger asChild>{trigger}</Dialog.Trigger>
    <Dialog.Portal>
      <Dialog.Overlay className="dialog-overlay" />
      <Dialog.Content className="dialog-content feature-panel confirmation-dialog"
        onOpenAutoFocus={(event) => { event.preventDefault(); cancel.current?.focus() }}
        onEscapeKeyDown={(event) => { if (pending) event.preventDefault() }}
        onPointerDownOutside={(event) => event.preventDefault()}>
        <Dialog.Title>{title}</Dialog.Title>
        <Dialog.Description>{description}</Dialog.Description>
        <div className="form-actions">
          <button className="danger-action" disabled={pending} onClick={onConfirm}>{confirmLabel}</button>
          <Dialog.Close asChild><button ref={cancel} disabled={pending}>{cancelLabel}</button></Dialog.Close>
        </div>
        <Notice error={error} />
        {pending && <p role="status">Saving changes…</p>}
      </Dialog.Content>
    </Dialog.Portal>
  </Dialog.Root>
}
