// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ThemeContext } from '../src/shared/theme'
import { DEFAULT_PROFILE, selectThemeProfile } from '../src/shared/theme'
import type { FeedVerdict, LibraryGame } from '../src/renderer/api/types'
import { avalon, AvalonDiscover } from '../src/renderer/themes/avalon'
import { AvalonCoverWorkspace } from '../src/renderer/themes/avalon-desktop-cover'
import { clearViewState } from '../src/renderer/viewState'
import { splitReason, verdictStatus } from '../src/renderer/themes/avalon-feed'

const game = (id: number): LibraryGame => ({
  workId: id,
  title: `Game ${id}`,
  bucket: 'never_played',
  playtimeMinutes: 0,
  entries: [
    {
      ownershipId: id,
      releaseId: id,
      workId: id,
      title: `Game ${id}`,
      store: 'steam',
      installed: false,
      playtimeMinutes: 0,
    },
  ],
})
let history: FeedVerdict[], failWrite: boolean, failUndo: boolean
function mount(mode: ThemeContext['mode'], configure?: (context: ThemeContext) => void) {
  const games = Array.from({ length: 12 }, (_, index) => game(index + 1))
  const feed = {
    candidateCount: 12,
    confidence: 0,
    failed: false,
    shelves: [
      {
        id: 'for-you',
        title: 'Your next game',
        blurb: 'Waiting for you',
        supportsFeedback: true,
        items: games.slice(0, 6).map((item) => ({
          ownershipId: item.workId,
          releaseId: item.workId,
          title: item.title,
          reason: `Reason for ${item.title}.`,
        })),
        reserve: games.slice(6).map((item) => ({
          ownershipId: item.workId,
          releaseId: item.workId,
          title: item.title,
          reason: `Reason for ${item.title}.`,
        })),
      },
    ],
  }
  const request = vi.fn(
    async ({ route, body }: { route: string; body?: { releaseId: number; kind: number } }) => {
      if (route === 'feedHistory') return { ok: true, status: 200, data: [...history] }
      if (route === 'feed.get') return { ok: true, status: 200, data: feed }
      if (route === 'feed.supplement')
        return { ok: true, status: 200, data: { shelves: [], candidateCount: 0 } }
      if (route === 'library.workspace')
        return {
          ok: true,
          status: 200,
          data: {
            works: [],
            externalIds: games.map((game) => ({
              releaseId: game.workId,
              provider: 'steam',
              providerId: String(game.workId),
            })),
            epicLaunchKeys: {},
            pluginActions: {},
          },
        }
      if (route === 'feedFeedback') {
        if (failWrite) return { ok: true, status: 200, data: { saved: false } }
        const row = {
          ...body!,
          createdAt: '2026-09-29T12:00:00Z',
          expiresAt: body!.kind === 1 ? '2026-10-29T12:00:00Z' : null,
          revokedAt: null,
          status: 0,
        }
        history.push(row)
        return { ok: true, status: 200, data: { saved: true, expiresAt: row.expiresAt } }
      }
      if (route === 'feedRevoke') {
        if (failUndo) return { ok: false, status: 503, message: 'Offline' }
        history = history.map((row) =>
          row.releaseId === body?.releaseId && row.kind === body.kind && row.status === 0
            ? { ...row, status: 1, revokedAt: '2026-09-30T12:00:00Z' }
            : row,
        )
        return { ok: true, status: 200, data: true }
      }
      return { ok: true, status: 200, data: {} }
    },
  )
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request } })
  const context: ThemeContext = {
    mode,
    page: 'discover',
    selectedWorkId: null,
    setPage: vi.fn(),
    openGame: vi.fn(),
    toggleFullscreen: vi.fn(),
    games,
    feed,
    loading: false,
    profile: selectThemeProfile(structuredClone(DEFAULT_PROFILE), 'avalon', avalon),
    children: null,
    renderScreen: () => null,
    actions: { launch: vi.fn() },
    components: {
      GameCard: () => null,
      ArtworkEffects: ({ children }) => <>{children}</>,
      GamePreview: () => null,
      Artwork: () => <span>Cover</span>,
      Impression: ({ children, releaseId }) => <div data-impression={releaseId}>{children}</div>,
    } as ThemeContext['components'],
  }
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  configure?.(context)
  const tree = () => (
    <QueryClientProvider client={client}>
      <AvalonCoverWorkspace.Provider
        value={{
          preferences: { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'all' },
          works: [],
          externalIds: context.games.flatMap((game) =>
            game.entries.map((entry) => ({
              releaseId: entry.releaseId,
              provider: entry.store,
              providerId: String(entry.releaseId),
            })),
          ),
          pluginActions: {},
          epicLaunchKeys: {},
        }}
      >
        <AvalonDiscover {...context} />
      </AvalonCoverWorkspace.Provider>
    </QueryClientProvider>
  )
  const rendered = render(tree())
  return { request, context, client, update: () => rendered.rerender(tree()) }
}
function revealDesktopActions() {
  for (const card of document.querySelectorAll('.avalon-feed-card')) fireEvent.mouseMove(card)
}
async function click(name: string | RegExp, index = 0) {
  act(revealDesktopActions)
  await act(async () => {
    fireEvent.click(screen.getAllByRole('button', { name })[index])
    await vi.advanceTimersByTimeAsync(1)
  })
}
async function tick(ms: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms)
  })
}
beforeEach(() => {
  history = []
  failWrite = false
  failUndo = false
  for (const mode of ['desktop', 'fullscreen'])
    for (const key of ['shelf', 'column', 'positions']) clearViewState(`avalon:home:${mode}:${key}`)
  vi.useFakeTimers()
  vi.spyOn(document, 'hasFocus').mockReturnValue(true)
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      disconnect() {}
      unobserve() {}
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

describe.each(['desktop', 'fullscreen'] as const)('feed feedback in %s', (mode) => {
  it('exposes the recommendation reason at its cover Tab stop and announces only a saved verdict', async () => {
    mount(mode, (context) => {
      context.games[0]!.title = 'Outer Wilds'
      context.feed!.shelves[0].items[0].reason = 'Bought 3 years ago, never opened.'
    })
    await tick(10)
    const cover = screen.getByRole('button', { name: 'View Outer Wilds' })
    expect(cover.getAttribute('aria-description')).toBe('Bought 3 years ago, never opened.')
    expect(
      document.querySelector('.avalon-feedback [role="status"], .avalon-feed-card-receipt [role="status"]'),
    ).toBeNull()
    await click('Not interested')
    expect(
      document.querySelector('.avalon-feedback [role="status"], .avalon-feed-card-receipt [role="status"]')
        ?.textContent,
    ).toContain('Off the feed.')
    await click('Undo')
    expect(
      document.querySelector('.avalon-feedback [role="status"], .avalon-feed-card-receipt [role="status"]'),
    ).toBeNull()
  })
  it('keeps desktop recommendation captions and membership tooltips separate from fullscreen hero actions', async () => {
    const { context } = mount(mode)
    await tick(10)
    const captions = document.querySelectorAll('.avalon-feed-card-caption')
    expect(captions).toHaveLength(mode === 'desktop' ? 5 : 0)
    if (mode === 'desktop') {
      expect(captions[0].textContent).toBe('Game 1Reason for Game 1.')
      expect(document.querySelectorAll('.avalon-feed-launch')).toHaveLength(0)
      expect(document.querySelector('.avalon-shelf-label')?.getAttribute('title')).toBe(
        context.feed!.shelves[0].blurb,
      )
    } else {
      expect(document.querySelectorAll('.avalon-feed-launch')).toHaveLength(1)
      expect(document.querySelector('.avalon-home-hero h1')?.textContent).toBe('Game 1')
    }
  })
  it('opens response history from an empty feed and returns to the same empty surface', async () => {
    mount(mode, (context) => {
      context.feed = { ...context.feed!, candidateCount: 0, shelves: [] }
    })
    await tick(10)
    await click("What you've told the feed")
    const historyPanel = screen.getByRole('region', { name: "What you've told the feed" })
    expect(within(historyPanel).getByText('Nothing yet. Your feed responses will appear here.')).toBeTruthy()
    await click('Back to the feed')
    expect(screen.queryByRole('region')).toBeNull()
    expect(screen.getByRole('button', { name: "What you've told the feed" })).toBeTruthy()
    expect(screen.queryByRole('button', { name: /^View Game/ })).toBeNull()
  })
  it('undoes the original Not interested verdict from history and retains that same history row', async () => {
    const original = { releaseId: 1, kind: 0, createdAt: '2026-09-28T12:00:00Z', status: 0 }
    history = [original]
    const { request } = mount(mode)
    await tick(10)
    await click(/What you've told the feed/)
    const panel = within(screen.getByRole('region', { name: "What you've told the feed" }))
    expect(panel.getAllByRole('listitem')).toHaveLength(1)
    expect(panel.getByText('NOT INTERESTED')).toBeTruthy()
    await click('Undo')
    expect(
      request.mock.calls.filter(([input]) => input.route === 'feedRevoke').map(([input]) => input.body),
    ).toEqual([{ releaseId: 1, kind: 0 }])
    expect(history).toEqual([{ ...original, status: 1, revokedAt: '2026-09-30T12:00:00Z' }])
    expect(panel.getAllByRole('listitem')).toHaveLength(1)
    expect(panel.getByText('Game 1')).toBeTruthy()
    expect(panel.getByText('NOT INTERESTED')).toBeTruthy()
    expect(panel.getByText(/Undone on/)).toBeTruthy()
    expect(panel.queryByRole('button', { name: 'Undo' })).toBeNull()
  })
  function withReplacement(configure?: (context: ThemeContext) => void) {
    const view = mount(mode, (context) => {
      if (mode === 'fullscreen')
        context.feed!.shelves[0].reserve = context.feed!.shelves[0].reserve.slice(0, 4)
      configure?.(context)
    })
    return () => {
      if (mode !== 'fullscreen') return
      // Fullscreen displays the initial reserve. A later pass can supply new
      // replacement games while the current receipt stays in its original slot.
      view.context.feed!.shelves[0].reserve.push(
        ...[11, 12].map((id) => ({
          ownershipId: id,
          releaseId: id,
          title: `Game ${id}`,
          reason: `Reason for Game ${id}.`,
        })),
      )
      view.context.feed = { ...view.context.feed! }
      view.update()
    }
  }
  it('retains both verdict attempts and the revocation stamp after dismiss, Undo and dismiss again', async () => {
    mount(mode)
    await click('Not interested')
    await click('Undo')
    await click('Not interested')
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /What you've told the feed/ }))
      await vi.advanceTimersByTimeAsync(10)
    })
    const dialog = within(screen.getByRole('region'))
    expect(dialog.getAllByRole('listitem')).toHaveLength(2)
    expect(dialog.getAllByRole('button', { name: 'Undo' })).toHaveLength(1)
    expect(dialog.getByText(/Undone on/)).toBeDefined()
    expect(dialog.getByText(/Off the feed since/)).toBeDefined()
    expect(history.map((row) => row.status)).toEqual([1, 0])
    expect(history[0].revokedAt).toBeTruthy()
  })
  it('states the history count only after responses exist and stays quiet for recent play alone', async () => {
    const { context, update } = mount(mode, (context) => {
      context.feed!.candidateCount = 0
      context.feed!.shelves[0].supportsFeedback = false
      context.feed!.shelves[0].id = 'recently_played'
    })
    await tick(10)
    expect(screen.getByRole('button', { name: "What you've told the feed" })).toBeDefined()
    expect(screen.queryByText(/games scored/)).toBeNull()
    expect(screen.queryByText(/Improves as you play/)).toBeNull()
    expect(screen.queryByRole('button', { name: 'Not now' })).toBeNull()
    context.feed = {
      ...context.feed!,
      shelves: context.feed!.shelves.map((shelf) => ({
        ...shelf,
        id: 'recommended',
        supportsFeedback: true,
      })),
    }
    update()
    await click('Not now')
    await tick(10)
    expect(screen.getByRole('button', { name: /What you've told the feed\s*1/ })).toBeDefined()
  })
  it('launches the installed supported entry, prevents repeat dispatch and offers retry after a failure', async () => {
    let reject!: (failure: Error) => void
    const launch = vi
      .fn()
      .mockImplementationOnce(
        () =>
          new Promise<void>((_, failed) => {
            reject = failed
          }),
      )
      .mockResolvedValue(undefined)
    mount(mode, (context) => {
      context.games[0].entries[0].installed = true
      context.actions.launch = launch
    })
    await tick(10)
    act(revealDesktopActions)
    const play = screen.getByRole('button', { name: mode === 'desktop' ? /^Play$/ : 'Play Game 1' })
    await act(async () => {
      fireEvent.click(play)
      fireEvent.click(play)
    })
    expect(launch).toHaveBeenCalledTimes(1)
    expect(launch).toHaveBeenCalledWith(1)
    if (mode === 'desktop') expect(play.getAttribute('aria-disabled')).toBe('true')
    else expect((play as HTMLButtonElement).disabled).toBe(true)
    await act(async () => reject(Error('Launcher unavailable')))
    expect(screen.getByRole('alert').textContent).toBe(
      mode === 'desktop' ? 'Play could not finish. Try again.' : 'Launcher unavailable',
    )
    await click(mode === 'desktop' ? /^Play$/ : 'Play Game 1')
    expect(launch).toHaveBeenCalledTimes(2)
    expect(screen.queryByRole('alert')).toBeNull()
    if (mode === 'fullscreen')
      await act(async () => screen.getByRole('button', { name: 'View Game 2' }).focus())
    if (mode === 'desktop') expect(screen.getAllByRole('button', { name: /^Install$/ })).toHaveLength(4)
    else expect(screen.getByRole('button', { name: 'Install Game 2' })).toBeDefined()
  })
  it('shows build, quiet, and failure states and retains good cards after a failed reload', async () => {
    const { context, update } = mount(mode, (context) => {
      context.feedLoading = true
      context.feed = undefined
    })
    expect(screen.getByText('Building the feed…')).toBeDefined()
    context.feedLoading = false
    context.feed = { candidateCount: 0, confidence: 0, failed: false, shelves: [] }
    update()
    expect(screen.getByText('Recommendations appear here as Winnow learns about your library.')).toBeDefined()
    context.feed.candidateCount = 12
    update()
    expect(
      screen.getByText('Nothing to suggest right now. Your library is still here to explore.'),
    ).toBeDefined()
    context.feedFailed = true
    update()
    expect(screen.getByRole('button', { name: 'Try again' })).toBeDefined()
    await click('Try again')
    await tick(10)
    expect(screen.getByRole('button', { name: 'View Game 1' })).toBeDefined()
    context.feed = { ...context.feed, failed: true, shelves: [] }
    update()
    expect(screen.getByRole('button', { name: 'View Game 1' })).toBeDefined()
  })
  it('steps a reduced-motion receipt once a second and pauses it while the window is inactive', async () => {
    const refill = withReplacement((context) => {
      context.profile.appearance.reducedMotion = true
    })
    await click('Not interested')
    refill()
    await tick(800)
    expect(screen.getByRole('progressbar').getAttribute('value')).toBe('0')
    await tick(200)
    expect(screen.getByRole('progressbar').getAttribute('value')).toBe('1000')
    vi.mocked(document.hasFocus).mockReturnValue(false)
    await tick(6000)
    expect(screen.getByRole('progressbar').getAttribute('value')).toBe('1000')
    vi.mocked(document.hasFocus).mockReturnValue(true)
    await tick(1000)
    expect(screen.getByRole('progressbar').getAttribute('value')).toBe('2000')
    await tick(1000)
    expect(screen.queryByRole('button', { name: 'View Game 1' })).toBeNull()
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /What you've told the feed/ }))
      await vi.advanceTimersByTimeAsync(1)
    })
    expect(within(screen.getByRole('region')).getByText('Game 1')).toBeDefined()
  })
  it('states a snooze date, keeps the original card and restores controls through Undo', async () => {
    const { request } = mount(mode)
    const cover = screen.getByRole('button', { name: 'View Game 1' })
    expect(cover.getAttribute('aria-description')).toBe('Reason for Game 1.')
    await click('Not now')
    expect(
      document.querySelector('.avalon-feedback [role="status"], .avalon-feed-card-receipt [role="status"]')
        ?.textContent,
    ).toContain('Back on 29 Oct 2026')
    expect(screen.getByRole('button', { name: 'View Game 1' })).toBe(cover)
    await click('Undo')
    expect(screen.queryByRole('progressbar')).toBeNull()
    await tick(6000)
    expect(screen.getByRole('button', { name: 'View Game 1' })).toBe(cover)
    expect(request).toHaveBeenCalledWith(
      expect.objectContaining({ route: 'feedRevoke', body: { releaseId: 1, kind: 1 } }),
    )
  })
  it('holds a focused undo and the hovered cover before replacing only that card', async () => {
    const refill = withReplacement()
    act(revealDesktopActions)
    const response = screen.getAllByRole('button', { name: 'Not interested' })[0]
    await act(async () => {
      response.focus()
      fireEvent.click(response)
      await vi.advanceTimersByTimeAsync(1)
    })
    refill()
    expect(document.activeElement?.textContent).toBe('Undo')
    await tick(6000)
    expect(screen.getByRole('progressbar').getAttribute('value')).toBe('0')
    const cover = screen.getByRole('button', { name: 'View Game 1' }),
      neighbor = screen.getByRole('button', { name: 'View Game 2' })
    await act(async () => {
      ;(document.activeElement as HTMLElement).blur()
      fireEvent.mouseEnter(cover)
    })
    await tick(6000)
    expect(screen.getByRole('button', { name: 'View Game 1' })).toBe(cover)
    await act(async () => fireEvent.mouseLeave(cover))
    await tick(3100)
    expect(screen.queryByRole('button', { name: 'View Game 1' })).toBeNull()
    expect(screen.getByRole('button', { name: `View Game ${mode === 'desktop' ? 6 : 11}` })).toBeDefined()
    expect(screen.getByRole('button', { name: 'View Game 2' })).toBe(neighbor)
  })
  it('failed feedback shows no receipt and retry keeps the intended verdict kind', async () => {
    const { request } = mount(mode)
    failWrite = true
    await click('Not interested')
    expect(screen.getByRole('alert').textContent).toContain('could not be saved')
    expect(screen.queryByRole('button', { name: 'Undo' })).toBeNull()
    failWrite = false
    await click('Not interested')
    expect(
      document.querySelector('.avalon-feedback [role="status"], .avalon-feed-card-receipt [role="status"]')
        ?.textContent,
    ).toContain('Off the feed.')
    expect(
      request.mock.calls
        .filter(([value]) => value.route === 'feedFeedback')
        .map(([value]) => value.body?.kind),
    ).toEqual([0, 0])
  })
  it('retains receipts through a feed refresh and pauses while history is open', async () => {
    const { context, update } = mount(mode)
    await click('Not interested')
    context.feed = {
      ...context.feed!,
      shelves: context.feed!.shelves.map((shelf) => ({
        ...shelf,
        items: shelf.items.filter((row) => row.releaseId !== 1),
      })),
    }
    update()
    expect(screen.getByRole('button', { name: 'View Game 1' })).toBeDefined()
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /What you've told the feed/ }))
      await vi.advanceTimersByTimeAsync(1)
    })
    await tick(6000)
    const dialog = screen.getByRole('region')
    expect(within(dialog).getByText('Game 1')).toBeDefined()
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Undo' }))
      await vi.advanceTimersByTimeAsync(10)
    })
    expect(within(dialog).getByText(/Undone on/)).toBeDefined()
    await click('Back to the feed')
    await tick(4000)
    expect(screen.getByRole('button', { name: 'View Game 1' })).toBeDefined()
    expect(screen.queryByRole('progressbar')).toBeNull()
  })
  it('history retains missing games and all statuses, and a failed Undo can be retried', async () => {
    history = [
      { releaseId: 99, kind: 0, createdAt: '2026-09-01T12:00:00Z', status: 0 },
      {
        releaseId: 1,
        kind: 1,
        createdAt: '2026-08-01T12:00:00Z',
        expiresAt: '2026-09-01T12:00:00Z',
        status: 2,
      },
      {
        releaseId: 2,
        kind: 0,
        createdAt: '2026-07-01T12:00:00Z',
        revokedAt: '2026-07-02T12:00:00Z',
        status: 1,
      },
    ]
    mount(mode)
    await tick(10)
    await act(async () => fireEvent.click(screen.getByRole('button', { name: /What you've told the feed/ })))
    const dialog = screen.getByRole('region')
    expect(within(dialog).getAllByRole('listitem')).toHaveLength(3)
    expect(within(dialog).getByText('A game that is no longer in your library')).toBeDefined()
    expect(within(dialog).getAllByRole('button', { name: 'Undo' })).toHaveLength(1)
    failUndo = true
    await click('Undo')
    expect(within(dialog).getByRole('alert').textContent).toContain('Offline')
    expect(within(dialog).getByRole('button', { name: 'Undo' })).toBeDefined()
    failUndo = false
    await click('Undo')
    expect(within(dialog).queryByRole('button', { name: 'Undo' })).toBeNull()
  })
})

it('renders a thin shelf as complete and places its own count beside the desktop heading', () => {
  mount('desktop', (context) => {
    context.feed!.shelves[0].items = context.feed!.shelves[0].items.slice(0, 3)
    context.feed!.shelves[0].reserve = []
  })
  const heading = screen.getByRole('heading', { name: 'Your next game' })
  expect(heading.closest('header')?.querySelector('.avalon-shelf-count')?.textContent).toBe('3')
  expect(screen.getAllByRole('button', { name: /^View Game/ })).toHaveLength(3)
  expect(screen.queryByText('Building the feed…')).toBeNull()
})

it.each([0, 1, 2])(
  'calibrates desktop confidence copy for tier %s and shows the candidate count once known',
  (confidence) => {
    const { context, update } = mount('desktop', (context) => {
      context.feed!.confidence = confidence
      context.feed!.candidateCount = 0
    })
    expect(screen.queryByText(/games scored/)).toBeNull()
    context.feed = { ...context.feed!, candidateCount: 997 }
    update()
    expect(screen.getByText('997')).toBeDefined()
    if (confidence === 0) expect(screen.getByText(/Improves as you play/)).toBeDefined()
    else if (confidence === 1)
      expect(screen.getByText('Recorded sessions help refine your picks.')).toBeDefined()
    else {
      expect(screen.queryByText(/Improves as you play/)).toBeNull()
      expect(screen.queryByText('Recorded sessions help refine your picks.')).toBeNull()
    }
  },
)

it('history dates refer to the act each status names', () => {
  const row = {
    releaseId: 1,
    kind: 1,
    createdAt: 'created',
    expiresAt: 'expires',
    revokedAt: 'revoked',
    status: 0,
  }
  expect(verdictStatus(row)).toEqual({ note: 'Back on', date: 'expires' })
  expect(verdictStatus({ ...row, kind: 0 })).toEqual({ note: 'Off the feed since', date: 'created' })
  expect(verdictStatus({ ...row, status: 1 })).toEqual({ note: 'Undone on', date: 'revoked' })
  expect(verdictStatus({ ...row, status: 2 })).toEqual({ note: 'Lapsed on', date: 'expires' })
})

it.each([
  'You played 2.8 hours in 2021. “UPDATE-1.2” is waiting!',
  'A sentence without numbers.',
  '',
  '  12h\n(3.5%) — ٢٠٢٦  ',
])('sets numeric words in the data face without changing the sentence: %s', (reason) => {
  const runs = splitReason(reason)
  expect(runs.map((run) => run.text).join('')).toBe(reason)
  expect(runs.filter((run) => run.data).every((run) => /[0-9]/.test(run.text))).toBe(true)
  if (reason.startsWith('You'))
    expect(runs.filter((run) => run.data).map((run) => run.text)).toEqual(['2.8', '2021', 'UPDATE-1.2'])
  if (!reason) expect(runs).toEqual([])
  if (reason.startsWith('A ')) expect(runs).toEqual([{ text: reason, data: false }])
})

describe.each(['desktop', 'fullscreen'] as const)('frozen recommendation contracts on %s', (mode) => {
  const reason = 'You put 2.8 hours into this in 2021 and it has had an update since.'
  function single(context: ThemeContext, feedback = true) {
    context.games = [{ ...game(1), title: 'Deep Rock Galactic1' }]
    context.feed = {
      candidateCount: 997,
      confidence: 1,
      failed: false,
      shelves: [
        {
          id: 'patched_while_away',
          title: 'Patched while you were away',
          blurb: 'Pitch.',
          supportsFeedback: feedback,
          items: [{ ownershipId: 1, releaseId: 1, title: 'Deep Rock Galactic1', reason }],
          reserve: [],
        },
      ],
    }
  }
  it('with no feedback store the exact original card retains its ordinary actions without verdict controls', async () => {
    mount(mode, (context) => single(context, false))
    await tick(10)
    const cover = screen.getByRole('button', { name: 'View Deep Rock Galactic1' })
    expect(cover.getAttribute('aria-description')).toContain(reason)
    act(revealDesktopActions)
    expect(screen.queryByRole('button', { name: 'Not now' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Not interested' })).toBeNull()
    expect(screen.getByRole('button', { name: /Add.*list/i })).toBeDefined()
    if (mode === 'fullscreen') {
      act(() => cover.focus())
      await click('More')
      const sheet = within(screen.getByRole('dialog', { name: 'More game actions' }))
      expect(sheet.getByRole('button', { name: 'Open game' })).toBeDefined()
      expect(sheet.queryByRole('button', { name: 'Not interested' })).toBeNull()
    }
  })
  it('history takes the original997-candidate body and returns the same card and trigger focus', async () => {
    mount(mode, single)
    await tick(10)
    const card = screen.getByRole('button', { name: 'View Deep Rock Galactic1' })
    const trigger = screen.getByRole('button', { name: "What you've told the feed" })
    act(() => trigger.focus())
    await click("What you've told the feed")
    const body = screen.getByRole('region', { name: "What you've told the feed" })
    expect(screen.queryByRole('button', { name: 'View Deep Rock Galactic1' })).toBeNull()
    expect(card.isConnected).toBe(true)
    expect(card.closest('[hidden]')).not.toBeNull()
    expect(document.querySelector('.avalon-feed-body[hidden]')).not.toBeNull()
    expect(within(body).getByRole('button', { name: 'Back to the feed' })).toBe(document.activeElement)
    await click('Back to the feed')
    expect(screen.queryByRole('region', { name: "What you've told the feed" })).toBeNull()
    expect(screen.getByRole('button', { name: 'View Deep Rock Galactic1' })).toBe(card)
    expect(document.activeElement).toBe(trigger)
    if (mode === 'desktop') expect(screen.getByText('997')).toBeDefined()
  })
  it('optional arrival retains the exact First game card, focused control and primary reason', async () => {
    const { context, update } = mount(mode, (context) => {
      context.games = [
        { ...game(1), title: 'First game' },
        { ...game(2), title: 'Second game' },
      ]
      context.feed = {
        candidateCount: 2,
        confidence: 0,
        failed: false,
        shelves: [
          {
            id: 'builtin',
            title: 'Built in',
            blurb: '',
            supportsFeedback: true,
            items: [{ ownershipId: 1, releaseId: 1, title: 'First game', reason: 'Baseline reason' }],
            reserve: [],
          },
        ],
      }
    })
    await tick(10)
    const cover = screen.getByRole('button', { name: 'View First game' })
    act(() => cover.focus())
    context.feed = {
      ...context.feed!,
      shelves: [
        ...context.feed!.shelves,
        {
          id: 'plugin:extra',
          title: 'Optional',
          blurb: '',
          supportsFeedback: true,
          items: [{ ownershipId: 2, releaseId: 2, title: 'Second game', reason: 'Optional reason' }],
          reserve: [],
        },
      ],
    }
    act(update)
    await tick(10)
    expect(screen.getByRole('button', { name: 'View First game' })).toBe(cover)
    expect(document.activeElement).toBe(cover)
    expect(cover.getAttribute('aria-description')).toContain('Baseline reason')
  })
  it('recent and recommended copies of the same First game have separate verdict and impression provenance', async () => {
    const { request } = mount(mode, (context) => {
      context.games = [
        { ...game(1), title: 'First game' },
        { ...game(2), title: 'Second game' },
      ]
      const item = { ownershipId: 1, releaseId: 1, title: 'First game', reason: 'Last played today.' }
      context.feed = {
        candidateCount: 1,
        confidence: 2,
        failed: false,
        shelves: [
          {
            id: 'recently_played',
            title: 'Recently played',
            blurb: 'Your latest games',
            supportsFeedback: false,
            items: [item],
            reserve: [],
          },
          {
            id: 'recommended',
            title: 'Recommended',
            blurb: '',
            supportsFeedback: true,
            items: [item],
            reserve: [],
          },
        ],
      }
    })
    await tick(10)
    if (mode === 'desktop') {
      const [recent, recommended] = [...document.querySelectorAll('.avalon-shelf')]
      act(revealDesktopActions)
      expect(within(recent as HTMLElement).queryByRole('button', { name: 'Not interested' })).toBeNull()
      expect(recent.querySelector('[data-impression]')).toBeNull()
      expect(recommended.querySelector('[data-impression="1"]')).not.toBeNull()
      expect(within(recommended as HTMLElement).getByRole('button', { name: 'Not interested' })).toBeDefined()
    } else {
      const cover = screen.getByRole('button', { name: 'View First game' })
      act(() => cover.focus())
      await click('More')
      let dialog = screen.getByRole('dialog', { name: 'More game actions' })
      expect(within(dialog).getByRole('button', { name: 'Open game' })).toBeDefined()
      expect(within(dialog).queryByRole('button', { name: 'Not now' })).toBeNull()
      expect(document.querySelector('[data-row-active="true"] [data-impression]')).toBeNull()
      fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
      await tick(250)
      expect(document.activeElement).toBe(cover)
      fireEvent.keyDown(cover, { key: 'ArrowDown' })
      await tick(250)
      await click('More')
      dialog = screen.getByRole('dialog', { name: 'More game actions' })
      expect(within(dialog).getByRole('button', { name: 'Not interested' })).toBeDefined()
      fireEvent.click(within(dialog).getByRole('button', { name: 'Not interested' }))
      await tick(10)
      expect(
        request.mock.calls.filter(([input]) => input.route === 'feedFeedback').map(([input]) => input.body),
      ).toEqual([{ releaseId: 1, kind: 0 }])
    }
  })
  it.each([0, 1])(
    'a thrown feed service with %i known tiles shows a retry sentence without synthesizing recent-play shelves',
    async (count) => {
      mount(mode, (context) => {
        context.games = count ? [{ ...game(1), lastPlayedAt: '2021-06-01T00:00:00Z' }] : []
        context.feed = undefined
        context.feedFailed = true
      })
      await tick(10)
      expect(
        screen.getByText('Recommendations could not be loaded. Your library is still available.'),
      ).toBeDefined()
      expect(screen.getByRole('button', { name: 'Try again' })).toBeDefined()
      expect(screen.queryByRole('button', { name: 'View Game 1' })).toBeNull()
    },
  )
})

it.each(['desktop', 'fullscreen'] as const)(
  '%s preserves all five frozen engine shelves, pitches and verbatim reasons',
  async (mode) => {
    const rows = [
      [
        'patched_while_away',
        'Patched while you were away',
        'Major updates landed after you stopped playing.',
        'You put 2.8 hours into this in 2021 and it has had an update since, most recently "PATCH NOTES - S06.05.02". This matches your taste in Survival games.',
      ],
      [
        'worth_another_look',
        'Worth another look',
        'You committed real hours past the refund line, then drifted off mid-story.',
        'You put 2.5 hours in — past the refund line — then let it go — that was 2022.',
      ],
      [
        'ready_to_play',
        'Installed and waiting',
        'Already on your disk with nothing sunk.',
        "Never opened since it joined your library. It's installed and ready to launch.",
      ],
      [
        'barely_touched',
        'Barely gave it a chance',
        'Under 2 hours in — you opened the door and never walked through.',
        'You tried it for 104 minutes and never went back — that was 2017.',
      ],
      [
        'on_your_taste',
        'Never opened, right up your alley',
        'Sitting sealed in your library, and it matches where your hours actually go.',
        'Never opened since it joined your library. This matches your taste in Sandbox games.',
      ],
    ]
    mount(mode, (context) => {
      context.games = rows.map((_, index) => ({
        ...game(index + 1),
        title: `Deep Rock Galactic ${index + 1}`,
        playtimeMinutes: 168,
        bucket: 'stale_but_patched',
        lastPlayedAt: '2021-06-01T00:00:00Z',
        lastMajorUpdateAt: '2025-06-01T00:00:00Z',
      }))
      context.feed = {
        candidateCount: 997,
        confidence: 1,
        failed: false,
        shelves: rows.map(([id, title, blurb, reason], index) => ({
          id,
          title,
          blurb,
          items: [
            {
              ownershipId: index + 1,
              releaseId: index + 1,
              title: `Deep Rock Galactic ${index + 1}`,
              reason,
            },
          ],
          reserve: [],
          supportsFeedback: true,
        })),
      }
    })
    await tick(10)
    expect(screen.queryByRole('alert')).toBeNull()
    if (mode === 'desktop') {
      const shelves = [...document.querySelectorAll('.avalon-shelf')]
      expect(shelves.map((element) => element.querySelector('h2')?.textContent)).toEqual(
        rows.map((row) => row[1]),
      )
      expect(shelves.map((element) => element.querySelector('header p')?.textContent)).toEqual(
        rows.map((row) => row[2]),
      )
      expect(
        shelves.map((element) => element.querySelector('.avalon-feed-card-caption > span')?.textContent),
      ).toEqual(rows.map((row) => row[3]))
    } else {
      for (let index = 0; index < rows.length; index++) {
        expect(document.querySelector('.avalon-home-hero > .avalon-label')?.textContent).toBe(rows[index][1])
        expect(document.querySelector('.avalon-home-hero > p')?.textContent).toBe(rows[index][3])
        const cover = screen.getByRole('button', {
          name: `View Deep Rock Galactic ${index + 1}, patched since you played`,
        })
        expect(cover.getAttribute('aria-description')).toBe(rows[index][3])
        fireEvent.keyDown(cover, { key: 'ArrowDown' })
        await tick(250)
      }
    }
  },
)
