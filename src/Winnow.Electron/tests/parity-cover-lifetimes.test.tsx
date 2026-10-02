// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import type { ReactNode } from 'react'
import type { ApiRequest } from '../src/shared/bridge'
import type { GameDetails, LibraryGame, Mode } from '../src/renderer/api/types'
import { Details } from '../src/renderer/features/Details'
import { Screenshots } from '../src/renderer/features/parity-details'
import { MergeQueue } from '../src/renderer/features/parity-merge'
import { ArtworkAsset } from '../src/renderer/components/ArtworkAsset'
import { artworkImages, closeArtworkImages } from '../src/renderer/components/artwork-images'
import { clearViewState } from '../src/renderer/viewState'
import type { MergeReview } from '../src/renderer/features/parity-merge-model'

const now = '2026-09-07T00:00:00Z'
const game: LibraryGame = {
  workId: 1,
  title: 'Fixture',
  playtimeMinutes: 0,
  bucket: 'never_played',
  entries: [
    {
      workId: 1,
      ownershipId: 1,
      releaseId: 1,
      title: 'Fixture',
      store: 'steam',
      installed: false,
      playtimeMinutes: 0,
    },
  ],
}
const workspace = {
  works: [{ id: 1, name: 'Fixture' }],
  externalIds: [{ releaseId: 1, provider: 'steam', providerId: '620' }],
  pluginActions: {},
  epicLaunchKeys: {},
  preferences: {},
}
const facts = {
  workId: 1,
  readAtUtc: now,
  events: [],
  ratings: [],
  achievements: [],
  journalEntries: [],
  sessions: {},
  history: {},
} as unknown as GameDetails
const clients: QueryClient[] = []
const pending: ((value: null) => void)[] = []
const pixels =
  'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAAEklEQVR4nGP4z8DwHx9mGBkKAMLXf4EvceABAAAAAElFTkSuQmCC'
beforeEach(() => {
  let sequence = 0
  URL.createObjectURL = vi.fn(() => `blob:fixture-${++sequence}`)
  URL.revokeObjectURL = vi.fn()
  vi.stubGlobal(
    'Image',
    class {
      src = ''
      naturalWidth = 8
      naturalHeight = 8
      async decode() {}
    },
  )
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({
    x: 0,
    y: 0,
    left: 0,
    top: 0,
    width: 160,
    height: 240,
    right: 160,
    bottom: 240,
    toJSON() {},
  })
})
afterEach(async () => {
  cleanup()
  pending.splice(0).forEach((resolve) => resolve(null))
  for (const client of clients.splice(0)) {
    await closeArtworkImages(client)
    client.clear()
  }
  for (const key of [
    'identity:queue-choices',
    'identity:queue-section',
    'identity:queue-answered-keys',
    'identity:queue-positions',
  ])
    clearViewState(key)
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})
function setup(mode: Mode = 'desktop', ready = false) {
  const artwork = vi.fn((_provider: string, _id: string, _width: number, _requestId?: string) =>
    ready ? Promise.resolve(pixels) : new Promise<null>((resolve) => pending.push(resolve)),
  )
  const cancel = vi.fn(async () => true)
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: {
      artwork,
      cancelRequest: cancel,
      request: vi.fn(async (input: ApiRequest) => ({
        ok: true,
        status: 200,
        data:
          input.route === 'library.get'
            ? { games: [game], lists: [] }
            : input.route === 'library.workspace'
              ? workspace
              : input.route === 'game.details'
                ? facts
                : input.route === 'metadata.get'
                  ? { available: false, fields: [] }
                  : input.route === 'metadata.igdb'
                    ? { available: false }
                    : input.route === 'artworkState'
                      ? {
                          current:
                            input.params?.workId === 1
                              ? { previewKey: { provider: 'steam', id: '620' } }
                              : null,
                          revision: 'source',
                        }
                      : input.route === 'identity.get'
                        ? { revision: 'source', workspace, candidates: [], history: [], expansions: [] }
                        : [],
      })),
    },
  })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  clients.push(client)
  return {
    client,
    artwork,
    cancel,
    cache: artworkImages(client),
    wrapper: ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>
        <div className={`avalon-shell ${mode}`}>{children}</div>
      </QueryClientProvider>
    ),
  }
}
function shots(ids: string) {
  return { ...facts, images: [{ source: 'igdb', kind: 'screenshot', imageIds: ids, observedAt: now }] }
}

