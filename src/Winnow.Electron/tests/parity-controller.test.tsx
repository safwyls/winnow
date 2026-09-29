// @vitest-environment jsdom
import { useState } from 'react'
import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { ControllerEdges, moveControllerFocus, spatialTarget } from '../src/renderer/controller'
import { OnScreenKeyboard, QuickMenu } from '../src/renderer/features/ControllerOverlays'

afterEach(cleanup)
describe('controller movement and resume parity', () => {
  it('moves in the requested geometric direction instead of DOM order', () => {
    const cells = [{ left: 0, top: 0, width: 40, height: 60 }, { left: 0, top: 80, width: 40, height: 60 }, { left: 60, top: 0, width: 40, height: 60 }, { left: 60, top: 80, width: 40, height: 60 }]
    expect(spatialTarget(cells, 0, 'right')).toBe(2)
    expect(spatialTarget(cells, 0, 'down')).toBe(1)
    expect(spatialTarget(cells, 3, 'up')).toBe(2)
    expect(spatialTarget(cells, 0, 'left')).toBe(0)
  })
  it('requires neutral buttons and sticks after connect, blur, and reconnect', () => {
    const input = new ControllerEdges()
    expect(input.sample(0, [true], [0], true)).toEqual([])
    expect(input.armed).toBe(false)
    expect(input.sample(0, [false], [.8], true)).toEqual([])
    expect(input.armed).toBe(false)
    input.sample(0, [false], [0], true)
    expect(input.sample(0, [true], [0], true)).toEqual([0])
    expect(input.sample(0, [true], [0], true)).toEqual([])
    input.sample(0, [true], [0], false)
    expect(input.sample(0, [true], [0], true)).toEqual([])
    input.sample(0, [false], [0], true)
    expect(input.sample(1, [true], [0], true)).toEqual([])
  })
  it('adjusts focused range/select fields without moving away', () => {
    render(<><select aria-label="Sort"><option>A</option><option>B</option></select><input aria-label="Scale" type="range" min="0.8" max="1.2" step=".05" defaultValue="1"/></>)
    const select = screen.getByLabelText('Sort') as HTMLSelectElement
    select.focus(); moveControllerFocus('right'); expect(select.value).toBe('B')
    const slider = screen.getByLabelText('Scale') as HTMLInputElement
    slider.focus(); moveControllerFocus('left'); expect(Number(slider.value)).toBeCloseTo(.95)
  })
})

function KeyboardFixture({ secret = false, multiline = false }: { secret?: boolean; multiline?: boolean }) {
  const [value, setValue] = useState('')
  const [input, setInput] = useState<HTMLInputElement | HTMLTextAreaElement | null>(null)
  const [submitted, setSubmitted] = useState(false)
  return <><form onSubmit={event => event.preventDefault()}>{multiline ? <textarea aria-label="Note" value={value} onChange={event => setValue(event.target.value)} onFocus={event => { if (!input) setInput(event.currentTarget) }}/>
    : <input aria-label="Name" type={secret ? 'password' : 'text'} value={value} onChange={event => setValue(event.target.value)} onKeyDown={event => { if (event.key === 'Enter') setSubmitted(true) }} onFocus={event => { if (!input) setInput(event.currentTarget) }}/>}</form>
    {input && <OnScreenKeyboard input={input} close={() => setInput(null)}/>}{submitted && <p>Entered</p>}</>
}
describe('controller keyboard and quick menu parity', () => {
  it('updates a controlled field and backspaces without submitting until Enter', () => {
    render(<KeyboardFixture/>); fireEvent.focus(screen.getByLabelText('Name'))
    fireEvent.click(screen.getByRole('button', { name: 'a' }))
    fireEvent.click(screen.getByRole('button', { name: 'b' }))
    expect((screen.getByLabelText('Name') as HTMLInputElement).value).toBe('ab')
    fireEvent.click(screen.getByRole('button', { name: 'Backspace' }))
    expect((screen.getByLabelText('Name') as HTMLInputElement).value).toBe('a')
    expect(screen.queryByText('Entered')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Enter' }))
    expect(screen.getByText('Entered')).toBeTruthy()
  })
  it('masks password previews', () => {
    render(<KeyboardFixture secret/>); fireEvent.focus(screen.getByLabelText('Name'))
    fireEvent.click(screen.getByRole('button', { name: 'a' }))
    expect(screen.getByLabelText('Current text').textContent).toBe('•')
  })
  it('inserts a newline for multiline fields', () => {
    render(<KeyboardFixture multiline/>); fireEvent.focus(screen.getByLabelText('Note'))
    fireEvent.click(screen.getByRole('button', { name: 'a' }))
    fireEvent.click(screen.getByRole('button', { name: 'Enter' }))
    expect((screen.getByLabelText('Note') as HTMLTextAreaElement).value).toBe('a\n')
    expect(screen.getByRole('dialog')).toBeTruthy()
  })
  it('provides Settings at the root and closes before navigating', () => {
    const called: string[] = []
    render(<QuickMenu close={() => called.push('close')} navigate={page => called.push(page)} exit={() => called.push('exit')}/>)
    expect(screen.getByRole('button', { name: 'Exit fullscreen' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Resume' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Library' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }))
    expect(called).toEqual(['close', 'settings'])
  })
})
