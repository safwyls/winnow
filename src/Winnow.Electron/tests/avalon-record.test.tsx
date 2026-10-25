// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { AvalonRecord } from '../src/renderer/themes/avalon-record'

afterEach(cleanup)

describe('desktop list row focus', () => {
  it('selects on keyboard focus but defers a pointer press until the click lands', () => {
    const onFocus = vi.fn(),
      onClick = vi.fn()
    render(
      <AvalonRecord data-avalon-game={1} onFocus={onFocus} onClick={onClick}>
        Alpha
      </AvalonRecord>,
    )
    const row = screen.getByRole('button', { name: 'Alpha' })
    expect(row.className).toBe('avalon-record')
    fireEvent.focus(row)
    expect(onFocus).toHaveBeenCalledTimes(1)
    fireEvent.blur(row)
    // The selection actions that focus reveals move every row, so a press waits for release.
    fireEvent.pointerDown(row)
    fireEvent.focus(row)
    expect(onFocus).toHaveBeenCalledTimes(1)
    fireEvent.click(row, { ctrlKey: true })
    expect(onFocus).toHaveBeenCalledTimes(2)
    expect(onClick).toHaveBeenCalledTimes(1)
    // A key press after a pointer press returns to immediate focus selection.
    fireEvent.keyDown(row, { key: 'ArrowDown' })
    fireEvent.blur(row)
    fireEvent.focus(row)
    expect(onFocus).toHaveBeenCalledTimes(3)
  })
})
