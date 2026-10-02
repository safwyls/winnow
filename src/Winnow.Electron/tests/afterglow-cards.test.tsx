// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { DEFAULT_PROFILE, type ThemeContext } from '../src/shared/theme'
import type { LibraryGame } from '../src/renderer/api/types'
import { AfterglowDiscover, AfterglowLibrary } from '../src/renderer/themes/afterglow'
import { catalogue } from '../src/renderer/themes/catalogue'
import { ArtworkEffectsProvider } from '../src/renderer/components/artwork-effects'
import { artworkEffects } from '../src/renderer/components/artwork-effects/interaction'

vi.mock('../src/renderer/components/Artwork', () => ({
  Artwork: () => (
    <div className="artwork">
      <img src="fixture.jpg" alt="" />
    </div>
  ),
}))
vi.mock('../src/renderer/api/hooks', () => ({ useLibrary: () => ({ data: { lists: [] } }) }))
vi.mock('@tanstack/react-virtual', () => ({
  useVirtualizer: () => ({
    getTotalSize: () => 320,
    getVirtualItems: () => [{ key: 0, index: 0, start: 0 }],
    measureElement: () => {},
    measure: () => {},
    scrollToOffset: () => {},
  }),
}))

const game: LibraryGame = {
  workId: 1,
  title: 'A long title with enough words to wrap across several lines',
  bucket: 'never_played',
  playtimeMinutes: 0,
  summary: 'The game description stays available in the details screen.',
  entries: [
    {
      ownershipId: 1,
      releaseId: 1,
      workId: 1,
      title: 'A title',
      store: 'Steam',
      installed: true,
      playtimeMinutes: 0,
    },
  ],
}
function context(mode: ThemeContext['mode']): ThemeContext {
  const profile = structuredClone(DEFAULT_PROFILE)
  profile.appearance.artwork!.finish = 'foil'
  profile.appearance.artwork!.tilt = 9
  return {
    mode,
    page: 'library',
    selectedWorkId: null,
    setPage: vi.fn(),
    openGame: vi.fn(),
    toggleFullscreen: vi.fn(),
    games: [game],
    loading: false,
    profile,
    children: null,
    renderScreen: () => null,
    actions: { launch: vi.fn() },
    components: {} as ThemeContext['components'],
    feed: {
      shelves: [
        {
          id: 'unplayed',
          title: 'A new beginning',
          blurb: '',
          supportsFeedback: false,
          reserve: [],
          items: [
            {
              ownershipId: 1,
              releaseId: 1,
              title: game.title,
              reason: 'Still waiting for your first visit.',
            },
          ],
        },
      ],
      candidateCount: 1,
      confidence: 1,
      failed: false,
    },
  }
}
function mount(context: ThemeContext, Screen: React.ComponentType<ThemeContext>) {
  return render(
    <QueryClientProvider client={new QueryClient()}>
      <ArtworkEffectsProvider options={context.profile.appearance.artwork!} reducedMotion={false}>
        <Screen {...context} />
      </ArtworkEffectsProvider>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  vi.useFakeTimers()
  vi.spyOn(artworkEffects, 'register').mockReturnValue(() => {})
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      disconnect() {}
    },
  )
  vi.stubGlobal(
    'IntersectionObserver',
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  )
  vi.stubGlobal('matchMedia', () => ({ matches: false, addEventListener() {}, removeEventListener() {} }))
})
afterEach(() => {
  cleanup()
  vi.useRealTimers()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe.each(['desktop', 'fullscreen'] as const)('Afterglow cards in %s', (mode) => {
  it('starts reduced-motion hero copy fully visible without a positional or opacity entrance', () => {
    const ctx = context(mode)
    ctx.profile.appearance.reducedMotion = true
    mount(ctx, AfterglowDiscover)
    const copy = document.querySelector<HTMLElement>('.hero-copy')!
    expect(copy).not.toBeNull()
    expect(copy.style.opacity).toBe('1')
    expect(copy.style.transform).toBe('none')
    expect(screen.queryByRole('button', { name: 'Pause rotation' })).toBeNull()
  })
  it.each([
    ['Discover', AfterglowDiscover],
    ['Library', AfterglowLibrary],
  ] as const)('%s keeps captions with stationary covers and preserves saved effects', (_, Screen) => {
    const ctx = context(mode)
    const saved = structuredClone(ctx.profile)
    mount(ctx, Screen)
    const card = screen.getByRole('button', { name: `View ${game.title}` })
    expect(card.dataset.presentation).toBe('poster')
    expect(card.dataset.preview).toBe('overlay')
    expect(card.textContent).toContain(game.title)
    expect(card.textContent).toContain('Steam')
    expect(card.textContent).toContain('Installed')
    expect(artworkEffects.register).toHaveBeenCalled()
    for (const [registration] of vi.mocked(artworkEffects.register).mock.calls) {
      expect(registration.options.finish).toBe('off')
      expect(registration.options.highlightFoil).toBe(false)
      expect(registration.options.floating).toBe(false)
    }
    fireEvent.pointerEnter(card)
    act(() => vi.advanceTimersByTime(300))
    fireEvent.focus(card)
    expect(screen.queryByRole('tooltip')).toBeNull()
    fireEvent.click(card)
    expect(ctx.openGame).toHaveBeenCalledWith(game.workId)
    expect(ctx.profile).toEqual(saved)
  })

  it('retains Catalogue’s opt-in materials and side previews', () => {
    const ctx = context(mode)
    ctx.profile.themeId = 'catalogue'
    mount(ctx, catalogue.Library!)
    expect(screen.getByRole('button', { name: `View ${game.title}` }).dataset.preview).toBe('flyout')
    expect(artworkEffects.register).toHaveBeenCalled()
    const registration = vi.mocked(artworkEffects.register).mock.calls[0][0]
    expect(registration.options.finish).toBe('foil')
    expect(registration.options.floating).toBe(true)
    expect(registration.options.tilt).toBe(9)
  })

  it('keeps eight titles in Pick up the thread', () => {
    const ctx = context(mode)
    ctx.games = Array.from({ length: 9 }, (_, index) => ({
      ...game,
      workId: index + 1,
      title: `Game ${index + 1}`,
      lastPlayedAt: '2026-09-01T12:00:00Z',
    }))
    const view = mount(ctx, AfterglowDiscover)
    expect(view.container.querySelectorAll('.returning-grid button')).toHaveLength(8)
  })
})
