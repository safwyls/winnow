import { useRef, useState } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { useQueryClient } from '@tanstack/react-query'
import type { Mode } from '../api/types'
import { useCommand } from '../api/hooks'
import { AvalonActions } from '../themes/avalon-actions'
import { Notice } from './shared'

export function LibraryHideConfirmation({
  mode,
  workIds,
  close,
  hidden,
}: {
  mode: Mode
  workIds: number[]
  close(): void
  hidden(): void
}) {
  const command = useCommand()
  const client = useQueryClient()
  const [saving, setSaving] = useState(false)
  const writing = useRef(false)
  const cancel = useRef<HTMLButtonElement>(null)
  const title = workIds.length === 1 ? 'Hide this game?' : `Hide ${workIds.length} games?`
  const dismiss = () => {
    if (!writing.current) close()
  }
  async function save() {
    if (writing.current) return
    writing.current = true
    setSaving(true)
    try {
      await command.mutateAsync({ route: 'hidden.put', body: { workIds, hidden: true } })
      await client.refetchQueries(
        { queryKey: ['api', 'library.get'], type: 'active' },
        { cancelRefetch: false },
      )
      hidden()
    } catch {
      // Keep the selected group available for retry when persistence fails.
    } finally {
      writing.current = false
      setSaving(false)
    }
  }
  const content = (
    <>
      <p>Their history stays saved. Restore hidden games one at a time from Library tools.</p>
      <div className="form-actions">
        <button ref={cancel} disabled={saving} onClick={dismiss}>
          Cancel
        </button>
        <button disabled={saving} onClick={() => void save()}>
          {workIds.length === 1 ? 'Hide game' : `Hide ${workIds.length} games`}
        </button>
      </div>
      <Notice error={command.error} />
    </>
  )
  if (mode === 'fullscreen')
    return (
      <AvalonActions open title={title} close={dismiss}>
        {content}
      </AvalonActions>
    )
  return (
    <Dialog.Root
      open
      onOpenChange={(open) => {
        if (!open) dismiss()
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay" />
        <Dialog.Content
          className="dialog-content feature-panel confirmation-dialog"
          aria-describedby={undefined}
          onOpenAutoFocus={(event) => {
            event.preventDefault()
            cancel.current?.focus()
          }}
          onCloseAutoFocus={(event) => event.preventDefault()}
          onEscapeKeyDown={(event) => {
            if (writing.current) event.preventDefault()
          }}
          onPointerDownOutside={(event) => event.preventDefault()}
        >
          <Dialog.Title>{title}</Dialog.Title>
          {content}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
