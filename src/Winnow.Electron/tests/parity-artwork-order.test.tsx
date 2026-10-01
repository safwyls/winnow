// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { AvalonBackdrop } from '../src/renderer/themes/avalon-backdrop'
import { loadBackdropImage } from '../src/renderer/themes/avalon-backdrop-image'
import { Details } from '../src/renderer/features/Details'
import { primaryEntry } from '../src/shared/game-actions'
import type { LibraryGame, Mode, Workspace } from '../src/renderer/api/types'
import type { BackdropPixels, BackdropSelection } from '../src/renderer/themes/avalon-backdrop-model'
import type { ApiRequest, BackendEvent, WinnowBridge } from '../src/shared/bridge'

vi.mock('../src/renderer/themes/avalon-backdrop-image', () => ({ loadBackdropImage: vi.fn() }))
const listeners = new Set<(event: BackendEvent) => void>()
const clients: QueryClient[] = []
const steam = { provider: 'steam-hero', id: '42' }
const igdb = { provider: 'igdb-backdrop', id: 'art' }
const pixels = (source: string, height: number): BackdropPixels => ({
  source,
  width: 32,
  height,
  dispose: vi.fn(),
})
let order: string[]
let request: ReturnType<typeof vi.fn>
beforeEach(() => {
  listeners.clear()
  order = ['steam', 'steamgriddb', 'igdb']
  vi.mocked(loadBackdropImage).mockReset()
  vi.stubGlobal('devicePixelRatio', 1)
  vi.stubGlobal('matchMedia', () => ({ matches: false, addEventListener() {}, removeEventListener() {} }))
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      disconnect() {}
    },
  )
  vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(1920)
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(1080)
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({
    x: 0,
    y: 0,
    left: 0,
    top: 0,
    right: 1920,
    bottom: 1080,
    width: 1920,
    height: 1080,
    toJSON() {},
  })
  request = vi.fn(async () => ({
    ok: true,
    status: 200,
    data: {
      candidates:
        order[0] === 'steam'
          ? [
              { key: steam, aspectRatio: 32 / 10, fitWholeHero: true },
              { key: igdb, aspectRatio: 16 / 9, fitWholeHero: false },
            ]
          : [
              { key: igdb, aspectRatio: 16 / 9, fitWholeHero: false },
              { key: steam, aspectRatio: 32 / 10, fitWholeHero: true },
            ],
      coverKey: null,
    } satisfies BackdropSelection,
  }))
  window.winnow = {
    request,
    cancelRequest: vi.fn(async () => true),
    onEvent: vi.fn((callback: (event: BackendEvent) => void) => {
      listeners.add(callback)
      return () => listeners.delete(callback)
    }),
  } as unknown as WinnowBridge
})
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

