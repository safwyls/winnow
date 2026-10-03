// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { ChoiceMenu } from '../src/renderer/components/ChoiceMenu'
import { chooseOption } from './choice-test-utils'

afterEach(cleanup)
const options = [
  { value: '', label: 'Choose a game', disabled: true },
  { value: 'first', label: 'First game' },
  { value: 'locked', label: 'Unavailable game', disabled: true },
  { value: 'last', label: 'Last game' },
]

it('keeps field labels and descriptions and returns the trigger when a choice changes', () => {
  const change = vi.fn()
  render(
    <form aria-label="Preferences">
      <label htmlFor="choice">Main game</label>
      <span id="help">Choose the parent title.</span>
      <ChoiceMenu
        id="choice"
        label="Main game"
        aria-label="Main game"
        aria-describedby="help"
        name="parent"
        value="first"
        options={options}
        onChange={change}
      />
    </form>,
  )
  const trigger = screen.getByRole('button', { name: 'Main game' })
  expect(screen.getByLabelText('Main game')).toBe(trigger)
  expect(trigger.getAttribute('aria-describedby')).toBe('help')
  expect(new FormData(screen.getByRole('form') as HTMLFormElement).get('parent')).toBe('first')
  chooseOption(trigger, 'last')
  expect(change).toHaveBeenCalledExactlyOnceWith('last', trigger)
  expect(document.activeElement).toBe(trigger)
})

it('skips disabled choices for opening, arrow navigation, Home, End and typeahead', () => {
  const change = vi.fn()
  render(<ChoiceMenu label="Game" value="" options={options} onChange={change} />)
  const trigger = screen.getByRole('button')
  fireEvent.click(trigger)
  const first = screen.getByRole('menuitemradio', { name: 'First game' })
  const last = screen.getByRole('menuitemradio', { name: 'Last game' })
  expect(document.activeElement).toBe(first)
  fireEvent.keyDown(first, { key: 'ArrowDown' })
  expect(document.activeElement).toBe(last)
  fireEvent.keyDown(last, { key: 'Home' })
  expect(document.activeElement).toBe(first)
  fireEvent.keyDown(first, { key: 'u' })
  expect(document.activeElement).toBe(first)
  fireEvent.keyDown(first, { key: 'End' })
  expect(document.activeElement).toBe(last)
  fireEvent.keyDown(last, { key: 'ArrowDown' })
  expect(document.activeElement).toBe(first)
  fireEvent.click(screen.getByRole('menuitemradio', { name: 'Unavailable game' }))
  expect(change).not.toHaveBeenCalled()
})

it('does not open disabled fields or commit when a containing fieldset becomes disabled', () => {
  const change = vi.fn()
  const view = render(<ChoiceMenu label="Game" disabled value="first" options={options} onChange={change} />)
  fireEvent.keyDown(screen.getByRole('button'), { key: 'ArrowDown' })
  expect(screen.queryByRole('menu')).toBeNull()
  view.rerender(
    <fieldset>
      <ChoiceMenu label="Game" value="first" options={options} onChange={change} />
    </fieldset>,
  )
  fireEvent.click(screen.getByRole('button'))
  view.rerender(
    <fieldset disabled>
      <ChoiceMenu label="Game" value="first" options={options} onChange={change} />
    </fieldset>,
  )
  fireEvent.click(screen.getByRole('menuitemradio', { name: 'Last game' }))
  expect(change).not.toHaveBeenCalled()
  fireEvent.keyDown(screen.getByRole('button'), { key: 'ArrowDown' })
  expect(screen.queryByRole('menu')).toBeNull()
})

it('keeps an empty choice list focusable and dismissible without closing its parent dialog', () => {
  const closeParent = vi.fn()
  render(
    <div role="dialog" aria-label="Choose a parent" onKeyDown={closeParent}>
      <ChoiceMenu label="Game" value="" options={[options[0]!]} onChange={() => {}} />
    </div>,
  )
  const trigger = screen.getByRole('button')
  fireEvent.click(trigger)
  expect(document.activeElement).toBe(screen.getByRole('menu'))
  fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
  expect(screen.queryByRole('menu')).toBeNull()
  expect(document.activeElement).toBe(trigger)
  expect(closeParent).not.toHaveBeenCalled()
})
