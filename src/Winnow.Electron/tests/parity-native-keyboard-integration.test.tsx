// @vitest-environment jsdom
import { useState } from 'react'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { OnScreenKeyboard, QuickMenu } from '../src/renderer/features/ControllerOverlays'

const fields: HTMLElement[] = []
const frames = new Map<number, FrameRequestCallback>()
let frameId = 0
beforeEach(() => {
  vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
    frames.set(++frameId, callback)
    return frameId
  })
  vi.stubGlobal('cancelAnimationFrame', (id: number) => frames.delete(id))
})
afterEach(() => {
  cleanup()
  fields.splice(0).forEach((field) => field.remove())
  frames.clear()
  vi.unstubAllGlobals()
})
function frame() {
  act(() => {
    const callbacks = [...frames.values()]
    frames.clear()
    callbacks.forEach((callback) => callback(0))
  })
}
function input(value: string, options: { password?: boolean; multiline?: boolean; maxLength?: number } = {}) {
  const field = document.createElement(options.multiline ? 'textarea' : 'input')
  if (field instanceof HTMLInputElement && options.password) field.type = 'password'
  if (options.maxLength !== undefined) field.maxLength = options.maxLength
  field.value = value
  document.body.append(field)
  fields.push(field)
  field.focus()
  field.setSelectionRange(value.length, value.length)
  return field
}
function click(name: string) {
  fireEvent.click(screen.getByRole('button', { name }))
}

describe('on-screen keyboard field lifetime and editing parity', () => {
  it('replaces a selection through the input binding without truncating the trailing text at maxlength', () => {
    const target = input('hello', { maxLength: 5 }),
      boundValue = vi.fn()
    target.setSelectionRange(1, 4)
    target.addEventListener('input', () => boundValue(target.value))
    render(<OnScreenKeyboard input={target} close={vi.fn()} />)
    click('1')
    expect(target.value).toBe('h1o')
    expect(boundValue).toHaveBeenLastCalledWith('h1o')
    click('1')
    click('1')
    click('1')
    expect(target.value).toBe('h111o')
    expect(boundValue).toHaveBeenLastCalledWith('h111o')
  })

  it('deletes a complete grapheme and refuses edits after the target becomes read-only', () => {
    const target = input('a👩‍💻'),
      close = vi.fn()
    render(<OnScreenKeyboard input={target} close={close} />)
    click('Backspace')
    expect(target.value).toBe('a')
    target.readOnly = true
    click('1')
    click('1')
    expect(target.value).toBe('a')
    expect(close).toHaveBeenCalledOnce()
  })

  it('closes once and stops editing even if the owner has not removed the keyboard yet', () => {
    const target = input('note'),
      close = vi.fn()
    render(<OnScreenKeyboard input={target} close={close} />)
    click('Done')
    click('Done')
    click('1')
    expect(close).toHaveBeenCalledOnce()
    expect(target.value).toBe('note')
  })

  it('Enter closes before submitting a single-line field while Done only closes', () => {
    const target = input('note'),
      calls: string[] = []
    target.addEventListener('keydown', (event) => {
      if ((event as KeyboardEvent).key === 'Enter') {
        calls.push('submit')
        event.preventDefault()
      }
    })
    const first = render(<OnScreenKeyboard input={target} close={() => calls.push('close')} />)
    click('Done')
    expect(calls).toEqual(['close'])
    first.unmount()
    render(<OnScreenKeyboard input={target} close={() => calls.push('close')} />)
    click('Enter')
    expect(calls).toEqual(['close', 'close', 'submit'])
  })

  it('retains password masking when the field type changes and never exposes the secret in keyboard content', () => {
    const target = input('secret-token', { password: true })
    render(<OnScreenKeyboard input={target} close={vi.fn()} />)
    click('1')
    expect(screen.getByLabelText('Current text').textContent).toBe('•'.repeat(13))
    ;(target as HTMLInputElement).type = 'text'
    target.value = 'another-secret'
    frame()
    expect(screen.getByLabelText('Current text').textContent).toBe('•'.repeat(14))
    expect(screen.getByRole('dialog').textContent).not.toContain('secret')
    expect(screen.getByRole('dialog').querySelector('input,textarea')).toBeNull()
  })

  it('tracks external edits while attached and closes without leaking detached updates', () => {
    const target = input('Original'),
      close = vi.fn()
    render(<OnScreenKeyboard input={target} close={close} />)
    target.value = 'Updated note'
    frame()
    expect(screen.getByLabelText('Current text').textContent).toBe('Updated note')
    target.remove()
    target.value = 'Detached'
    frame()
    expect(close).toHaveBeenCalledOnce()
    expect(screen.getByLabelText('Current text').textContent).toBe('Updated note')
    click('1')
    expect(target.value).toBe('Detached')
  })

  it('stops observing when the keyboard unmounts and reads the latest text on reopening', () => {
    const target = input('Original')
    const keyboard = render(<OnScreenKeyboard input={target} close={vi.fn()} />)
    target.value = 'Updated note'
    frame()
    const preview = screen.getByLabelText('Current text')
    keyboard.unmount()
    target.value = 'Detached'
    frame()
    expect(preview.textContent).toBe('Updated note')
    render(<OnScreenKeyboard input={target} close={vi.fn()} />)
    expect(screen.getByLabelText('Current text').textContent).toBe('Detached')
  })

  it('moves the multiline caret with the arrow cluster and keeps forward delete distinct from backspace', () => {
    const target = input('hello', { multiline: true }),
      close = vi.fn()
    render(<OnScreenKeyboard input={target} close={close} />)
    click('Enter')
    click('1')
    expect(target.value).toBe('hello\n1')
    expect(close).not.toHaveBeenCalled()
    click('Up')
    expect(target.selectionStart).toBe(1)
    click('Right')
    expect(target.selectionStart).toBe(2)
    click('Delete')
    expect(target.value).toBe('helo\n1')
    click('Down')
    expect(target.selectionStart).toBe(target.value.length)
    click('Backspace')
    expect(target.value).toBe('helo\n')
  })

  it('starts a fresh edit session when the target changes and edits only the current field', () => {
    const first = input('first'),
      second = input('second')
    const rendered = render(<OnScreenKeyboard input={first} close={vi.fn()} />)
    click('Shift')
    rendered.rerender(<OnScreenKeyboard input={second} close={vi.fn()} />)
    expect(screen.getByLabelText('Current text').textContent).toBe('second')
    click('a')
    expect(second.value).toBe('seconda')
    expect(first.value).toBe('first')
  })
})

