// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { useState } from 'react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { AvalonFullscreenGrid } from '../src/renderer/themes/avalon-fullscreen-grid'
import { clearViewState } from '../src/renderer/viewState'
import type { LibraryGame } from '../src/renderer/api/types'

let frames: FrameRequestCallback[] = []
const games = Array.from({ length: 90 }, (_, index) => ({
  workId: index + 1,
  title: `Game ${index + 1}`,
  entries: [],
  playtimeMinutes: 0,
  bucket: 'never_played',
})) as LibraryGame[]
function Harness({
  data = games,
  columns = 6,
  reduced = false,
  collection,
}: {
  data?: LibraryGame[]
  columns?: number
  reduced?: boolean
  collection?: string
}) {
  const [selected, setSelected] = useState<number | null>(null)
  return (
    <AvalonFullscreenGrid
      games={data}
      columns={columns}
      gap={24}
      prefix="fixture"
      collection={collection}
      selected={selected}
      onSelected={setSelected}
      reducedMotion={reduced}
      onKeyDown={() => {}}
    >
      {(game, _index, handlers) => (
        <button
          key={game.workId}
          data-avalon-game={game.workId}
          data-selected={game.workId === selected}
          {...handlers}
        >
          {game.title}
        </button>
      )}
    </AvalonFullscreenGrid>
  )
}
const press = (key: string) => fireEvent.keyDown(document.activeElement!, { key })
const firstRow = () => document.querySelector('.avalon-row-viewport')!.getAttribute('data-first-row')
function frame(time: number) {
  act(() => {
    const pending = frames
    frames = []
    pending.forEach((callback) => callback(time))
  })
}
beforeEach(() => {
  frames = []
  clearViewState('fixture:rows')
  vi.stubGlobal('requestAnimationFrame', (frame: FrameRequestCallback) => {
    frames.push(frame)
    return frames.length
  })
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      disconnect() {}
    },
  )
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(600)
  vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(900)
})
afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

it('moves within two rows before sliding one row and focuses the destination immediately', () => {
  render(<Harness />)
  act(() => screen.getByRole('button', { name: 'Game 1' }).focus())
  const overlap = document.querySelector('[data-row-id="7:8:9:10:11:12"]')
  press('ArrowDown')
  expect(document.activeElement?.textContent).toBe('Game 7')
  expect(firstRow()).toBe('0')
  expect(document.querySelector('[data-animating]')).toBeNull()
  press('ArrowDown')
  expect(document.activeElement?.textContent).toBe('Game 13')
  expect(firstRow()).toBe('1')
  expect(document.querySelector('[data-animating]')).not.toBeNull()
  expect(document.querySelector('[data-row-id="7:8:9:10:11:12"]')).toBe(overlap)
  expect(document.querySelector('[data-row-id="1:2:3:4:5:6"]')?.getAttribute('inert')).toBe('')
  frame(1000)
  frame(1110)
  expect((overlap as HTMLElement).style.transform).not.toBe('translateY(0%)')
  frame(1220)
  expect(document.querySelectorAll('.avalon-retained-row')).toHaveLength(4)
  for (let index = 0; index < 4; index++) {
    press('ArrowDown')
    frame(2000 + index * 300)
    frame(2220 + index * 300)
    expect(document.querySelectorAll('.avalon-retained-row').length).toBeLessThanOrEqual(4)
  }
  expect(overlap?.isConnected).toBe(false)
  for (const row of document.querySelectorAll<HTMLElement>('[data-row-active="false"]')) {
    expect(row.hasAttribute('inert')).toBe(true)
    expect(row.style.pointerEvents).toBe('none')
  }
})
it('routes the mouse wheel through the same row window', () => {
  render(<Harness />)
  act(() => screen.getByRole('button', { name: 'Game 1' }).focus())
  const grid = document.querySelector('.avalon-fullscreen-grid')!
  fireEvent.wheel(grid, { deltaY: 120 })
  fireEvent.wheel(grid, { deltaY: 120 })
  expect(firstRow()).toBe('1')
  expect(document.activeElement?.textContent).toBe('Game 13')
  expect(document.querySelector('[data-animating]')).not.toBeNull()
})
it('reduces motion and returns at the selected game without travel', () => {
  const view = render(<Harness reduced />)
  act(() => screen.getByRole('button', { name: 'Game 1' }).focus())
  press('ArrowDown')
  press('ArrowDown')
  expect(firstRow()).toBe('1')
  expect(document.querySelector('[data-animating]')).toBeNull()
  view.unmount()
  render(<Harness />)
  expect(firstRow()).toBe('1')
  expect(document.querySelector('[data-animating]')).toBeNull()
  expect(document.querySelector('[data-selected="true"]')?.textContent).toBe('Game 13')
})
it('narrowing during travel releases old rows and focuses a valid remaining game', () => {
  const view = render(<Harness />)
  act(() => screen.getByRole('button', { name: 'Game 1' }).focus())
  press('ArrowDown')
  press('ArrowDown')
  const outgoing = document.querySelector('[data-row-id="1:2:3:4:5:6"]')!
  view.rerender(<Harness data={[games[79]]} />)
  expect(firstRow()).toBe('0')
  expect(document.querySelector('[data-animating]')).toBeNull()
  expect(outgoing.isConnected).toBe(false)
  expect(document.querySelectorAll('.avalon-retained-row')).toHaveLength(1)
  expect(document.activeElement?.textContent).toBe('Game 80')
})
it('uses expanded content for immediate navigation without a stale queued rebuild', () => {
  const view = render(<Harness data={games.slice(0, 1)} />)
  act(() => screen.getByRole('button', { name: 'Game 1' }).focus())
  view.rerender(<Harness />)
  press('ArrowDown')
  press('ArrowDown')
  expect(firstRow()).toBe('1')
  expect(document.activeElement?.textContent).toBe('Game 13')
})
it('does not steal search focus when a filter selects the nearest remaining game', () => {
  const view = render(
    <>
      <input aria-label="Query" />
      <Harness />
    </>,
  )
  act(() => screen.getByRole('textbox').focus())
  view.rerender(
    <>
      <input aria-label="Query" />
      <Harness data={[games[79]]} />
    </>,
  )
  expect(document.activeElement).toBe(screen.getByRole('textbox'))
  expect(document.querySelector('[data-selected="true"]')?.textContent).toBe('Game 80')
})

