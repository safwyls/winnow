// @vitest-environment jsdom
import { act, cleanup, render } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { AvalonRowViewport } from '../src/renderer/themes/avalon-row-viewport'

let frames: FrameRequestCallback[] = []
beforeEach(() => {
  frames = []
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
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(300)
  vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(800)
})
afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})
const rows = Array.from({ length: 1000 }, (_, index) => ({ id: `shelf-${index}`, title: `Game ${index}` }))
function frame(time: number) {
  act(() => {
    const pending = frames
    frames = []
    pending.forEach((callback) => callback(time))
  })
}
function tree(first: number, items = rows, reducedMotion = false) {
  return (
    <AvalonRowViewport rows={items} first={first} reducedMotion={reducedMotion}>
      {(row) => <button>{row.title}</button>}
    </AvalonRowViewport>
  )
}

it('retains shelf and cover nodes while translating, and disables outgoing interaction immediately', () => {
  const view = render(tree(0)),
    outgoing = view.container.querySelector('[data-row-id="shelf-0"]')!,
    incoming = view.container.querySelector('[data-row-id="shelf-1"]')!
  const cover = incoming.firstElementChild
  view.rerender(tree(1))
  expect(view.container.querySelector('[data-row-id="shelf-0"]')).toBe(outgoing)
  expect(view.container.querySelector('[data-row-id="shelf-1"]')).toBe(incoming)
  expect(incoming.firstElementChild).toBe(cover)
  expect(outgoing.getAttribute('inert')).toBe('')
  expect(outgoing.getAttribute('aria-hidden')).toBe('true')
  expect(incoming.hasAttribute('inert')).toBe(false)
  expect((outgoing as HTMLElement).style.pointerEvents).toBe('none')
  frame(1000)
  frame(1110)
  expect((incoming as HTMLElement).style.transform).not.toBe('translateY(0%)')
  expect((outgoing as HTMLElement).style.opacity).toBe('')
  frame(1220)
  expect((incoming as HTMLElement).style.transform).toBe('translateY(0%)')
  expect(view.container.querySelectorAll('.avalon-retained-row')).toHaveLength(3)
})
it('refreshes one row without replacing its neighbors and immediately exposes appended data', () => {
  const view = render(tree(0, rows.slice(0, 2)))
  const neighbor = view.container.querySelector('[data-row-id="shelf-1"]')
  view.rerender(tree(0, [{ ...rows[0], title: 'Updated game' }, rows[1]]))
  expect(view.getByRole('button').textContent).toBe('Updated game')
  expect(view.container.querySelector('[data-row-id="shelf-1"]')).toBe(neighbor)
  view.rerender(tree(3, rows.slice(0, 5)))
  expect(view.getByRole('button').textContent).toBe('Game 3')
  expect(view.container.querySelector('[data-first-row]')?.getAttribute('data-first-row')).toBe('3')
})
it('publishes one realized content refresh without notifying for offscreen rows or animation frames', () => {
  const renderRows = (first: number, items = rows) => (
    <AvalonRowViewport rows={items} first={first} reducedMotion={false} visible={2}>
      {(row) => <button>{row.title}</button>}
    </AvalonRowViewport>
  )
  const view = render(renderRows(0))
  const neighbor = view.container.querySelector('[data-row-id="shelf-1"]')
  const original = view.container.querySelector('[data-row-id="shelf-0"]')
  const observer = new MutationObserver(() => {})
  observer.observe(view.container, { subtree: true, childList: true, characterData: true })
  const refreshed = [{ ...rows[0], title: 'Updated game' }, ...rows.slice(1)]
  view.rerender(renderRows(0, refreshed))
  // React updates the retained text node rather than replacing the whole realized control.
  expect(observer.takeRecords()).toHaveLength(1)
  expect(original?.textContent).toBe('Updated game')
  expect(view.container.querySelector('[data-row-id="shelf-0"]')).toBe(original)
  expect(view.container.querySelector('[data-row-id="shelf-1"]')).toBe(neighbor)
  view.rerender(renderRows(0, refreshed))
  expect(observer.takeRecords()).toHaveLength(0)
  const unseen = refreshed.map((row, index) => (index === 500 ? { ...row, title: 'Offscreen update' } : row))
  view.rerender(renderRows(0, unseen))
  expect(observer.takeRecords()).toHaveLength(0)
  view.rerender(renderRows(1, unseen))
  expect(observer.takeRecords().length).toBeGreaterThan(0)
  frame(1000)
  frame(1110)
  expect(observer.takeRecords()).toHaveLength(0)
  expect(view.container.querySelector('[data-row-id="shelf-1"]')).toBe(neighbor)
  observer.disconnect()
})

