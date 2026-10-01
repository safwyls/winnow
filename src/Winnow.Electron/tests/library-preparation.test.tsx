// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useLibrary } from '../src/renderer/api/hooks'
import { librarySchema, prepareLibrary } from '../src/renderer/api/prepare-library'
import type { LibraryResponse } from '../src/renderer/api/types'
import { useLibraryProjection } from '../src/renderer/features/parity-library-projection'
import type { WinnowBridge } from '../src/shared/bridge'

const key = ['api', 'library.get']
const game = (index: number) => ({
  workId: index + 1,
  title: `Game ${index + 1}`,
  bucket: 'never_played',
  playtimeMinutes: 0,
  entries: [
    {
      ownershipId: index + 1,
      releaseId: index + 1,
      workId: index + 1,
      title: `Game ${index + 1}`,
      store: 'manual',
      installed: false,
      playtimeMinutes: 0,
    },
  ],
})
afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})

function LibraryProbe({ mode, input }: { mode: 'desktop' | 'fullscreen'; input(): void }) {
  const library = useLibrary()
  const projection = useLibraryProjection(library.data?.games)
  return (
    <section aria-label={`${mode} library`}>
      <input aria-label="Responsive input" onInput={input} />
      <output data-testid="all">{library.data?.games.length ?? 0}</output>
      <output data-testid="visible">{projection.games.length}</output>
      <output data-testid="total">{library.data?.games.length ?? 0}</output>
    </section>
  )
}

describe.each(['desktop', 'fullscreen'] as const)('%s library response preparation', (mode) => {
  it.each(['complete', 'cancel', 'dispose', 'replace'] as const)(
    'processes input during 512-game preparation and publishes only the winning complete snapshot: %s',
    async (action) => {
      const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
      let replacement = false,
        observed = false,
        atInput = -1
      let resolveInput!: () => void
      const inputFinished = new Promise<void>((resolve) => {
        resolveInput = resolve
      })
      const games = Array.from({ length: 512 }, (_, index) => game(index))
      const first = games[0]
      Object.defineProperty(games, 0, {
        get() {
          if (!observed) {
            observed = true
            // The source fixture posts input on first enumeration; the real preparation must yield to it.
            setTimeout(
              () =>
                fireEvent.input(screen.getByRole('textbox', { name: 'Responsive input' }), {
                  target: { value: 'still responsive' },
                }),
              0,
            )
          }
          return first
        },
      })
      const read = vi.fn(async ({ route }: { route: string }) => ({
        ok: true,
        status: 200,
        data:
          route === 'library.get'
            ? { games: replacement ? [first] : games, lists: [] }
            : route === 'library.workspace'
              ? { works: [], externalIds: [], pluginActions: {}, epicLaunchKeys: {} }
              : [],
      }))
      window.winnow = { request: read, cancelRequest: vi.fn(async () => true) } as unknown as WinnowBridge
      const publications: LibraryResponse[] = []
      const unsubscribe = client.getQueryCache().subscribe((event) => {
        if (
          event.type === 'updated' &&
          event.query.queryKey[1] === 'library.get' &&
          event.action.type === 'success'
        )
          publications.push(event.query.state.data as LibraryResponse)
      })
      const view = render(
        <QueryClientProvider client={client}>
          <LibraryProbe
            mode={mode}
            input={() => {
              atInput = client.getQueryData<LibraryResponse>(key)?.games.length ?? 0
              expect(screen.getByTestId('all').textContent).toBe('0')
              expect(screen.getByTestId('visible').textContent).toBe('0')
              expect(screen.getByTestId('total').textContent).toBe('0')
              if (action === 'dispose') {
                view.unmount()
                resolveInput()
              } else if (action === 'cancel') void client.cancelQueries({ queryKey: key }).then(resolveInput)
              else if (action === 'replace') {
                replacement = true
                void client
                  .cancelQueries({ queryKey: key })
                  .then(() => client.refetchQueries({ queryKey: key }))
                  .then(resolveInput)
              } else resolveInput()
            }}
          />
        </QueryClientProvider>,
      )
      await act(async () => {
        await inputFinished
      })
      await waitFor(() => expect(client.getQueryState(key)?.fetchStatus).toBe('idle'))
      const expected = action === 'complete' ? 512 : action === 'replace' ? 1 : 0
      expect(atInput).toBe(0)
      expect(client.getQueryData<LibraryResponse>(key)?.games.length ?? 0).toBe(expected)
      expect(publications.map((snapshot) => snapshot.games.length)).toEqual(expected ? [expected] : [])
      if (action !== 'dispose') {
        for (const count of ['all', 'visible', 'total'])
          await waitFor(() => expect(screen.getByTestId(count).textContent).toBe(String(expected)))
        expect((screen.getByRole('textbox', { name: 'Responsive input' }) as HTMLInputElement).value).toBe(
          'still responsive',
        )
      }
      expect(read.mock.calls.filter(([request]) => request.route === 'library.get')).toHaveLength(
        action === 'replace' ? 2 : 1,
      )
      unsubscribe()
      view.unmount()
      client.clear()
    },
  )
})

it('keeps small library passthrough data and the public schema diagnostics while large responses yield', async () => {
  const source = {
    games: [
      {
        ...game(0),
        summary: 'Metadata survives',
        entries: [{ ...game(0).entries[0], customField: 'entry metadata' }],
      },
    ],
    lists: [],
    revision: 'top-level metadata',
  }
  expect(await prepareLibrary(source)).toEqual(librarySchema.parse(source))
  const invalid = {
    games: Array.from({ length: 512 }, (_, index) =>
      index === 256 ? { ...game(index), workId: 'invalid' } : game(index),
    ),
    lists: [{ id: 1, name: 'List', isLive: false, releaseIds: [1], revision: 5 }],
  }
  const original = librarySchema.safeParse(invalid)
  expect(original.success).toBe(false)
  await expect(prepareLibrary(invalid)).rejects.toMatchObject({ issues: original.error!.issues })
  for (const malformed of [null, {}, { games: [], lists: 'invalid' }]) {
    const original = librarySchema.safeParse(malformed)
    await expect(prepareLibrary(malformed)).rejects.toMatchObject({ issues: original.error!.issues })
  }
})
