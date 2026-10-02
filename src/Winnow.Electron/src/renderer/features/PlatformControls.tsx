import { useEffect, useRef, useState, type KeyboardEvent } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { editable, isControllerNavigationEvent } from '../controller'
import type { Mode } from '../api/types'
import { Notice } from './shared'
import selectGlyph from './assets/xbox_button_a_outline.svg?raw'
import backGlyph from './assets/xbox_button_b_outline.svg?raw'
import keyboardGlyph from './assets/xbox_button_y_outline.svg?raw'
import './fullscreen-platforms.css'

export function platformVerticalFocus(event: KeyboardEvent<HTMLElement>) {
  if (event.defaultPrevented || !event.currentTarget.contains(event.target as Node)) return
  if (!['ArrowDown', 'ArrowUp', 'ArrowLeft', 'ArrowRight'].includes(event.key)) return
  const controls = [
    ...event.currentTarget.querySelectorAll<HTMLElement>('button, input, select, textarea, summary'),
  ].filter(
    (control) =>
      !control.matches(':disabled, [type="hidden"], [tabindex="-1"]') &&
      !control.closest('[hidden], [inert]') &&
      ![...event.currentTarget.querySelectorAll('details:not([open])')].some(
        (details) => details.contains(control) && details.querySelector('summary') !== control,
      ),
  )
  const index = controls.indexOf(document.activeElement as HTMLElement)
  if (index < 0) return
  if ((event.key === 'ArrowLeft' || event.key === 'ArrowRight') && editable(document.activeElement)) {
    if (isControllerNavigationEvent(event.nativeEvent)) event.preventDefault()
    event.stopPropagation()
    return
  }
  event.preventDefault()
  event.stopPropagation()
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    const next = controls[index + (event.key === 'ArrowDown' ? 1 : -1)]
    next?.focus()
    next?.scrollIntoView?.({ block: 'nearest' })
  }
}

export function PlatformHints() {
  const root = useRef<HTMLDivElement>(null)
  const [keyboard, setKeyboard] = useState(false)
  useEffect(() => {
    const update = () => {
      const field = document.activeElement
      const scope = root.current?.closest('.fullscreen-platform-page, .fullscreen-platform-dialog')
      setKeyboard(editable(field) && !field.disabled && !field.readOnly && !!scope?.contains(field))
    }
    update()
    document.addEventListener('focusin', update)
    document.addEventListener('focusout', update)
    return () => {
      document.removeEventListener('focusin', update)
      document.removeEventListener('focusout', update)
    }
  }, [])
  return (
    <div ref={root} className="platform-controller-hints" role="group" aria-label="Platform controls">
      {[
        [selectGlyph, 'A', 'Select'],
        [backGlyph, 'B', 'Back'],
        ...(keyboard ? [[keyboardGlyph, 'Y', 'Keyboard']] : []),
      ].map(([art, key, label]) => (
        <span key={key}>
          <span
            aria-hidden="true"
            data-platform-glyph={key}
            dangerouslySetInnerHTML={{ __html: art.replace('<svg ', '<svg viewBox="8 8 48 48" ') }}
          />
          <span className="sr-only">{key} </span>
          {label}
        </span>
      ))}
    </div>
  )
}

export function PlatformConfirmation({
  open,
  setOpen,
  title,
  description,
  mode,
  pending,
  error,
  confirm,
  returnFocus,
}: {
  open: boolean
  setOpen(open: boolean): void
  title: string
  description: string
  mode: Mode
  pending: boolean
  error?: unknown
  confirm(): void
  returnFocus?(): HTMLElement | null
}) {
  const origin = useRef<HTMLElement | null>(null)
  return (
    <Dialog.Root
      open={open}
      onOpenChange={(next) => {
        if (!pending) setOpen(next)
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="setup-overlay consent-overlay" />
        <Dialog.Content
          className={`setup-dialog consent-dialog${mode === 'fullscreen' ? ' fullscreen-platform-dialog' : ''}`}
          onOpenAutoFocus={(event) => {
            origin.current = document.activeElement as HTMLElement
            event.preventDefault()
            document.querySelector<HTMLButtonElement>('[data-platform-confirm-cancel]')?.focus()
          }}
          onCloseAutoFocus={(event) => {
            event.preventDefault()
            const target = origin.current?.isConnected ? origin.current : returnFocus?.()
            target?.focus()
          }}
          onEscapeKeyDown={(event) => {
            event.stopPropagation()
            if (pending) event.preventDefault()
          }}
          onKeyDown={mode === 'fullscreen' ? platformVerticalFocus : undefined}
        >
          <div className={`setup-body${mode === 'fullscreen' ? ' fullscreen-platform-content' : ''}`}>
            <Dialog.Title>{title}</Dialog.Title>
            <Dialog.Description>{description}</Dialog.Description>
            <div className="form-actions">
              <button data-platform-confirm-cancel disabled={pending} onClick={() => setOpen(false)}>
                Cancel
              </button>
              <button disabled={pending} onClick={confirm}>
                Sign out
              </button>
            </div>
            <Notice error={error} />
          </div>
          {mode === 'fullscreen' && <PlatformHints />}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
