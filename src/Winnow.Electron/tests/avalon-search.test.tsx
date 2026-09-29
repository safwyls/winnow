// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { AvalonSearch, AVALON_SEARCH_STATE, searchGames } from '../src/renderer/themes/avalon-search'
import { DEFAULT_PROFILE, validateThemeDefinition, type ThemeContext } from '../src/shared/theme'
import type { LibraryGame } from '../src/renderer/api/types'
import { clearViewState } from '../src/renderer/viewState'
import { useController } from '../src/renderer/controller'
import { navigatePosition, returnFromSearch } from '../src/renderer/search-navigation'

vi.mock('../src/renderer/themes/avalon', () => ({
  AvalonCover: ({
    game,
    selected,
    context,
    onFocus,
    onKeyDown,
  }: {
    game: LibraryGame
    selected: boolean
    context: ThemeContext
    onFocus(): void
    onKeyDown(event: React.KeyboardEvent<HTMLButtonElement>): void
  }) => (
    <button
      data-avalon-game={game.workId}
      data-selected={selected}
      onFocus={onFocus}
      onKeyDown={onKeyDown}
      onClick={() => context.openGame(game.workId)}
    >
      {game.title}
    </button>
  ),
}))
vi.mock('../src/renderer/features/parity-library-projection', () => ({
  useLibraryProjection: (games: LibraryGame[]) => ({ games, marks: new Map() }),
}))
const games = Array.from({ length: 90 }, (_, index) => ({
  workId: index + 1,
  title: `Game ${String(index + 1).padStart(3, '0')}`,
  bucket: 'never_played',
  entries: [],
  playtimeMinutes: 0,
})) as LibraryGame[]
const frames = new Map<number, FrameRequestCallback>()
let frameId = 0,
  time = 0
const pressed = Array<boolean>(17).fill(false)
let resize: ResizeObserverCallback
const context = (extra: Partial<ThemeContext> = {}): ThemeContext => ({
  mode: 'fullscreen',
  page: 'search',
  selectedWorkId: null,
  games,
  feed: undefined,
  loading: false,
  profile: structuredClone(DEFAULT_PROFILE),
  children: null,
  renderScreen: () => null,
  components: {} as ThemeContext['components'],
  actions: { launch: vi.fn() },
  setPage: vi.fn(),
  openGame: vi.fn(),
  closeSearch: vi.fn(),
  toggleFullscreen: vi.fn(),
  editText: vi.fn(),
  ...extra,
})
function frame(at?: number) {
  act(() => {
    time = at ?? time + 300
    const callbacks = [...frames.values()]
    frames.clear()
    callbacks.forEach((callback) => callback(time))
  })
}
function press(key: string) {
  fireEvent.keyDown(document.activeElement!, { key })
}
function focus(name: string) {
  act(() => screen.getByRole('button', { name }).focus())
}
function query(value: string) {
  const input = screen.getByRole('searchbox', { name: 'Search games' })
  act(() => input.focus())
  fireEvent.change(input, { target: { value } })
}
function controller(button: number) {
  pressed.fill(false)
  frame()
  pressed[button] = true
  frame()
  pressed.fill(false)
  frame()
}
function ControlledSearch({ value }: { value: ThemeContext }) {
  useController({
    enabled: true,
    menu: vi.fn(),
    search: vi.fn(),
    switchPage: vi.fn(),
    keyboard: vi.fn(),
    play: vi.fn(),
  })
  return <AvalonSearch {...value} />
}
beforeEach(() => {
  for (const key of ['query', 'rows', 'selected', 'in-results'])
    clearViewState(`${AVALON_SEARCH_STATE}:${key}`)
  frames.clear()
  pressed.fill(false)
  frameId = 0
  time = 0
  vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
    frames.set(++frameId, callback)
    return frameId
  })
  vi.stubGlobal('cancelAnimationFrame', (id: number) => frames.delete(id))
  vi.stubGlobal(
    'ResizeObserver',
    class {
      constructor(callback: ResizeObserverCallback) {
        resize = callback
      }
      observe() {}
      disconnect() {}
    },
  )
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(600)
  vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(900)
  vi.spyOn(document, 'hasFocus').mockReturnValue(true)
  vi.stubGlobal('matchMedia', () => ({ matches: false, addEventListener() {}, removeEventListener() {} }))
  Object.defineProperty(navigator, 'getGamepads', {
    configurable: true,
    value: vi.fn(() => [
      {
        index: 0,
        get buttons() {
          return pressed.map((pressed) => ({ pressed }))
        },
        axes: [0, 0, 0, 0],
      } as unknown as Gamepad,
    ]),
  })
})
afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  Reflect.deleteProperty(navigator, 'getGamepads')
})