it('retains an independent selected cover and two-row position for every collection and saved list', () => {
  const view = render(<Harness collection="all" reduced />)
  act(() => screen.getByRole('button', { name: 'Game 1' }).focus())
  press('ArrowRight')
  press('ArrowDown')
  press('ArrowDown')
  expect(document.activeElement?.textContent).toBe('Game 14')
  expect(firstRow()).toBe('1')
  view.rerender(<Harness collection="installed" reduced />)
  expect(document.activeElement?.textContent).toBe('Game 1')
  press('ArrowDown')
  expect(document.activeElement?.textContent).toBe('Game 7')
  view.rerender(<Harness collection="list:10" data={games.slice(20)} reduced />)
  expect(document.activeElement?.textContent).toBe('Game 21')
  press('ArrowRight')
  view.rerender(<Harness collection="all" reduced />)
  expect(document.activeElement?.textContent).toBe('Game 14')
  expect(firstRow()).toBe('1')
  view.rerender(<Harness collection="installed" reduced />)
  expect(document.activeElement?.textContent).toBe('Game 7')
  view.rerender(<Harness collection="list:10" data={games.slice(20)} reduced />)
  expect(document.activeElement?.textContent).toBe('Game 22')
})

it('keeps the newly selected collection viewport and covers attached after queued frames settle', () => {
  const view = render(<Harness collection="all" />)
  act(() => screen.getByRole('button', { name: 'Game 1' }).focus())
  press('ArrowDown')
  press('ArrowDown')
  for (const [index, collection] of ['installed', 'never_played', 'stale_but_patched', 'all'].entries()) {
    view.rerender(<Harness collection={collection} data={games.slice(index * 10)} />)
    const viewport = document.querySelector('.avalon-row-viewport')
    const covers = [...viewport!.querySelectorAll('[data-row-active="true"] button')]
    expect(covers.length).toBeGreaterThan(0)
    frame(1000 + index * 500)
    frame(1220 + index * 500)
    expect(document.querySelector('.avalon-row-viewport')).toBe(viewport)
    expect(covers.every((cover) => cover.isConnected)).toBe(true)
  }
})

it('resets an emptied collection without losing another collection position', () => {
  const view = render(<Harness collection="all" reduced />)
  act(() => screen.getByRole('button', { name: 'Game 1' }).focus())
  press('ArrowDown')
  press('ArrowDown')
  view.rerender(<Harness collection="installed" reduced />)
  press('ArrowRight')
  view.rerender(<Harness collection="installed" data={[]} reduced />)
  expect(document.querySelector('[data-selected-id]')).toBeNull()
  view.rerender(<Harness collection="all" reduced />)
  expect(document.querySelector('.avalon-fullscreen-grid')?.getAttribute('data-selected-id')).toBe('13')
  expect(firstRow()).toBe('1')
  view.rerender(<Harness collection="installed" reduced />)
  expect(document.querySelector('.avalon-fullscreen-grid')?.getAttribute('data-selected-id')).toBe('1')
})