for (const mode of ['desktop', 'fullscreen'] as const) {
  it(`${mode} closing Details releases its never-answering Steam620 cover consumer`, async () => {
    const f = setup(mode)
    const view = render(<Details workId={1} mode={mode} />, { wrapper: f.wrapper })
    await waitFor(() => expect(f.artwork).toHaveBeenCalledTimes(1))
    expect(f.artwork.mock.calls[0].slice(0, 3)).toEqual(['steam', '620', 160])
    expect(f.cache.liveSlots).toBe(1)
    view.unmount()
    expect(f.cache.liveSlots).toBe(0)
    expect(f.cancel).toHaveBeenCalledWith(f.artwork.mock.calls[0][3])
    expect(document.querySelector('.detail-hero img')).toBeNull()
  })

  it(`${mode} closing the screenshot strip releases all three never-answering aa11 bb22 cc33 thumbnails`, async () => {
    const f = setup(mode)
    const view = render(<Screenshots details={shots('aa11,bb22,cc33')} />, { wrapper: f.wrapper })
    await waitFor(() => expect(f.artwork).toHaveBeenCalledTimes(3))
    expect(f.artwork.mock.calls.map((call) => call.slice(0, 3))).toEqual([
      ['igdb-shot', 'aa11', 400],
      ['igdb-shot', 'bb22', 400],
      ['igdb-shot', 'cc33', 400],
    ])
    expect(f.cache.liveSlots).toBe(3)
    view.unmount()
    expect(f.cache.liveSlots).toBe(0)
    for (const call of f.artwork.mock.calls) expect(f.cancel).toHaveBeenCalledWith(call[3])
    expect(document.querySelectorAll('.screenshot-strip img')).toHaveLength(0)
  })

  it(`${mode} the real lightbox owns only aa11 then bb22 at1280 and releases its last slot on close`, async () => {
    const f = setup(mode)
    const view = render(<Screenshots details={shots('aa11,bb22')} />, { wrapper: f.wrapper })
    await waitFor(() => expect(f.cache.liveSlots).toBe(2))
    fireEvent.click(screen.getByRole('button', { name: 'Open screenshot 1 of 2' }))
    await screen.findByRole('dialog')
    await waitFor(() => expect(f.cache.liveSlots).toBe(3))
    const first = f.artwork.mock.calls.find((call) => call[1] === 'aa11' && call[2] === 1280)!
    expect(first).toBeDefined()
    fireEvent.click(screen.getByRole('button', { name: 'Next screenshot' }))
    await waitFor(() =>
      expect(f.artwork.mock.calls.some((call) => call[1] === 'bb22' && call[2] === 1280)).toBe(true),
    )
    expect(f.cache.liveSlots).toBe(3)
    expect(f.cancel).toHaveBeenCalledWith(first[3])
    const second = f.artwork.mock.calls.find((call) => call[1] === 'bb22' && call[2] === 1280)!
    fireEvent.click(screen.getByRole('button', { name: 'Close screenshots' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(f.cache.liveSlots).toBe(2)
    expect(f.cancel).toHaveBeenCalledWith(second[3])
    expect(screen.getByRole('button', { name: 'Open screenshot 1 of 2' })).toBe(document.activeElement)
    // The source isolates the lightbox's one slot; this composition also keeps two strip slots alive.
    view.unmount()
    expect(f.cache.liveSlots).toBe(0)
  })
}

it('desktop Fixture merge row releases its one pending Steam620 image used for both vivid and dormant states', async () => {
  const f = setup()
  const review = {
    revision: 'source',
    hasCompletedSweep: true,
    candidates: [
      { id: 1, leftReleaseId: 1, rightReleaseId: 2, score: 0.95, status: 'pending', signalsJson: '{}' },
    ],
    history: [],
    expansions: [],
    workspace: {
      ...workspace,
      works: [
        { id: 1, name: 'Fixture' },
        { id: 2, name: 'Fixture edition' },
      ],
      releases: [
        { id: 1, workId: 1 },
        { id: 2, workId: 2 },
      ],
      ownerships: [
        { id: 1, releaseId: 1, store: 'steam', installed: false },
        { id: 2, releaseId: 2, store: 'gog', installed: false },
      ],
      buckets: [1, 2].map((id) => ({
        ownershipId: id,
        releaseId: id,
        workId: id,
        resolvedWorkId: id,
        playtimeMinutes: 0,
        bucket: 'never_played',
      })),
    },
  } as unknown as MergeReview
  const view = render(<MergeQueue review={review} onReview={() => {}} />, { wrapper: f.wrapper })
  await waitFor(() => expect(f.artwork).toHaveBeenCalledTimes(1))
  expect(f.artwork.mock.calls[0].slice(0, 3)).toEqual(['steam', '620', 160])
  expect(f.cache.liveSlots).toBe(1)
  expect(view.container.querySelector('.merge-cover')?.getAttribute('style')).toContain('saturate(0.22)')
  view.unmount()
  expect(f.cache.liveSlots).toBe(0)
  expect(f.cancel).toHaveBeenCalledWith(f.artwork.mock.calls[0][3])
})

it('a loaded screenshot clears the outgoing DOM source before releasing the last live consumer', async () => {
  const f = setup('desktop', true)
  const view = render(
    <ArtworkAsset asset={{ provider: 'igdb-shot', id: 'aa11' }} width={1280} alt="Game screenshot" />,
    { wrapper: f.wrapper },
  )
  await waitFor(() => expect(view.container.querySelector('img')).not.toBeNull())
  const image = view.container.querySelector('img')!
  const source = image.getAttribute('src')!
  expect(f.cache.liveSlots).toBe(1)
  view.unmount()
  expect(image.getAttribute('src')).toBeNull()
  expect(f.cache.liveSlots).toBe(0)
  await closeArtworkImages(f.client)
  expect(URL.revokeObjectURL).toHaveBeenCalledExactlyOnceWith(source)
})

it('an invalidated screenshot retains its old visible lease until the refreshed same-ID image is ready', async () => {
  const f = setup('desktop', true)
  const view = render(
    <ArtworkAsset asset={{ provider: 'igdb-shot', id: 'aa11' }} width={1280} alt="Game screenshot" />,
    { wrapper: f.wrapper },
  )
  await waitFor(() => expect(view.container.querySelector('img')).not.toBeNull())
  const image = view.container.querySelector('img')!
  const original = image.getAttribute('src')!
  let complete!: (value: string) => void
  f.artwork.mockReturnValueOnce(
    new Promise<string>((resolve) => {
      complete = resolve
    }),
  )
  try {
    await act(async () => {
      await f.client.invalidateQueries({ queryKey: ['artwork-image', 'igdb-shot', 'aa11', 1280, 'asset'] })
    })
    await waitFor(() => expect(f.artwork).toHaveBeenCalledTimes(2))
    expect(view.container.querySelector('img')).toBe(image)
    expect(image.getAttribute('src')).toBe(original)
    expect(URL.revokeObjectURL).not.toHaveBeenCalledWith(original)
    expect(f.cache.liveSlots).toBe(2)
    await act(async () => complete(pixels))
    await waitFor(() => expect(image.getAttribute('src')).not.toBe(original))
    expect(f.cache.liveSlots).toBe(1)
    expect(URL.revokeObjectURL).toHaveBeenCalledWith(original)
    view.unmount()
    expect(image.getAttribute('src')).toBeNull()
    expect(f.cache.liveSlots).toBe(0)
  } finally {
    complete(pixels)
  }
})

it('a completed absent screenshot clears retained pixels after keeping them visible during refresh', async () => {
  const f = setup('desktop', true)
  const view = render(
    <ArtworkAsset asset={{ provider: 'igdb-shot', id: 'aa11' }} width={1280} alt="Game screenshot" />,
    { wrapper: f.wrapper },
  )
  const image = await screen.findByRole('img', { name: 'Game screenshot' })
  const original = image.getAttribute('src')!
  let complete!: (value: null) => void
  f.artwork.mockReturnValueOnce(
    new Promise<null>((resolve) => {
      complete = resolve
    }),
  )
  try {
    await act(async () => {
      await f.client.invalidateQueries({ queryKey: ['artwork-image', 'igdb-shot', 'aa11', 1280, 'asset'] })
    })
    await waitFor(() => expect(f.artwork).toHaveBeenCalledTimes(2))
    expect(screen.getByRole('img', { name: 'Game screenshot' })).toBe(image)
    expect(image.getAttribute('src')).toBe(original)
    expect(URL.revokeObjectURL).not.toHaveBeenCalledWith(original)
    expect(f.cache.liveSlots).toBe(2)
    await act(async () => complete(null))
    expect(await screen.findByText('Screenshot unavailable')).toBeDefined()
    expect(screen.queryByRole('img', { name: 'Game screenshot' })).toBeNull()
    expect(image.getAttribute('src')).toBeNull()
    expect(URL.revokeObjectURL).toHaveBeenCalledWith(original)
    expect(f.cache.liveSlots).toBe(0)
    view.unmount()
  } finally {
    complete(null)
  }
})

it('a failed screenshot refresh preserves its existing visible lease', async () => {
  const f = setup('desktop', true)
  const view = render(
    <ArtworkAsset asset={{ provider: 'igdb-shot', id: 'aa11' }} width={1280} alt="Game screenshot" />,
    { wrapper: f.wrapper },
  )
  const image = await screen.findByRole('img', { name: 'Game screenshot' })
  const original = image.getAttribute('src')!
  f.artwork.mockRejectedValueOnce(new Error('Temporary artwork transport failure'))
  await act(async () => {
    await f.client.invalidateQueries({ queryKey: ['artwork-image', 'igdb-shot', 'aa11', 1280, 'asset'] })
  })
  await waitFor(() => expect(f.artwork).toHaveBeenCalledTimes(2))
  await waitFor(() => expect(f.cache.pendingCount).toBe(0))
  expect(screen.getByRole('img', { name: 'Game screenshot' })).toBe(image)
  expect(image.getAttribute('src')).toBe(original)
  expect(URL.revokeObjectURL).not.toHaveBeenCalledWith(original)
  expect(f.cache.liveSlots).toBe(1)
  view.unmount()
  expect(image.getAttribute('src')).toBeNull()
  expect(f.cache.liveSlots).toBe(0)
})

it('two joined same-ID screenshots both retain their visible image after a shared refresh fails', async () => {
  const f = setup('desktop', true)
  const view = render(
    <>
      <ArtworkAsset asset={{ provider: 'igdb-shot', id: 'aa11' }} width={1280} alt="First screenshot" />
      <ArtworkAsset asset={{ provider: 'igdb-shot', id: 'aa11' }} width={1280} alt="Second screenshot" />
    </>,
    { wrapper: f.wrapper },
  )
  const first = await screen.findByRole('img', { name: 'First screenshot' })
  const second = await screen.findByRole('img', { name: 'Second screenshot' })
  const original = first.getAttribute('src')!
  expect(second.getAttribute('src')).toBe(original)
  expect(f.artwork).toHaveBeenCalledTimes(1)
  let reject!: (reason: Error) => void
  f.artwork.mockReturnValueOnce(
    new Promise<string>((_resolve, fail) => {
      reject = fail
    }),
  )
  try {
    await act(async () => {
      await f.client.invalidateQueries({ queryKey: ['artwork-image', 'igdb-shot', 'aa11', 1280, 'asset'] })
    })
    await waitFor(() => expect(f.artwork).toHaveBeenCalledTimes(2))
    expect(f.cache.liveSlots).toBe(2)
    await act(async () => reject(new Error('Shared artwork transport failure')))
    await waitFor(() => expect(f.cache.pendingCount).toBe(0))
    expect(screen.getByRole('img', { name: 'First screenshot' })).toBe(first)
    expect(screen.getByRole('img', { name: 'Second screenshot' })).toBe(second)
    expect(first.getAttribute('src')).toBe(original)
    expect(second.getAttribute('src')).toBe(original)
    expect(f.cache.liveSlots).toBe(1)
    expect(URL.revokeObjectURL).not.toHaveBeenCalledWith(original)
    view.unmount()
    expect(first.getAttribute('src')).toBeNull()
    expect(second.getAttribute('src')).toBeNull()
    expect(f.cache.liveSlots).toBe(0)
    expect(URL.revokeObjectURL).toHaveBeenCalledExactlyOnceWith(original)
  } finally {
    reject(new Error('Cleanup'))
  }
})

it('an invalid typed plugin screenshot ends loading without an image request or live lease', async () => {
  const f = setup()
  render(
    <ArtworkAsset
      asset={{ provider: 'plugin-fixture', id: 'http://images.example.test/cover.jpg' }}
      width={1280}
      alt="Game screenshot"
    />,
    { wrapper: f.wrapper },
  )
  expect(await screen.findByText('Screenshot unavailable')).toBeDefined()
  expect(f.artwork).not.toHaveBeenCalled()
  expect(f.cache.liveSlots).toBe(0)
})
