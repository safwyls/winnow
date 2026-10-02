// @vitest-environment jsdom
import { useState } from 'react'
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { SortMenu } from '../src/renderer/components/SortMenu'

afterEach(cleanup)
const options = [
  { value: 'strength', label: 'Strongest match' },
  { value: 'playtime', label: 'Playtime at stake' },
  { value: 'title', label: 'Title' },
]
it('keeps a portalled menu within the owning dialog focus boundary', () => {
  render(
    <div role="dialog" aria-label="Tools">
      <SortMenu value="title" options={options} onChange={() => {}} />
    </div>,
  )
  fireEvent.click(screen.getByRole('button', { name: 'Sort · Title' }))
  const menu = screen.getByRole('menu')
  expect(menu.closest('[role="dialog"]')).toBe(screen.getByRole('dialog'))
  expect(menu.getAttribute('popover')).toBe('manual')
  expect(document.activeElement).toBe(screen.getByRole('menuitemradio', { checked: true }))
})
function setup() {
  const change = vi.fn()
  function Host() {
    const [value, setValue] = useState('playtime')
    return (
      <>
        <SortMenu
          value={value}
          options={options}
          onChange={(value) => {
            change(value)
            setValue(value)
          }}
        />
        <button>Following control</button>
      </>
    )
  }
  render(<Host />)
  return change
}
it('sort button announces the current order and opens selected radio items that close and restore focus on selection', () => {
  const change = setup()
  const trigger = screen.getByRole('button', { name: 'Sort · Playtime at stake' })
  expect(trigger.title).toBe('Sort order')
  expect(trigger.getAttribute('aria-haspopup')).toBe('menu')
  fireEvent.click(trigger)
  expect(trigger.getAttribute('aria-expanded')).toBe('true')
  expect(screen.getAllByRole('menuitemradio').map((item) => item.textContent)).toEqual(
    options.map((option) => option.label),
  )
  expect(document.activeElement).toBe(
    screen.getByRole('menuitemradio', { name: 'Playtime at stake', checked: true }),
  )
  fireEvent.click(screen.getByRole('menuitemradio', { name: 'Title' }))
  expect(change).toHaveBeenCalledExactlyOnceWith('title')
  expect(screen.queryByRole('menu')).toBeNull()
  expect(document.activeElement).toBe(trigger)
  expect(trigger.textContent).toBe('Sort · Title')
  expect(trigger.getAttribute('aria-expanded')).toBe('false')
})
it('menu arrows Home End and typeahead stay inside the sort control and Escape does not reach page navigation', () => {
  const change = setup()
  const trigger = screen.getByRole('button', { name: /^Sort ·/ })
  fireEvent.keyDown(trigger, { key: 'ArrowDown' })
  const first = screen.getByRole('menuitemradio', { name: 'Strongest match' }),
    last = screen.getByRole('menuitemradio', { name: 'Title' })
  expect(document.activeElement).toBe(first)
  fireEvent.keyDown(first, { key: 'ArrowUp' })
  expect(document.activeElement).toBe(last)
  fireEvent.keyDown(last, { key: 'Home' })
  expect(document.activeElement).toBe(first)
  fireEvent.keyDown(first, { key: 'End' })
  expect(document.activeElement).toBe(last)
  fireEvent.keyDown(last, { key: 'p' })
  expect(document.activeElement).toBe(screen.getByRole('menuitemradio', { name: 'Playtime at stake' }))
  const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })
  fireEvent(document.activeElement!, escape)
  expect(escape.defaultPrevented).toBe(true)
  expect(screen.queryByRole('menu')).toBeNull()
  expect(document.activeElement).toBe(trigger)
  expect(change).not.toHaveBeenCalled()
  fireEvent.keyDown(trigger, { key: 'ArrowUp' })
  expect(document.activeElement).toBe(screen.getByRole('menuitemradio', { name: 'Title' }))
})
it('outside pointer and Tab dismiss without choosing a sort or trapping focus', () => {
  const change = setup()
  const trigger = screen.getByRole('button', { name: /^Sort ·/ }),
    next = screen.getByRole('button', { name: 'Following control' })
  fireEvent.click(trigger)
  fireEvent.pointerDown(next)
  next.focus()
  expect(screen.queryByRole('menu')).toBeNull()
  expect(document.activeElement).toBe(next)
  fireEvent.click(trigger)
  const tab = new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true })
  fireEvent(document.activeElement!, tab)
  expect(tab.defaultPrevented).toBe(false)
  expect(screen.queryByRole('menu')).toBeNull()
  expect(document.activeElement).toBe(trigger)
  expect(change).not.toHaveBeenCalled()
})
it('ignores a queued scroll at the opening position but closes when its anchor moves', () => {
  setup()
  const trigger = screen.getByRole('button', { name: /^Sort ·/ })
  fireEvent.click(trigger)
  fireEvent.scroll(document)
  expect(screen.queryByRole('menu')).not.toBeNull()
  const bounds = trigger.getBoundingClientRect()
  vi.spyOn(trigger, 'getBoundingClientRect').mockReturnValue({
    ...bounds,
    top: bounds.top - 20,
    left: bounds.left,
  })
  fireEvent.scroll(document)
  expect(screen.queryByRole('menu')).toBeNull()
  expect(document.activeElement).toBe(trigger)
})
