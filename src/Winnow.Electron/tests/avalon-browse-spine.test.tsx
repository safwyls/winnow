// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AvalonBrowseSpine } from '../src/renderer/themes/avalon-browse-spine'
import {
  alphabetSection,
  browseSections,
  nearestSection,
  spineHalo,
  spineLocation,
  spinePointerRow,
  spineWave,
} from '../src/renderer/themes/avalon-browse-policy'

beforeEach(() =>
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      disconnect() {}
    },
  ),
)
afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})
const games = ['Cobalt', 'Crown', 'Port', 'Tunic'].map((title) => ({ title }))

describe('desktop Library browse spine', () => {
  it.each([
    ['Élan', 'E'],
    ['  Zelda', 'Z'],
    ['123 Robots', '#'],
    ['™Game', '#'],
    ['e\u0301lan', 'E'],
    ['\u0301Zelda', 'Z'],
    ['東京', '#'],
    ['Łódź', '#'],
    ['ßeta', '#'],
    ['', '#'],
    ['   ', '#'],
    [null, '#'],
    [undefined, '#'],
  ])('folds source diacritics and groups leading nonletters: %s', (title, expected) => {
    expect(alphabetSection(title)).toBe(expected)
  })

  it('keeps all fixed stops, their first visible title and reversed descending order', () => {
    const sections = browseSections(games, 'title')
    expect(sections.map((section) => section.label).join('')).toBe('#ABCDEFGHIJKLMNOPQRSTUVWXYZ')
    expect(sections.filter((section) => section.index !== undefined)).toEqual([
      { label: 'C', index: 0 },
      { label: 'P', index: 2 },
      { label: 'T', index: 3 },
    ])
    const reversed = browseSections([...games].reverse(), 'title-desc')
    expect(reversed.map((section) => section.label).join('')).toBe('ZYXWVUTSRQPONMLKJIHGFEDCBA#')
    expect(reversed.filter((section) => section.index !== undefined)).toEqual([
      { label: 'T', index: 0 },
      { label: 'P', index: 1 },
      { label: 'C', index: 2 },
    ])
  })

  it('scrubs an unavailable M to P and resolves an equal distance toward the earlier stop', () => {
    expect(nearestSection(browseSections(games, 'title'), 13)).toBe(2)
    expect(nearestSection(browseSections([{ title: 'Bravo' }, { title: 'Delta' }], 'title'), 3)).toBe(0)
    expect(nearestSection(browseSections([{ title: 'Delta' }, { title: 'Bravo' }], 'title-desc'), 23)).toBe(0)
    expect(nearestSection(browseSections([], 'title'), 13)).toBeUndefined()
  })

  it('maps sub-row pointer positions without snapping and clamps outside the rail', () => {
    expect(spinePointerRow(-20, 540)).toBe(0)
    expect(spinePointerRow(550, 540)).toBe(26)
    expect(spinePointerRow(270, 540)).toBe(13)
    expect(spinePointerRow(417, 540)).toBeCloseTo(20.35)
    expect(spineWave(20, 20)).toBe(-13)
    expect(spineWave(20, 20.35)).toBeGreaterThan(-12.9)
    expect(spineWave(20, 20.35)).toBeLessThan(-12.5)
    expect(spineWave(16, 20)).toBe(0)
    expect(spineWave(20, null)).toBe(0)
  })

  it('interpolates the viewport halo in the displayed order and returns the wave to rest', () => {
    expect(spineLocation(games, 'title', 0)).toBe(3)
    expect(spineLocation(games, 'title', 0.5)).toBe(9.5)
    expect(spineLocation(games, 'title', 1)).toBe(20)
    expect(spineLocation([...games].reverse(), 'title-desc', 0)).toBe(6)
    expect(spineLocation(games, 'time', 0.5)).toBe(13)
    expect([13, 14, 15, 16, 17].map((stop) => spineHalo(stop, 13))).toEqual([4, 3, 2, 1, 0])
  })

  it('exposes named enabled stops, keeps unavailable letters disabled and activates by keyboard', () => {
    const jump = vi.fn(),
      scroll = { current: document.createElement('div') }
    const view = render(<AvalonBrowseSpine games={games} sort="title" scroll={scroll} jump={jump} />)
    const rail = screen.getByRole('group', { name: 'Browse by letter' })
    expect(within(rail).getAllByRole('button')).toHaveLength(27)
    expect((screen.getByRole('button', { name: 'Jump to #' }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'Jump to A' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Jump to T' }), { detail: 0 })
    expect(jump).toHaveBeenCalledExactlyOnceWith(3)
    view.rerender(
      <AvalonBrowseSpine games={[...games].reverse()} sort="title-desc" scroll={scroll} jump={jump} />,
    )
    expect(within(rail).getAllByRole('button')[0].textContent).toBe('Z')
    fireEvent.click(screen.getByRole('button', { name: 'Jump to C' }), { detail: 0 })
    expect(jump).toHaveBeenLastCalledWith(2)
    view.rerender(<AvalonBrowseSpine games={games} sort="time" scroll={scroll} jump={jump} />)
    expect(screen.queryAllByRole('button')).toHaveLength(0)
    expect(screen.getByRole('group', { name: 'Browse this order' }).children).toHaveLength(27)
    view.rerender(<AvalonBrowseSpine games={[]} sort="title" scroll={scroll} jump={jump} />)
    expect(screen.queryByRole('group')).toBeNull()
  })
})