function MenuFixture({ atRoot, exit = vi.fn() }: { atRoot: boolean; exit?: () => void }) {
  const [open, setOpen] = useState(false)
  return (
    <>
      <input aria-label="Editor draft" defaultValue="My unsaved note" />
      <button onClick={() => setOpen(true)}>Open quick menu</button>
      {open && <QuickMenu atRoot={atRoot} close={() => setOpen(false)} navigate={vi.fn()} exit={exit} />}
    </>
  )
}
describe('fullscreen nested quick menu parity', () => {
  it('preserves the nested editor, hides Settings, and restores the original focus on Resume', async () => {
    render(<MenuFixture atRoot={false} />)
    const draft = screen.getByLabelText('Editor draft') as HTMLInputElement
    draft.focus()
    fireEvent.click(screen.getByRole('button', { name: 'Open quick menu' }))
    expect(screen.queryByRole('button', { name: 'Settings' })).toBeNull()
    click('Resume')
    await waitFor(() => expect(document.activeElement).toBe(draft))
    expect(draft.value).toBe('My unsaved note')
  })

  it('requires an explicit confirmation before quitting and cancellation preserves the editor', async () => {
    const quit = vi.fn().mockResolvedValue(undefined)
    vi.stubGlobal('winnow', { quit })
    render(<MenuFixture atRoot={false} />)
    const draft = screen.getByLabelText('Editor draft') as HTMLInputElement
    fireEvent.click(screen.getByRole('button', { name: 'Open quick menu' }))
    click('Quit Winnow')
    expect(quit).not.toHaveBeenCalled()
    expect(screen.getByRole('alertdialog')).toBeTruthy()
    click('Keep Winnow open')
    expect(quit).not.toHaveBeenCalled()
    expect(draft.value).toBe('My unsaved note')
    fireEvent.click(screen.getByRole('button', { name: 'Open quick menu' }))
    click('Quit Winnow')
    click('Quit Winnow')
    await waitFor(() => expect(quit).toHaveBeenCalledOnce())
  })
})
