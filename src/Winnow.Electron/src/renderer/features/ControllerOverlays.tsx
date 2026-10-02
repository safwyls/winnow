import { useCallback, useEffect, useRef, useState } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { setTextValue } from '../controller'
import type { ThemePage } from '../../shared/theme'
import type { Mode } from '../api/types'
import dpad from './assets/xbox_dpad.svg?raw'
import acceptButton from './assets/xbox_button_a_outline.svg?raw'
import backspaceButton from './assets/xbox_button_x_outline.svg?raw'
import enterTrigger from './assets/xbox_rt_outline.svg?raw'
import closeButton from './assets/xbox_button_b_outline.svg?raw'
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
type KeyboardKey = { label: string; shifted?: string; width: number; action?: boolean }
const characters = (normal: string, shifted: string): KeyboardKey[] =>
  [...normal].map((label, index) => ({ label, shifted: shifted[index], width: 1 }))
const keyboardRows: KeyboardKey[][] = [
  [...characters('1234567890-=', '!@#$%^&*()_+'), { label: 'Backspace', width: 2.5, action: true }],
  [...characters('qwertyuiop[]\\', 'QWERTYUIOP{}|'), { label: 'Delete', width: 1.5, action: true }],
  [
    { label: 'Case', width: 1.75, action: true },
    ...characters("asdfghjkl;'", 'ASDFGHJKL:\"'),
    { label: 'Enter', width: 1.75, action: true },
  ],
  [
    { label: '`', shifted: '~', width: 1.75 },
    ...characters('zxcvbnm,./', 'ZXCVBNM<>?'),
    { label: '', width: 0.75 },
    { label: 'Up', width: 1, action: true },
    { label: '', width: 1 },
  ],
  [
    { label: 'Done', width: 2, action: true },
    { label: 'Space', width: 9.5, action: true },
    ...['Left', 'Down', 'Right'].map((label) => ({ label, width: 1, action: true })),
  ],
]
const keyCenter = (row: number, column: number) =>
  keyboardRows[row].slice(0, column).reduce((sum, key) => sum + key.width, 0) +
  keyboardRows[row][column].width / 2
export function OnScreenKeyboard({
  input,
  close,
  mode = 'desktop',
}: {
  input: HTMLInputElement | HTMLTextAreaElement
  close(): void
  mode?: Mode
}) {
  if (!keyboardTargets.has(input)) keyboardTargets.set(input, ++nextKeyboardTarget)
  return <KeyboardSession key={keyboardTargets.get(input)} input={input} close={close} mode={mode} />
}

const keyboardHints = [
  ['D-pad', 'Move', dpad],
  ['A', 'Type', acceptButton],
  ['X', 'Backspace', backspaceButton],
  ['RT', 'Enter', enterTrigger],
  ['B', 'Close', closeButton],
] as const

