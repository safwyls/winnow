import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { ImageOff } from 'lucide-react'
interface ArtState {
  current: { previewKey: { provider: string; id: string } } | null
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
  const [loadedSource, setLoadedSource] = useState<string | null>(null)
  const [failedSource, setFailedSource] = useState<string | null>(null)
  const { data, isPending } = useQuery({
    queryKey: ['artwork', workId, hero],
    staleTime: 120_000,
    queryFn: async () => {
      const state = await window.winnow.request<ArtState>({
        route: 'artworkState',
        params: { workId, slot: hero ? 'Hero' : 'Cover' },
      })
      const key = state.data?.current?.previewKey
      if (!key) return null
      return window.winnow.artwork(key.provider, key.id, hero ? 1920 : 600)
    },
  })
  const loading = isPending || Boolean(data && loadedSource !== data && failedSource !== data)
  return (
    <div className={`artwork ${className}`} data-loading={loading || undefined} aria-hidden="true">
      {data && failedSource !== data && (
        <img
          src={data}
          alt=""
          loading={eager ? 'eager' : 'lazy'}
          className={loadedSource === data ? 'art-ready' : ''}
          onLoad={() => setLoadedSource(data)}
          onError={() => setFailedSource(data)}
        />
      )}
      {loading ? (
        <div className="art-loading">
          <span className="art-loading-orbit" />
        </div>
      ) : (
        (!data || failedSource === data) && (
          <div className="art-placeholder">
            <ImageOff size={24} strokeWidth={1} />
            <span>Artwork unavailable</span>
          </div>
        )
      )}
    </div>
  )
}
