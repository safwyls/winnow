import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { ImageOff } from 'lucide-react'
interface ArtState {
  current: { previewKey: { provider: string; id: string } } | null
  revision: string
}

const imageFreshness = 120_000
const cacheLifetime = 300_000
let imageRequest = 0
interface ImageAsset {
  source: string
  request: number
  queryKey: readonly ['artwork-image', string, string, number, string]
}
export function Artwork({
  workId,
  hero = false,
  className = '',
  eager = false,
}: {
  workId: number
  hero?: boolean
  className?: string
  eager?: boolean
}) {
  const client = useQueryClient()
  const view = `${workId}:${hero}`
  const [loaded, setLoaded] = useState<{ view: string; source: string } | null>(null)
  const [failure, setFailure] = useState<{ view: string; request: number } | null>(null)
  const { data, isPending, isError } = useQuery({
    queryKey: ['artwork', workId, hero],
    staleTime: imageFreshness,
    gcTime: cacheLifetime,
    retry: false,
    queryFn: async () => {
      const state = await window.winnow.request<ArtState>({
        route: 'artworkState',
        params: { workId, slot: hero ? 'Hero' : 'Cover' },
      })
      if (!state.ok) throw new Error('Artwork state could not be loaded.')
      const key = state.data?.current?.previewKey
      if (!key) return null
      const width = hero ? 1920 : 600
      const queryKey = ['artwork-image', key.provider, key.id, width, state.data!.revision] as const
      // State must stay live, but unchanged selections can share encoded bytes across cards.
      // The revision describes the selection, not file content, so reuse also has a short TTL.
      return client.fetchQuery<ImageAsset>({
        queryKey,
        staleTime: imageFreshness,
        gcTime: cacheLifetime,
        retry: false,
        queryFn: async () => {
          const source = await window.winnow.artwork(key.provider, key.id, width)
          if (!source) throw new Error('Artwork image could not be loaded.')
          return { source, request: ++imageRequest, queryKey }
        },
      })
    },
  })
  const failed = Boolean(data && failure?.view === view && failure.request === data.request)
  const ready = Boolean(data && loaded?.view === view && loaded.source === data.source)
  const loading = isPending || Boolean(data && !ready && !failed)
  return (
    <div
      className={`artwork ${className}`}
      data-loading={loading || undefined}
      data-state={loading ? 'loading' : failed || (!data && isError) ? 'error' : data ? 'ready' : 'missing'}
      aria-hidden="true"
    >
      {data && !failed && (
        <img
          key={view}
          src={data.source}
          alt=""
          loading={eager ? 'eager' : 'lazy'}
          className={ready ? 'art-ready' : ''}
          onLoad={() => setLoaded({ view, source: data.source })}
          onError={() => {
            setFailure({ view, request: data.request })
            setLoaded(null)
            // A later state refresh must be able to retry even if the selection is unchanged.
            client.removeQueries({ queryKey: data.queryKey, exact: true })
          }}
        />
      )}
      {loading ? (
        <div className={`art-loading${hero ? ' art-loading-hero' : ''}`}>
          {hero && <span className="art-loading-orbit" />}
        </div>
      ) : (
        (!data || failed) && (
          <div className="art-placeholder">
            <ImageOff size={24} strokeWidth={1} />
            <span>Artwork unavailable</span>
          </div>
        )
      )}
    </div>
  )
}
