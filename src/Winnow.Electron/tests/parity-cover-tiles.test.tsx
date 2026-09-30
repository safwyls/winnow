// @vitest-environment jsdom
import { StrictMode } from 'react'
import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { AvalonCover } from '../src/renderer/themes/avalon'
import { AvalonCoverWorkspace, type AvalonCoverProps } from '../src/renderer/themes/avalon-desktop-cover'
import type { ThemeContext } from '../src/shared/theme'
import type { LibraryGame, Workspace } from '../src/renderer/api/types'
import { coverGame, coverProfile, coverWorkspace, FixtureCoverArt } from './cover-fixtures'

beforeEach(() => {
  vi.stubGlobal('PointerEvent', MouseEvent)
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: {
      request: vi.fn().mockResolvedValue({ ok: true, status: 200, data: { ratings: [] } }),
    },
  })
})
afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

function setup(
  options: {
    game?: LibraryGame
    workspace?: Workspace
    mode?: 'desktop' | 'fullscreen'
    strict?: boolean
  } = {},
) {
  const launch = vi.fn().mockResolvedValue(undefined)
  const open = vi.fn()
  const context = {
    mode: options.mode ?? 'desktop',
    profile: coverProfile(),
    components: { Artwork: FixtureCoverArt },
    actions: { launch },
    openGame: open,
  } as unknown as ThemeContext
  const game = options.game ?? coverGame()
  const workspace = options.workspace ?? coverWorkspace(game)
  let props: AvalonCoverProps = { context, game }
  const draw = () => (
    <AvalonCoverWorkspace.Provider value={workspace}>
      <button>Outside</button>
      <AvalonCover {...props} />
    </AvalonCoverWorkspace.Provider>
  )
  const view = render(options.strict ? <StrictMode>{draw()}</StrictMode> : draw())
  return {
    launch,
    open,
    game,
    context,
    view,
    update(patch: Partial<AvalonCoverProps>) {
      props = { ...props, ...patch }
      view.rerender(options.strict ? <StrictMode>{draw()}</StrictMode> : draw())
    },
    tile: () => view.container.querySelector<HTMLElement>('.avalon-desktop-cover')!,
    cover: () => screen.getByRole('button', { name: /^View / }),
  }
}
function reveal(f: ReturnType<typeof setup>) {
  fireEvent.mouseMove(f.tile())
}

for (const mode of ['desktop', 'fullscreen'] as const) {
  it.each([1, 3, 1234])(`announces %i correlated updates once across store copies in ${mode}`, (count) => {
    const game = coverGame(),
      workspace = coverWorkspace(game)
    workspace.buckets = game.entries.map((entry) => ({
      releaseId: entry.releaseId,
      resolvedWorkId: game.workId,
      game: { unreadUpdateCount: count },
    }))
    const f = setup({ mode, game, workspace })
    expect(f.cover().getAttribute('aria-label')).toBe(
      `View ${game.title}, patched since you played: ${count.toLocaleString()} ${count === 1 ? 'update' : 'updates'}. Owned on Steam, GOG, Epic`,
    )
    expect(f.cover().querySelector('.avalon-unread')).not.toBeNull()
  })
  it(`states a badge without inventing a zero count in ${mode}`, () => {
    const f = setup({ mode })
    expect(f.cover().getAttribute('aria-label')).toContain('patched since you played')
    expect(f.cover().getAttribute('aria-label')).not.toMatch(/\d+ updates?/)
    expect(f.cover().querySelector('.avalon-unread')).not.toBeNull()
  })
  it(`keeps an unbadged unplayed game free of update claims in ${mode}`, () => {
    const game = coverGame(1, false, false, true),
      workspace = coverWorkspace(game)
    game.title = 'Tunic'
    workspace.buckets = [{ resolvedWorkId: 1, releaseId: 10, game: { unreadUpdateCount: 3 } }]
    const f = setup({ mode, game, workspace })
    expect(f.cover().getAttribute('aria-label')).toBe('View Tunic')
    expect(f.cover().querySelector('.avalon-unread')).toBeNull()
  })
}