it.each<Mode>(['desktop', 'fullscreen'])(
  '%s grouped installed GOG primary keeps the Steam42 hero with duplicate Steam ownerships',
  async (mode) => {
    const entry = (ownershipId: number, releaseId: number, store: string, installed: boolean) => ({
      ownershipId,
      releaseId,
      workId: 1,
      title: 'Game',
      store,
      installed,
      playtimeMinutes: 0,
      lastPlayedAt: null,
    })
    const game: LibraryGame = {
      workId: 1,
      title: 'Game',
      bucket: 'never_played',
      playtimeMinutes: 0,
      lastPlayedAt: null,
      entries: [entry(1, 1, 'gog', true), entry(2, 2, 'steam', false), entry(3, 2, 'steam', false)],
    }
    const workspace: Workspace = {
      preferences: { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'AdultsOnly' },
      works: [{ id: 1, name: 'Game' }],
      externalIds: [
        { releaseId: 1, provider: 'gog', providerId: '1' },
        { releaseId: 2, provider: 'steam', providerId: '42' },
      ],
      pluginActions: {},
      epicLaunchKeys: {},
    }
    const primary = primaryEntry(game.entries, workspace)!
    expect(primary).toBe(game.entries[0])
    expect(primary.store).toBe('gog')
    expect(
      workspace.externalIds.find((id) => id.releaseId === primary.releaseId && id.provider === 'steam')
        ?.providerId ?? null,
    ).toBeNull()
    expect(game.entries.map((value) => [value.ownershipId, value.releaseId])).toEqual([
      [1, 1],
      [2, 2],
      [3, 2],
    ])
    const hero = pixels('blob:grouped-steam42', 10)
    request.mockImplementation(async (input: ApiRequest) => {
      let data: unknown = {}
      if (input.route === 'library.get') data = { games: [game], lists: [] }
      if (input.route === 'library.workspace') data = workspace
      if (input.route === 'game.details')
        data = {
          workId: 1,
          sessions: {},
          events: [],
          acknowledgements: {},
          history: {},
          ownerships: [],
          journalEntries: [],
          ratings: [],
          achievements: [],
          images: [],
        }
      if (input.route === 'metadata.get')
        data = { workId: 1, title: 'Game', revision: 'metadata', fields: [], isPinned: false }
      if (input.route === 'metadata.igdb') data = { workId: 1, available: false, revision: 'igdb' }
      if (input.route === 'identity.get') data = { revision: 'identity' }
      if (input.route === 'artwork.backdrop')
        data = {
          candidates: [{ key: steam, aspectRatio: 32 / 10, fitWholeHero: true }],
          coverKey: null,
        } satisfies BackdropSelection
      if (input.route === 'actions.execute') data = 0
      return { ok: true, status: 200, data }
    })
    vi.mocked(loadBackdropImage).mockResolvedValue(hero)
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    })
    clients.push(client)
    const view = render(
      <QueryClientProvider client={client}>
        <Details workId={1} mode={mode} presentation="avalon" onClose={vi.fn()} />
      </QueryClientProvider>,
    )
    await waitFor(() =>
      expect(document.querySelector('.avalon-backdrop img')?.getAttribute('src')).toBe(hero.source),
    )
    expect(vi.mocked(loadBackdropImage).mock.calls.map(([key]) => key)).toEqual([steam])
    const backdropReads = request.mock.calls
      .map(([input]) => input as ApiRequest)
      .filter((input) => input.route === 'artwork.backdrop')
    expect(backdropReads.length).toBeGreaterThan(0)
    expect(backdropReads.every((input) => input.params?.workId === 1)).toBe(true)
    fireEvent.click(await screen.findByRole('button', { name: 'Play' }))
    await waitFor(() =>
      expect(
        request.mock.calls.some(
          ([input]) => input.route === 'actions.execute' && input.params?.ownershipId === 1,
        ),
      ).toBe(true),
    )
    expect(vi.mocked(loadBackdropImage).mock.calls.map(([key]) => key)).toEqual([steam])
    view.unmount()
    expect(hero.dispose).toHaveBeenCalledOnce()
  },
)
function saveOrder(next: string[]) {
  order = next
  for (const listener of listeners)
    listener({
      kind: 'preferences.changed',
      resource: 'ArtworkSourceOrder',
      epoch: 'source-fixture',
      sequence: 1,
    })
}