it('searches the full projection by title only with a trimmed culture-aware query and stable order', () => {
  const source = [
    { ...games[1], title: 'Outer World' },
    { ...games[0], title: 'WORLD Builder' },
    { ...games[2], title: 'Other', entries: [{ title: 'World' }] },
  ] as LibraryGame[]
  expect(searchGames(source, '  world ')).toEqual(source.slice(0, 2))
  expect(searchGames(source, ' ')).toEqual(source)
  expect(searchGames(source, 'Steam')).toEqual([])
})
it('initially focuses Enter search and opens the keyboard for its named query field', () => {
  const value = context()
  render(<AvalonSearch {...value} />)
  frame()
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Enter search' }))
  fireEvent.click(document.activeElement!)
  expect(value.editText).toHaveBeenCalledWith(screen.getByRole('searchbox', { name: 'Search games' }))
  fireEvent.click(screen.getByRole('button', { name: 'B · Back' }))
  expect(value.closeSearch).toHaveBeenCalledOnce()
})
it('does not steal query focus when typing starts before the initial focus frame', () => {
  render(<AvalonSearch {...context()} />)
  query('Game 090')
  frame()
  expect(document.activeElement).toBe(screen.getByRole('searchbox'))
  expect((screen.getByRole('searchbox') as HTMLInputElement).value).toBe('Game 090')
  expect(screen.getByText('1 games')).toBeTruthy()
})
it('returns from Go to results to the first row in the same column', () => {
  render(<AvalonSearch {...context()} />)
  frame()
  focus('Game 003')
  press('ArrowUp')
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Go to results' }))
  press('ArrowDown')
  expect(document.activeElement?.textContent).toBe('Game 003')
  expect(document.querySelector('[data-animating]')).toBeNull()
})
it('Go to results chooses the first currently visible result while Down restores selection', () => {
  render(<AvalonSearch {...context()} />)
  frame()
  focus('Game 003')
  press('PageDown')
  frame()
  frame()
  expect(document.activeElement?.textContent).toBe('Game 013')
  focus('Go to results')
  press('ArrowDown')
  expect(document.activeElement?.textContent).toBe('Game 013')
  fireEvent.click(screen.getByRole('button', { name: 'Go to results' }))
  expect(document.activeElement?.textContent).toBe('Game 006')
})
it('Page keys move two rows from the header and update the visible row range', () => {
  render(<AvalonSearch {...context()} />)
  frame()
  press('PageDown')
  expect(document.activeElement?.textContent).toBe('Game 011')
  expect(screen.getByText('LT / RT · Rows 2–3 / 18')).toBeTruthy()
  press('PageUp')
  expect(document.activeElement?.textContent).toBe('Game 001')
  expect(screen.getByText('LT / RT · Rows 1–2 / 18')).toBeTruthy()
})
it('narrows during travel without stealing query focus and releases old rows', () => {
  render(<AvalonSearch {...context()} />)
  frame()
  focus('Game 001')
  press('ArrowDown')
  press('ArrowDown')
  const row = document.querySelector('[data-row-id="1:2:3:4:5"]')!
  expect(document.querySelector('[data-animating]')).not.toBeNull()
  query('Game 080')
  expect(row.isConnected).toBe(false)
  expect(document.querySelectorAll('.avalon-retained-row')).toHaveLength(1)
  expect(document.querySelector('[data-animating]')).toBeNull()
  expect(document.activeElement).toBe(screen.getByRole('searchbox'))
  fireEvent.click(screen.getByRole('button', { name: 'Go to results' }))
  expect(document.activeElement?.textContent).toBe('Game 080')
})
it('retains overlapping Search rows through controlled travel and evicts rows after repeated navigation', () => {
  render(<AvalonSearch {...context()} />)
  frame()
  focus('Game 001')
  const overlap = document.querySelector('[data-row-id="6:7:8:9:10"]')!
  const incoming = document.querySelector('[data-row-id="11:12:13:14:15"]')!
  const first = document.querySelector('[data-row-id="1:2:3:4:5"]')!
  press('ArrowDown')
  expect(document.querySelector('.avalon-row-viewport')?.getAttribute('data-first-row')).toBe('0')
  expect(document.querySelector('[data-animating]')).toBeNull()
  press('ArrowDown')
  expect(document.activeElement?.textContent).toBe('Game 011')
  expect(document.querySelector('[data-row-id="6:7:8:9:10"]')).toBe(overlap)
  expect(document.querySelector('[data-row-id="11:12:13:14:15"]')).toBe(incoming)
  expect(first.hasAttribute('inert')).toBe(true)
  frame(1000)
  frame(1110)
  expect((overlap as HTMLElement).style.transform).not.toBe('translateY(0%)')
  frame(1220)
  for (let index = 0; index < 4; index++) {
    press('ArrowDown')
    frame()
    frame()
    expect(document.querySelectorAll('.avalon-retained-row').length).toBeLessThanOrEqual(4)
  }
  expect(overlap.isConnected).toBe(false)
  for (const row of document.querySelectorAll('[data-row-active="false"]'))
    expect(row.hasAttribute('inert')).toBe(true)
})
it('a programmatic query change during travel focuses the remaining result when results own focus', () => {
  render(<AvalonSearch {...context()} />)
  frame()
  focus('Game 001')
  press('ArrowDown')
  press('ArrowDown')
  const old = document.querySelector('[data-row-id="1:2:3:4:5"]')!
  fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'Game 080' } })
  expect(document.activeElement?.textContent).toBe('Game 080')
  expect(old.isConnected).toBe(false)
  expect(document.querySelector('[data-animating]')).toBeNull()
  expect(document.querySelector('.avalon-row-viewport')?.getAttribute('data-first-row')).toBe('0')
})
it('empty results disable the destination and clearing starts with a valid first game', () => {
  render(<AvalonSearch {...context()} />)
  frame()
  focus('Game 003')
  press('PageDown')
  query('Nothing here')
  expect(screen.getByText('No games match. Try a different title.')).toBeTruthy()
  expect((screen.getByRole('button', { name: 'Go to results' }) as HTMLButtonElement).disabled).toBe(true)
  expect(screen.queryByText(/LT \/ RT/)).toBeNull()
  query('')
  fireEvent.click(screen.getByRole('button', { name: 'Go to results' }))
  expect(document.activeElement?.textContent).toBe('Game 001')
})
it('retains query, selected game and row window when returning from Details without a slide', () => {
  const value = context(),
    view = render(<AvalonSearch {...value} />)
  frame()
  query('Game 0')
  focus('Game 003')
  press('PageDown')
  frame()
  frame()
  fireEvent.click(document.activeElement!)
  expect(value.openGame).toHaveBeenCalledWith(13)
  view.unmount()
  render(<AvalonSearch {...value} />)
  frame()
  expect((screen.getByRole('searchbox') as HTMLInputElement).value).toBe('Game 0')
  expect(document.activeElement?.textContent).toBe('Game 013')
  expect(document.querySelector('[data-animating]')).toBeNull()
  expect(document.querySelector('.avalon-row-viewport')?.getAttribute('data-first-row')).toBe('1')
})
it('wheel movement shares the same selection policy and reduced motion snaps', () => {
  const value = context()
  value.profile.appearance.reducedMotion = true
  render(<AvalonSearch {...value} />)
  frame()
  const grid = document.querySelector('.avalon-fullscreen-grid')!
  fireEvent.wheel(grid, { deltaY: 120 })
  fireEvent.wheel(grid, { deltaY: 120 })
  expect(document.activeElement?.textContent).toBe('Game 011')
  expect(document.querySelector('[data-animating]')).toBeNull()
})
it('a resize preserves selected identity and text entry focus', () => {
  render(<AvalonSearch {...context()} />)
  frame()
  focus('Game 003')
  press('PageDown')
  frame()
  vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(1440)
  act(() => resize([], {} as ResizeObserver))
  expect(document.activeElement?.textContent).toBe('Game 013')
  act(() => screen.getByRole('searchbox').focus())
  vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(900)
  act(() => resize([], {} as ResizeObserver))
  expect(document.activeElement).toBe(screen.getByRole('searchbox'))
  expect(document.querySelector('.avalon-fullscreen-grid')?.getAttribute('data-selected-id')).toBe('13')
})
it('controller triggers page results from any region, Y edits the query, and modals contain both', () => {
  const value = context()
  render(<ControlledSearch value={value} />)
  frame()
  controller(7)
  expect(document.activeElement?.textContent).toBe('Game 011')
  controller(6)
  expect(document.activeElement?.textContent).toBe('Game 001')
  controller(3)
  expect(value.editText).toHaveBeenCalledOnce()
  const dialog = document.createElement('div')
  dialog.setAttribute('role', 'dialog')
  document.body.append(dialog)
  controller(7)
  controller(3)
  expect(value.editText).toHaveBeenCalledOnce()
  expect(document.querySelector('.avalon-fullscreen-grid')?.getAttribute('data-selected-id')).toBe('1')
  dialog.remove()
})
it('preserves the exact page and game that opened Search through a different Details visit', () => {
  const origin = { page: 'details', previous: 'library', workId: 4 } as const
  const search = navigatePosition(origin, 'search')
  const detail = { ...search, page: 'details', previous: 'search', workId: 19 } as const
  const returned = navigatePosition(detail, 'search')
  expect(returnFromSearch(returned)).toEqual(origin)
  expect(navigatePosition(returned, 'search')).toBe(returned)
})
it('accepts an optional Search component and rejects a malformed override without changing API 1', () => {
  expect(
    validateThemeDefinition({ apiVersion: 1, id: 'example', name: 'Example', Search: () => null }).Search,
  ).toBeTypeOf('function')
  expect(validateThemeDefinition({ apiVersion: 1, id: 'example', name: 'Example' }).Search).toBeUndefined()
  expect(() =>
    validateThemeDefinition({ apiVersion: 1, id: 'example', name: 'Example', Search: 'code' }),
  ).toThrow('Search must be a React component function.')
})