it('retains arranged translations before the first frame and immediately after reversing', () => {
  const items = rows.slice(0, 8)
  const view = render(tree(0, items))
  const incoming = view.container.querySelector<HTMLElement>('[data-row-id="shelf-1"]')!
  const initial = incoming.style.transform
  view.rerender(tree(1, items))
  expect(incoming.style.transform).toBe(initial)
  frame(1000)
  expect(incoming.style.transform).toBe(initial)
  frame(1090)
  const beforeReverse = incoming.style.transform
  expect(beforeReverse).not.toBe(initial)
  view.rerender(tree(0, items))
  expect(incoming.style.transform).toBe(beforeReverse)
  expect(view.container.querySelector('[data-row-id="shelf-1"]')).toBe(incoming)
  frame(2000)
  expect(incoming.style.transform).toBe(beforeReverse)
})
it('bounds DOM realization on distant navigation and removes old rows on replacement and empty data', () => {
  const view = render(tree(0))
  view.rerender(tree(900))
  expect(view.container.querySelectorAll('.avalon-retained-row')).toHaveLength(3)
  expect(view.container.querySelector('[data-row-id="shelf-0"]')).toBeNull()
  expect(view.container.querySelector('[data-animating]')).toBeNull()
  view.rerender(tree(0, [{ id: 'replacement', title: 'New game' }]))
  expect(view.container.querySelectorAll('.avalon-retained-row')).toHaveLength(1)
  expect(view.getByRole('button').textContent).toBe('New game')
  view.rerender(tree(0, []))
  expect(view.container.querySelectorAll('.avalon-retained-row')).toHaveLength(0)
})
it('unmounts pending motion safely and reattaches at the selected shelf without travel', () => {
  const view = render(tree(0))
  view.rerender(tree(1))
  frame(1000)
  view.unmount()
  frame(1110)
  const second = render(tree(1))
  expect(second.container.querySelector('[data-animating]')).toBeNull()
  expect((second.container.querySelector('[data-row-active="true"]') as HTMLElement).style.transform).toBe(
    'translateY(0%)',
  )
})

it('keeps two visible rows interactive while retaining only bounded neighbors', () => {
  const tree = (first: number) => (
    <AvalonRowViewport rows={rows} first={first} reducedMotion={false} visible={2}>
      {(row) => <button>{row.title}</button>}
    </AvalonRowViewport>
  )
  const view = render(tree(0))
  const overlap = view.container.querySelector('[data-row-id="shelf-1"]')
  view.rerender(tree(1))
  expect(view.container.querySelector('[data-row-id="shelf-1"]')).toBe(overlap)
  expect(view.getAllByRole('button').map((button) => button.textContent)).toEqual(['Game 1', 'Game 2'])
  expect(view.container.querySelectorAll('.avalon-retained-row')).toHaveLength(4)
  for (const row of view.container.querySelectorAll<HTMLElement>('.avalon-retained-row'))
    expect(row.style.height).toBe('50%')
  frame(1000)
  frame(1220)
  expect((overlap as HTMLElement).style.transform).toBe('translateY(0%)')
})
