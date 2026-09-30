import { useEffect, useRef, useState } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import type { FilePickerAction, FilePickerSnapshot } from '../../shared/file-picker'
import { restoreFocusWhenReady } from './restore-focus'
import './fullscreen-file-picker.css'

/** The renderer presents main's active chooser; it never supplies a filesystem path. */
export function FullscreenFilePicker() {
  const [picker, setPicker] = useState<FilePickerSnapshot | null>(null)
  const [name, setName] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const content = useRef<HTMLDivElement>(null)
  const origin = useRef<HTMLElement | null>(null)
  const current = useRef<FilePickerSnapshot | null>(null)
  const actionGeneration = useRef(0)
  useEffect(() => {
    let received = false
    let lastFocus: HTMLElement | null = null
    const rememberFocus = (event: FocusEvent) => {
      if (!current.current && event.target instanceof HTMLElement && event.target !== document.body)
        lastFocus = event.target
    }
    document.addEventListener('focusin', rememberFocus)
    const receive = (snapshot: FilePickerSnapshot | null) => {
      if (snapshot?.id !== current.current?.id) {
        actionGeneration.current++
        setBusy(false)
      }
      if (snapshot && current.current?.id !== snapshot.id) {
        origin.current =
          document.activeElement instanceof HTMLElement && document.activeElement !== document.body
            ? document.activeElement
            : lastFocus
        setName(snapshot.suggestedName ?? '')
      }
      current.current = snapshot
      setPicker(snapshot)
      setError('')
    }
    const unsubscribe = window.winnow.onFilePicker?.((snapshot) => {
      received = true
      receive(snapshot)
    })
    void window.winnow.filePickerSnapshot?.().then((snapshot) => {
      if (!received && snapshot) receive(snapshot)
    })
    return () => {
      unsubscribe?.()
      document.removeEventListener('focusin', rememberFocus)
    }
  }, [])
  const focusFirst = () =>
    content.current?.querySelector<HTMLElement>('input:not(:disabled),button:not(:disabled)')?.focus()
  useEffect(() => {
    if (picker && !picker.loading) focusFirst()
  }, [picker?.id, picker?.directory, picker?.page, picker?.replaceName, picker?.loading])
  async function act(action: FilePickerAction['action'], extra: Partial<FilePickerAction> = {}) {
    if (!picker || (busy && action !== 'back' && action !== 'cancel')) return
    const generation = ++actionGeneration.current
    const active = () => current.current?.id === picker.id && actionGeneration.current === generation
    setBusy(true)
    setError('')
    try {
      await window.winnow.filePickerAction?.({ id: picker.id, action, ...extra })
    } catch (error) {
      if (active())
        setError(
          error instanceof Error
            ? error.message.replace(/^Error invoking remote method '[^']+': Error: /, '')
            : 'This item could not be selected. Try again.',
        )
    } finally {
      if (active()) setBusy(false)
    }
  }
  return (
    <Dialog.Root
      open={Boolean(picker)}
      onOpenChange={(open) => {
        if (!open) void act('back')
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay" />
        <Dialog.Content
          ref={content}
          className="dialog-content fullscreen-file-picker"
          aria-describedby={undefined}
          onOpenAutoFocus={(event) => {
            event.preventDefault()
            focusFirst()
          }}
          onCloseAutoFocus={(event) => {
            event.preventDefault()
            restoreFocusWhenReady(origin.current)
          }}
          onInteractOutside={(event) => event.preventDefault()}
          onEscapeKeyDown={(event) => {
            event.preventDefault()
            void act('back')
          }}
          onKeyDown={(event) => {
            if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return
            event.preventDefault()
            event.stopPropagation()
            const controls = [
              ...content.current!.querySelectorAll<HTMLElement>('input:not(:disabled),button:not(:disabled)'),
            ]
            const index = controls.indexOf(document.activeElement as HTMLElement)
            const next =
              controls[
                Math.max(0, Math.min(controls.length - 1, index + (event.key === 'ArrowDown' ? 1 : -1)))
              ]
            next?.focus({ preventScroll: true })
            next?.scrollIntoView({ block: 'nearest', behavior: 'instant' })
          }}
        >
          <Dialog.Title>
            {picker?.replaceName ? `Replace ${picker.replaceName}?` : picker?.title}
          </Dialog.Title>
          {picker?.replaceName ? (
            <div className="file-picker-rows">
              <p>A file with this name already exists.</p>
              <button onClick={() => void act('back')}>Cancel</button>
              <button onClick={() => void act('replace')} aria-disabled={busy}>
                Replace file
              </button>
            </div>
          ) : (
            <>
              <p className="file-picker-location">{picker?.directory ?? 'Choose a drive'}</p>
              <div className="file-picker-rows" aria-busy={picker?.loading || busy}>
                {picker?.loading ? (
                  <p role="status">Reading folder…</p>
                ) : (
                  <>
                    {picker?.mode === 'save' && picker.directory && (
                      <>
                        <label>
                          File name
                          <input value={name} onChange={(event) => setName(event.target.value)} />
                        </label>
                        <button onClick={() => void act('save', { name })}>Save here</button>
                      </>
                    )}
                    {picker?.mode === 'directory' && picker.directory && (
                      <button onClick={() => void act('directory')}>Choose this folder</button>
                    )}
                    {picker?.directory && <button onClick={() => void act('parent')}>Parent folder</button>}
                    {picker?.entries.map((entry) => (
                      <button
                        key={entry.id}
                        aria-label={`${entry.directory ? 'Folder' : 'File'} ${entry.name}`}
                        onClick={() => void act('entry', { entryId: entry.id })}
                      >
                        <span className="file-picker-kind">{entry.directory ? 'Folder' : 'File'} </span>
                        {entry.name}
                      </button>
                    ))}
                    {picker && picker.page > 0 && (
                      <button onClick={() => void act('previous')}>Previous page</button>
                    )}
                    {picker && picker.page + 1 < picker.pages && (
                      <button onClick={() => void act('next')}>Next page</button>
                    )}
                  </>
                )}
                <button onClick={() => void act('cancel')}>Cancel</button>
                {(error || picker?.error) && <p role="alert">{error || picker?.error}</p>}
              </div>
            </>
          )}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