function KeyboardSession({
  input,
  close,
  mode,
}: {
  input: HTMLInputElement | HTMLTextAreaElement
  close(): void
  mode: Mode
}) {
  const [shift, setShift] = useState(false)
  const [isClosed, setClosed] = useState(false)
  const keys = useRef<Array<Array<HTMLButtonElement | null>>>([])
  const [value, setValue] = useState(input.value)
  const [cursor, setCursor] = useState(input.selectionStart ?? input.value.length)
  const [end, setEnd] = useState(input.selectionEnd ?? input.value.length)
  // React can restore the unfocused input's old DOM selection after its value changes.
  // While the keyboard owns editing, its caret is authoritative between commits.
  const selection = useRef({ start: cursor, end })
  const secure = useRef(input.type === 'password').current
  const closed = useRef(false)
  const closeCurrent = useRef(close)
  closeCurrent.current = close
  const lastValue = useRef(input.value)
  // Some fullscreen fields keep their editor input hidden behind a named button.
  const origin = useRef(document.activeElement as HTMLElement | null)
  const restoreFocus = useCallback(() => {
    if (!input.isConnected || input.disabled || input.readOnly) return
    input.focus()
    if (document.activeElement !== input && origin.current?.isConnected) origin.current.focus()
  }, [input])
  const multiline = input instanceof HTMLTextAreaElement
  const finish = useCallback(
    (submit = false) => {
      if (closed.current) return
      closed.current = true
      setClosed(true)
      closeCurrent.current()
      if (!input.isConnected || input.disabled || input.readOnly) return
      restoreFocus()
      try {
        input.setSelectionRange(selection.current.start, selection.current.end)
      } catch {
        /* Number fields have no selection. */
      }
      if (submit && !(input instanceof HTMLTextAreaElement)) {
        const unhandled = input.dispatchEvent(
          new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }),
        )
        if (unhandled) input.form?.requestSubmit()
      }
    },
    [input, restoreFocus],
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
        selection.current = {
          start: input.selectionStart ?? input.value.length,
          end: input.selectionEnd ?? input.value.length,
        }
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
    selection.current = { start: start + insert.length, end: start + insert.length }
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
    selection.current = { start: next, end: next }
    try {
      input.setSelectionRange(next, next)
    } catch {
      /* Number fields do not expose a selection. */
    }
  }
  const activate = (key: KeyboardKey) => {
    if (closed.current) return
    if (!key.action) return change(shift ? key.shifted! : key.label)
    switch (key.label) {
      case 'Case':
        setShift((value) => !value)
        break
      case 'Space':
        change(' ')
        break
      case 'Backspace':
        change('', 'backward')
        break
      case 'Delete':
        change('', 'forward')
        break
      case 'Enter':
        multiline ? change('\n') : finish(true)
        break
      case 'Done':
        finish()
        break
      case 'Left':
      case 'Right':
      case 'Up':
      case 'Down':
        moveCaret(key.label.toLowerCase() as 'left' | 'right' | 'up' | 'down')
    }
  }
  return (
    <Dialog.Root
      open={!isClosed}
      onOpenChange={(open) => {
        if (!open) finish()
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay keyboard-overlay" />
        <Dialog.Content
          className="dialog-content onscreen-keyboard"
          onOpenAutoFocus={(event) => {
            event.preventDefault()
            keys.current[0]?.[0]?.focus()
          }}
          onKeyDown={(event) => {
            if (!['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(event.key)) return
            event.preventDefault()
            event.stopPropagation()
            if (closed.current) return
            let row = keys.current.findIndex((items) =>
              items.includes(document.activeElement as HTMLButtonElement),
            )
            if (row < 0) return
            let column = keys.current[row].indexOf(document.activeElement as HTMLButtonElement)
            if (event.key === 'ArrowUp' || event.key === 'ArrowDown') {
              const center = keyCenter(row, column)
              row = (row + (event.key === 'ArrowUp' ? -1 : 1) + keyboardRows.length) % keyboardRows.length
              column = keyboardRows[row].reduce(
                (nearest, key, index) =>
                  key.label &&
                  Math.abs(keyCenter(row, index) - center) < Math.abs(keyCenter(row, nearest) - center)
                    ? index
                    : nearest,
                0,
              )
            } else {
              const direction = event.key === 'ArrowLeft' ? -1 : 1
              do {
                column = (column + direction + keyboardRows[row].length) % keyboardRows[row].length
              } while (!keyboardRows[row][column].label)
            }
            keys.current[row]?.[column]?.focus({ preventScroll: true })
          }}
          onCloseAutoFocus={(event) => {
            event.preventDefault()
            restoreFocus()
          }}
        >
          <Dialog.Title>Enter text</Dialog.Title>
          <Dialog.Description className={mode === 'fullscreen' ? 'keyboard-hints' : undefined}>
            {mode === 'fullscreen'
              ? keyboardHints.map(([button, label, artwork]) => (
                  <span className="keyboard-hint" key={button}>
                    <span
                      className="keyboard-hint-icon"
                      data-keyboard-glyph={button}
                      aria-hidden="true"
                      dangerouslySetInnerHTML={{
                        // Match the source geometry-sized glyph, excluding the asset's 8px padding.
                        __html: artwork.replace('<svg ', '<svg viewBox="8 8 48 48" '),
                      }}
                    />
                    <span className="sr-only">{button} </span>
                    <span>{label}</span>
                  </span>
                ))
              : 'D-pad moves · A types · X backspaces · RT enters · B closes'}
          </Dialog.Description>
          <output className="keyboard-preview" aria-label="Current text">
            {secure ? '•'.repeat(value.length) : value || ' '}
          </output>
          {keyboardRows.map((row, rowIndex) => (
            <div
              className="keyboard-row"
              key={rowIndex}
              style={{ gridTemplateColumns: row.map((key) => `${key.width}fr`).join(' ') }}
            >
              {row.map((key, column) =>
                key.label ? (
                  <button
                    key={column}
                    ref={(element) => {
                      ;(keys.current[rowIndex] ??= [])[column] = element
                    }}
                    aria-label={key.action ? key.label : undefined}
                    aria-pressed={key.label === 'Case' ? shift : undefined}
                    data-keyboard-backspace={key.label === 'Backspace' ? '' : undefined}
                    data-keyboard-enter={key.label === 'Enter' ? '' : undefined}
                    onClick={() => activate(key)}
                  >
                    {key.label === 'Case'
                      ? `Case: ${shift ? 'ABC' : 'abc'}`
                      : (({ Up: '↑', Left: '←', Down: '↓', Right: '→' } as Record<string, string>)[
                          key.label
                        ] ?? (key.action || !shift ? key.label : key.shifted))}
                  </button>
                ) : (
                  <span key={column} aria-hidden="true" />
                ),
              )}
            </div>
          ))}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
