import type { ReactElement } from 'react'
import { useQuery } from '@tanstack/react-query'
import './igdb-candidate.css'

export interface IgdbCandidate {
  igdbId: number
  name: string
  coverUrl?: string | null
  firstReleaseYear?: number | null
  platforms: string[]
}

export function IgdbCandidateRow({
  candidate,
  idMatch = false,
  children,
}: {
  candidate: IgdbCandidate
  idMatch?: boolean
  children: ReactElement
}) {
  const platforms = candidate.platforms.join(', ')
  return (
    <article className="igdb-candidate-row" aria-label={candidate.name}>
      <IgdbCover url={candidate.coverUrl} />
      <div className="igdb-candidate-text">
        <h3 className="igdb-candidate-name" title={candidate.name}>
          {idMatch && <span className="store-chip">ID MATCH</span>} {candidate.name}
        </h3>
        {(candidate.firstReleaseYear || platforms) && (
          <p className="igdb-candidate-detail">
            <span className="igdb-candidate-year">
              {candidate.firstReleaseYear ? `${candidate.firstReleaseYear}${platforms ? ' · ' : ''}` : ''}
            </span>
            <span className="igdb-candidate-platforms" title={platforms || undefined}>
              {platforms}
            </span>
          </p>
        )}
      </div>
      {children}
    </article>
  )
}

export function IgdbCover({ url }: { url?: string | null }) {
  const id = url?.match(/\/([^/.]+)\.[a-z]+(?:\?.*)?$/i)?.[1]
  const art = useQuery({
    queryKey: ['artwork', 'igdb', id, 100],
    queryFn: () => window.winnow.artwork('igdb', id!, 100),
    enabled: Boolean(id),
    retry: false,
    staleTime: 120_000,
  })
  return (
    <span className="igdb-candidate-cover" aria-hidden="true">
      {art.data ? <img src={art.data} width={34} height={51} alt="" /> : '—'}
    </span>
  )
}
