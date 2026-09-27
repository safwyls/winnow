import { useQuery } from '@tanstack/react-query'
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
  const { data } = useQuery({
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
  return (
    <div className={`artwork ${className}`} aria-hidden="true">
      {data ? (
        <img src={data} alt="" loading={eager ? 'eager' : 'lazy'} />
      ) : (
        <div className="art-placeholder">
          <ImageOff size={24} strokeWidth={1} />
          <span>Winnow</span>
        </div>
      )}
    </div>
  )
}