it('keeps a stationary mouse-enter inert until fresh movement reveals the controls', () => {
  const f = setup()
  fireEvent.mouseEnter(f.tile())
  expect(f.tile().hasAttribute('data-revealed')).toBe(false)
  reveal(f)
  expect(screen.getByRole('button', { name: 'Play' })).toBeDefined()
})

it('keeps single cover activation, Details and the reachable primary action independent', async () => {
  const f = setup()
  expect(screen.queryByRole('button', { name: 'Play' })).toBeNull()
  expect(screen.queryByRole('button', { name: 'Details' })).toBeNull()
  reveal(f)
  const play = screen.getByRole('button', { name: 'Play' })
  expect(play.title).toBe('Launch through Steam')
  expect(play.querySelector('[data-glyph="play"]')).not.toBeNull()
  await act(async () => fireEvent.click(play))
  expect(f.launch).toHaveBeenCalledExactlyOnceWith(10)
  expect(f.open).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: 'Details' }))
  expect(f.open).toHaveBeenCalledExactlyOnceWith(1)
  fireEvent.click(f.cover())
  expect(f.open).toHaveBeenCalledTimes(2)
  expect(f.launch).toHaveBeenCalledTimes(1)
})
it.each([true, false])('keeps the off-disk Install label and glyph with single store %s', (singleStore) => {
  const f = setup({ game: coverGame(1, false, false, singleStore) })
  reveal(f)
  const install = screen.getByRole('button', { name: 'Install' })
  expect(install.title).toBe('Install through Steam')
  expect(install.querySelector('[data-glyph="install"]')).not.toBeNull()
  expect(screen.getByText('never opened')).toBeDefined()
  expect(screen.queryByRole('button', { name: 'Play' })).toBeNull()
})
it('chooses the reachable installed copy, while an unreachable library still has Details', async () => {
  const game = coverGame()
  game.entries[0]!.installed = false
  game.entries[1]!.installed = true
  const f = setup({ game })
  reveal(f)
  await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Play' })))
  expect(f.launch).toHaveBeenCalledExactlyOnceWith(11)
  cleanup()
  const inaccessible = setup({ workspace: { ...coverWorkspace(game), externalIds: [] } })
  reveal(inaccessible)
  expect(screen.queryByRole('button', { name: /^(Play|Install)$/ })).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Details' }))
  expect(inaccessible.open).toHaveBeenCalledExactlyOnceWith(1)
})
it.each([false, true])(
  'refuses duplicate launches while pending and enables actionable retry in strict mode %s',
  async (strict) => {
    const f = setup({ strict })
    let reject!: (reason: Error) => void
    f.launch.mockImplementationOnce(
      () =>
        new Promise((_, failure) => {
          reject = failure
        }),
    )
    reveal(f)
    const play = screen.getByRole('button', { name: 'Play' })
    fireEvent.click(play)
    fireEvent.click(play)
    expect(f.launch).toHaveBeenCalledTimes(1)
    expect(play.getAttribute('aria-disabled')).toBe('true')
    await act(async () => reject(Error('private filesystem or account diagnostic')))
    expect(screen.getByRole('alert').textContent).toBe('Play could not finish. Try again.')
    expect(play.hasAttribute('aria-disabled')).toBe(false)
    await act(async () => fireEvent.click(play))
    expect(f.launch).toHaveBeenCalledTimes(2)
    expect(screen.queryByRole('alert')).toBeNull()
  },
)
it('selection retains its ring state without keeping the action dock open', () => {
  const f = setup()
  f.update({ selected: true })
  expect(f.tile().dataset.selected).toBe('true')
  expect(f.tile().hasAttribute('data-revealed')).toBe(false)
  reveal(f)
  expect(f.tile().dataset.revealed).toBe('true')
  fireEvent.mouseLeave(f.tile())
  expect(f.tile().dataset.selected).toBe('true')
  expect(f.tile().hasAttribute('data-revealed')).toBe(false)
})
it('keyboard action focus reveals controls until focus leaves, while a pointer press does not pin them', () => {
  const f = setup()
  const details = f.tile().querySelector<HTMLButtonElement>('.avalon-tile-details')!
  act(() => details.focus())
  expect(f.tile().dataset.revealed).toBe('true')
  fireEvent.mouseLeave(f.tile())
  expect(f.tile().dataset.revealed).toBe('true')
  act(() => screen.getByRole('button', { name: 'Outside' }).focus())
  expect(f.tile().hasAttribute('data-revealed')).toBe(false)
  reveal(f)
  fireEvent.pointerDown(details)
  act(() => details.focus())
  fireEvent.mouseLeave(f.tile())
  expect(f.tile().hasAttribute('data-revealed')).toBe(false)
})
it.each(['hover', 'focus', 'pending'] as const)(
  'recycling releases outgoing %s state and late work cannot affect the incoming tile',
  async (state) => {
    const f = setup()
    let reject!: (reason: Error) => void
    f.launch.mockImplementationOnce(
      () =>
        new Promise((_, failure) => {
          reject = failure
        }),
    )
    reveal(f)
    const outgoing = f.tile()
    const details = screen.getByRole('button', { name: 'Details' })
    if (state === 'focus') act(() => details.focus())
    if (state === 'pending') fireEvent.click(screen.getByRole('button', { name: 'Play' }))
    f.update({ game: coverGame(2) })
    expect(f.tile()).not.toBe(outgoing)
    expect(f.tile().hasAttribute('data-revealed')).toBe(false)
    expect(document.activeElement).not.toBe(details)
    expect(screen.queryByRole('button', { name: 'Details' })).toBeNull()
    if (state === 'pending') await act(async () => reject(Error('late outgoing failure')))
    expect(screen.queryByRole('alert')).toBeNull()
    fireEvent.mouseMove(f.tile())
    expect(screen.getByRole('button', { name: 'Details' })).toBeDefined()
    fireEvent.click(screen.getByRole('button', { name: 'Details' }))
    expect(f.open).toHaveBeenCalledExactlyOnceWith(2)
  },
)
it('retains the artwork DOM on an unchanged identity and exposes compact playtime, idle, stores and unread state', () => {
  const f = setup()
  const artwork = f.cover().querySelector('.artwork')
  f.update({ selected: true, expansion: { count: 2, text: '2 expansions', unplayed: true } })
  expect(f.cover().querySelector('.artwork')).toBe(artwork)
  expect(f.cover().getAttribute('aria-label')).toContain(
    'patched since you played. Owned on Steam, GOG, Epic. 2 expansions',
  )
  expect(f.tile().querySelector('.avalon-unread')).not.toBeNull()
  reveal(f)
  expect(screen.getByText(/^12345h · idle 10y$/)).toBeDefined()
  expect(within(f.tile()).getByText('STEAM')).toBeDefined()
  expect(within(f.tile()).getByText('GOG')).toBeDefined()
  expect(within(f.tile()).getByText('EPIC')).toBeDefined()
})
it('preserves selection modifiers and context routing only on the cover', () => {
  const f = setup()
  const click = vi.fn(),
    menu = vi.fn()
  f.update({ onClick: click, onContextMenu: menu })
  fireEvent.click(f.cover(), { ctrlKey: true })
  expect(click.mock.calls[0]![0].ctrlKey).toBe(true)
  fireEvent.contextMenu(f.cover())
  expect(menu).toHaveBeenCalledOnce()
  reveal(f)
  fireEvent.click(screen.getByRole('button', { name: 'Details' }), { ctrlKey: true })
  expect(click).toHaveBeenCalledOnce()
  expect(f.open).toHaveBeenCalledExactlyOnceWith(1)
})
it('fullscreen retains its separate single-target cover and keyboard collection callback', () => {
  const f = setup({ mode: 'fullscreen' })
  const keyboard = vi.fn()
  f.update({ onKeyDown: keyboard })
  expect(f.tile()).toBeNull()
  expect(document.querySelectorAll('.avalon-tile-primary,.avalon-tile-details')).toHaveLength(0)
  fireEvent.keyDown(f.cover(), { key: 'ArrowRight' })
  expect(keyboard).toHaveBeenCalledOnce()
  fireEvent.click(f.cover())
  expect(f.open).toHaveBeenCalledExactlyOnceWith(1)
})
