import { useCallback, useEffect, useRef, useState } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { setTextValue } from '../controller'
import type { ThemePage } from '../../shared/theme'
import './controller.css'
import { QuickUpdate } from './Updates'

export function QuickMenu({
  close,
  navigate,
  exit,
  atRoot = true,
}: {
  close(): void
  navigate(page: ThemePage): void
  exit(): void
  atRoot?: boolean
}) {
  const originalFocus = useRef(document.activeElement)
  const [confirmQuit, setConfirmQuit] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)
  const [quitting, setQuitting] = useState(false)
  const quit = async () => {
    setQuitting(true)
    try {
      await window.winnow.quit?.()
      close()
    } catch {
      setFailure('Winnow could not quit. Try again.')
      setQuitting(false)
    }
  }
  return (
    <Dialog.Root
      open
      onOpenChange={(open) => {
        if (!open && !quitting) close()
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay" />
        <Dialog.Content
          className="dialog-content controller-menu"
          role={confirmQuit ? 'alertdialog' : 'dialog'}
          onCloseAutoFocus={(event) => {
            event.preventDefault()
            const target = originalFocus.current
            if (target instanceof HTMLElement && target.isConnected) target.focus()
          }}
        >
          <Dialog.Title>{confirmQuit ? 'Quit Winnow?' : 'Quick menu'}</Dialog.Title>
          <Dialog.Description>
            {confirmQuit ? 'Your library will be here when you return.' : 'Resume where you left off.'}
          </Dialog.Description>
          {confirmQuit ? (
            <>
              <button disabled={quitting} autoFocus onClick={close}>
                Keep Winnow open
              </button>
              <button
                disabled={quitting}
                onClick={() => {
                  void quit()
                }}
              >
                Quit Winnow
              </button>
              {failure && <p role="alert">{failure}</p>}
            </>
          ) : (
            <>
              <button onClick={close}>Resume</button>
              <QuickUpdate close={close} />
              {atRoot && (
                <button
                  onClick={() => {
                    close()
                    navigate('settings')
                  }}
                >
                  Settings
                </button>
              )}
              <button
                onClick={() => {
                  close()
                  exit()
                }}
              >
                Exit fullscreen
              </button>
              {window.winnow?.quit && <button onClick={() => setConfirmQuit(true)}>Quit Winnow</button>}
            </>
          )}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
const keyboardTargets = new WeakMap<Element, number>()
let nextKeyboardTarget = 0
export function OnScreenKeyboard({
  input,
  close,
}: {
  input: HTMLInputElement | HTMLTextAreaElement
  close(): void
}) {
  if (!keyboardTargets.has(input)) keyboardTargets.set(input, ++nextKeyboardTarget)
  return <KeyboardSession key={keyboardTargets.get(input)} input={input} close={close} />
}

function KeyboardSession({ input, close }: { input: HTMLInputElement | HTMLTextAreaElement; close(): void }) {
  const [shift, setShift] = useState(false)
  const [value, setValue] = useState(input.value)
  const [cursor, setCursor] = useState(input.selectionStart ?? input.value.length)
  const [end, setEnd] = useState(input.selectionEnd ?? input.value.length)
  // React can restore the unfocused input's old DOM selection after its value changes.
  // While the keyboard owns editing, its caret is authoritative between commits.
  const selection = useRef({start:cursor,end})
  const secure = useRef(input.type === 'password').current
  const closed = useRef(false)
  const closeCurrent = useRef(close)
  closeCurrent.current = close
  const lastValue = useRef(input.value)
  const multiline = input instanceof HTMLTextAreaElement
  const finish = useCallback(
    (submit = false) => {
      if (closed.current) return
      closed.current = true
      closeCurrent.current()
      if (!input.isConnected || input.disabled || input.readOnly) return
      input.focus()
      try { input.setSelectionRange(selection.current.start,selection.current.end) } catch { /* Number fields have no selection. */ }
      if (submit && !(input instanceof HTMLTextAreaElement)) {
        const unhandled = input.dispatchEvent(
          new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }),
        )
        if (unhandled) input.form?.requestSubmit()
      }
    },
    [input],
  )
  useEffect(() => {
    let frame = 0
    const inspect = () => {
      if (closed.current) return
      if (!input.isConnected || input.disabled || input.readOnly) {
        finish()
        return
      }
      if (input.value !== lastValue.current) {
        lastValue.current = input.value
        setValue(input.value)
        setCursor(input.selectionStart ?? input.value.length)
        setEnd(input.selectionEnd ?? input.value.length)
        selection.current={start:input.selectionStart ?? input.value.length,end:input.selectionEnd ?? input.value.length}
      }
      frame = requestAnimationFrame(inspect)
    }
    frame = requestAnimationFrame(inspect)
    return () => cancelAnimationFrame(frame)
  }, [input, finish])
  const change = (text: string, remove: 'backward' | 'forward' | false = false) => {
    if (closed.current) return
    if (!input.isConnected || input.disabled || input.readOnly) {
      finish()
      return
    }
    const current = input.value
    let start = selection.current.start,
      finishAt = selection.current.end
    if (remove && start === finishAt) {
      const segments = new Intl.Segmenter(undefined, { granularity: 'grapheme' })
      if (remove === 'backward') start = [...segments.segment(current.slice(0, start))].at(-1)?.index ?? 0
      else finishAt += [...segments.segment(current.slice(finishAt))][0]?.segment.length ?? 0
    }
    const limit = input.maxLength >= 0 ? input.maxLength : Infinity
    const insert = text.slice(0, Math.max(0, limit - (current.length - (finishAt - start))))
    const next = current.slice(0, start) + insert + current.slice(finishAt)
    lastValue.current = next
    setValue(next)
    setCursor(start + insert.length)
    setEnd(start + insert.length)
    selection.current={start:start+insert.length,end:start+insert.length}
    setTextValue(input, next)
    try {
      input.setSelectionRange(start + insert.length, start + insert.length)
    } catch {
      /* Number fields do not expose a selection. */
    }
  }
  const moveCaret = (direction: 'left' | 'right' | 'up' | 'down') => {
    if (closed.current) return
    if (!input.isConnected || input.disabled || input.readOnly) {
      finish()
      return
    }
    const current = input.value,
      start = selection.current.start,
      finishAt = selection.current.end
    let next = direction === 'right' ? finishAt : start
    const segments = new Intl.Segmenter(undefined, { granularity: 'grapheme' })
    if (direction === 'left' && start === finishAt)
      next = [...segments.segment(current.slice(0, start))].at(-1)?.index ?? 0
    if (direction === 'right' && start === finishAt)
      next += [...segments.segment(current.slice(next))][0]?.segment.length ?? 0
    if (direction === 'up' || direction === 'down') {
      const lineStart = start === 0 ? 0 : current.lastIndexOf('\n', start - 1) + 1
      const lineEnd = current.indexOf('\n', start),
        column = start - lineStart
      if (direction === 'up') {
        const previousStart = lineStart <= 1 ? 0 : current.lastIndexOf('\n', lineStart - 2) + 1
        next = lineStart === 0 ? 0 : previousStart + Math.min(column, lineStart - previousStart - 1)
      } else
        next =
          lineEnd === -1
            ? current.length
            : Math.min(
                lineEnd + 1 + column,
                current.indexOf('\n', lineEnd + 1) === -1
                  ? current.length
                  : current.indexOf('\n', lineEnd + 1),
              )
    }
    setCursor(next)
    setEnd(next)
    selection.current={start:next,end:next}
    try {
      input.setSelectionRange(next, next)
    } catch {
      /* Number fields do not expose a selection. */
    }
  }
  return (
    <Dialog.Root
      open
      onOpenChange={(open) => {
        if (!open) finish()
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay keyboard-overlay" />
        <Dialog.Content
          className="dialog-content onscreen-keyboard"
          onCloseAutoFocus={(event) => {
            event.preventDefault()
            if (input.isConnected && !input.disabled) input.focus()
          }}
        >
          <Dialog.Title>Enter text</Dialog.Title>
          <Dialog.Description>A selects a key · X deletes · RT enters · B closes</Dialog.Description>
          <output className="keyboard-preview" aria-label="Current text">
            {secure ? '•'.repeat(value.length) : value || ' '}
          </output>
          {['1234567890', 'qwertyuiop', 'asdfghjkl', 'zxcvbnm', '-_./:@?&='].map((row) => (
            <div className="keyboard-row" key={row}>
              {[...row].map((key) => (
                <button key={key} onClick={() => change(shift ? key.toUpperCase() : key)}>
                  {shift ? key.toUpperCase() : key}
                </button>
              ))}
            </div>
          ))}
          <div className="keyboard-row">
            <button aria-pressed={shift} onClick={() => setShift(!shift)}>
              Shift
            </button>
            <button data-keyboard-backspace onClick={() => change('', 'backward')}>
              Backspace
            </button>
            <button onClick={() => change('', 'forward')}>Delete</button>
            <button data-keyboard-enter onClick={() => (multiline ? change('\n') : finish(true))}>
              Enter
            </button>
            <button onClick={() => finish()}>Done</button>
          </div>
          <div className="keyboard-bottom">
            <button className="keyboard-space" onClick={() => change(' ')}>
              Space
            </button>
            <div className="keyboard-arrows">
              <button className="keyboard-up" aria-label="Up" onClick={() => moveCaret('up')}>
                ↑
              </button>
              <button className="keyboard-left" aria-label="Left" onClick={() => moveCaret('left')}>
                ←
              </button>
              <button className="keyboard-down" aria-label="Down" onClick={() => moveCaret('down')}>
                ↓
              </button>
              <button className="keyboard-right" aria-label="Right" onClick={() => moveCaret('right')}>
                →
              </button>
            </div>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