it.each([false, true])(
  'retains exact 32×10 Steam42 pixels until 32×18 IGDB art arrives and unsubscribes on detach, fullscreen %s',
  async (fullscreen) => {
    const oldArt = pixels('blob:old-32x10', 10),
      nextArt = pixels('blob:new-32x18', 18)
    let finish!: (value: BackdropPixels) => void
    vi.mocked(loadBackdropImage).mockImplementation((key) =>
      key.provider === 'steam-hero'
        ? Promise.resolve(oldArt)
        : new Promise((resolve) => {
            finish = resolve
          }),
    )
    const view = render(<AvalonBackdrop workId={1} fullscreen={fullscreen} reducedMotion />)
    await waitFor(() => expect(view.container.querySelector('img')?.getAttribute('src')).toBe(oldArt.source))
    const originalImage = view.container.querySelector('img')!
    expect(loadBackdropImage).toHaveBeenCalledTimes(1)
    expect(vi.mocked(loadBackdropImage).mock.calls[0][0]).toEqual(steam)
    expect(oldArt.width).toBe(32)
    expect(oldArt.height).toBe(10)
    act(() => saveOrder(fullscreen ? ['igdb', 'steamgriddb', 'steam'] : ['igdb', 'steam', 'steamgriddb']))
    await waitFor(() => expect(finish).toBeDefined())
    expect(vi.mocked(loadBackdropImage).mock.calls[1][0]).toEqual(igdb)
    expect(view.container.querySelector('img')).toBe(originalImage)
    expect(originalImage.getAttribute('src')).toBe(oldArt.source)
    expect(oldArt.dispose).not.toHaveBeenCalled()
    await act(async () => finish(nextArt))
    await waitFor(() => expect(originalImage.getAttribute('src')).toBe(nextArt.source))
    expect(view.container.querySelector('img')).toBe(originalImage)
    expect(view.container.querySelectorAll('img')).toHaveLength(1)
    expect(nextArt.width).toBe(32)
    expect(nextArt.height).toBe(18)
    expect(oldArt.dispose).toHaveBeenCalledOnce()
    expect(nextArt.dispose).not.toHaveBeenCalled()
    expect(listeners.size).toBe(1)
    view.unmount()
    const readsAtDetach = request.mock.calls.length,
      imagesAtDetach = vi.mocked(loadBackdropImage).mock.calls.length
    expect(listeners.size).toBe(0)
    await act(async () => saveOrder(['steam', 'igdb', 'steamgriddb']))
    expect(request).toHaveBeenCalledTimes(readsAtDetach)
    expect(loadBackdropImage).toHaveBeenCalledTimes(imagesAtDetach)
    expect(oldArt.dispose).toHaveBeenCalledOnce()
    expect(nextArt.dispose).toHaveBeenCalledOnce()
    expect(originalImage.isConnected).toBe(false)
    expect(originalImage.getAttribute('src')).toBeNull()
  },
)

it('uses the unowned canonical root background before Steam42 and SGDB50584 without requesting the owned child background', async () => {
  const rootSaved = { provider: 'user', id: 'rootsaved' }
  const childSaved = { provider: 'user', id: 'childsaved' }
  const sgdb = { provider: 'steamgriddb-hero', id: '61ba87bf4177f576150389d84d14bb01.png' }
  const sgdbArt = pixels('blob:sgdb-50584', 10)
  request.mockImplementation(async (input: ApiRequest) => {
    expect(input.route).toBe('artwork.backdrop')
    expect(input.params?.workId).toBe(1)
    return {
      ok: true,
      status: 200,
      data: {
        candidates: [
          { key: rootSaved, aspectRatio: 0, fitWholeHero: false },
          { key: steam, aspectRatio: 1920 / 620, fitWholeHero: true },
          { key: sgdb, aspectRatio: 1920 / 620, fitWholeHero: true },
        ],
        coverKey: null,
      } satisfies BackdropSelection,
    }
  })
  vi.mocked(loadBackdropImage).mockImplementation(async (key) =>
    key.provider === sgdb.provider ? sgdbArt : null,
  )
  const view = render(<AvalonBackdrop workId={1} fullscreen reducedMotion />)
  await waitFor(() => expect(view.container.querySelector('img')?.getAttribute('src')).toBe(sgdbArt.source))
  expect(vi.mocked(loadBackdropImage).mock.calls.map(([key]) => key)).toEqual([rootSaved, steam, sgdb])
  expect(
    vi
      .mocked(loadBackdropImage)
      .mock.calls.some(([key]) => key.provider === childSaved.provider && key.id === childSaved.id),
  ).toBe(false)
  expect(sgdbArt.dispose).not.toHaveBeenCalled()
  view.unmount()
  expect(sgdbArt.dispose).toHaveBeenCalledOnce()
  expect(listeners.size).toBe(0)
})
