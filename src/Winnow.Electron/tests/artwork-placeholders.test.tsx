// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ArtworkAsset } from '../src/renderer/components/ArtworkAsset'
import { closeArtworkImages } from '../src/renderer/components/artwork-images'
import { ArtworkImage } from '../src/renderer/features/artwork-browser'
import { IgdbCandidateRow } from '../src/renderer/features/igdb-candidate'
import type { ReactNode } from 'react'

const clients: QueryClient[] = []
function mount(children: ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  clients.push(client)
  return render(<QueryClientProvider client={client}>{children}</QueryClientProvider>)
}
function bridge(artwork: ReturnType<typeof vi.fn>) {
  Object.defineProperty(window, 'winnow', { value: { artwork }, configurable: true })
}
afterEach(async () => {
  cleanup()
  for (const client of clients.splice(0)) {
    await closeArtworkImages(client)
    client.clear()
  }
  vi.restoreAllMocks()
})

describe('composition artwork fallbacks', () => {
  it('distinguishes a screenshot still loading from a confirmed unavailable screenshot', async () => {
    let resolve!: (value: null) => void
    bridge(
      vi.fn(
        () =>
          new Promise<null>((done) => {
            resolve = done
          }),
      ),
    )
    const { container } = mount(
      <ArtworkAsset asset={{ provider: 'igdb-shot', id: 'missing' }} width={400} alt="Game screenshot" />,
    )
    expect(screen.getByText('Loading screenshot…')).toBeTruthy()
    expect(container.querySelector('.artwork-placeholder-image')).toBeNull()
    await act(async () => resolve(null))
    await waitFor(() => expect(screen.getByText('Screenshot unavailable')).toBeTruthy())
    expect(container.querySelector('.artwork-placeholder-image')?.getAttribute('aria-hidden')).toBe('true')
    expect(container.querySelector('img')).toBeNull()
  })

  it('replaces a broken artwork-browser image without removing its useful unavailable label', async () => {
    bridge(vi.fn().mockResolvedValue('data:image/png;base64,bm90LXBpeGVscw=='))
    const { container } = mount(<ArtworkImage imageKey={{ provider: 'igdb', id: 'invalid' }} preview />)
    const image = await screen.findByRole('img', { name: 'Selected artwork preview' })
    fireEvent.error(image)
    expect(screen.getByText('Preview unavailable')).toBeTruthy()
    expect(container.querySelector('.artwork-placeholder-image')).not.toBeNull()
    expect(container.querySelector('img')).toBeNull()
  })

  it.each([null, 'data:image/png;base64,bm90LXBpeGVscw=='])(
    'keeps match names and actions when an IGDB cover is absent or fails (%s)',
    async (source) => {
      bridge(vi.fn().mockResolvedValue(source))
      const { container } = mount(
        <IgdbCandidateRow
          candidate={{
            igdbId: 1,
            name: 'A distant island',
            coverUrl: 'https://images.igdb.com/image/upload/t_cover_big/cover.jpg',
            platforms: ['PC'],
          }}
        >
          <button>Use this game</button>
        </IgdbCandidateRow>,
      )
      if (source) {
        await waitFor(() => expect(container.querySelector('img')).not.toBeNull())
        fireEvent.error(container.querySelector('img')!)
      }
      await waitFor(() => expect(container.querySelector('.artwork-placeholder-image')).not.toBeNull())
      expect(screen.getByRole('article', { name: 'A distant island' })).toBeTruthy()
      expect(screen.getByRole('button', { name: 'Use this game' })).toBeTruthy()
      expect(container.querySelector('.igdb-candidate-cover')?.getAttribute('aria-hidden')).toBe('true')
      expect(container.querySelector('img')).toBeNull()
    },
  )
})
