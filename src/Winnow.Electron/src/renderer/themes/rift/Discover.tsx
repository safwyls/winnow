import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import {
  ArrowRight,
  ArrowUpRight,
  ChevronLeft,
  ChevronRight,
  HardDrive,
  ListFilter,
  Search,
  Sparkles,
  X,
} from 'lucide-react'
import type { ThemeContext } from '../../../shared/theme'
import type { FeedItem, FeedShelf } from '../../api/types'
import { Artwork } from '../../components/Artwork'
import { PortalSurface } from '../../components/portal-effects'
import { DeckShuffle, type DeckPoses } from '../../components/deck-shuffle'
import { bucketLabel, Empty, hours, Impression } from '../../components/primitives'
import { libraryScroll, useViewState } from '../../viewState'
import { RiftCover } from './Cover'
import { storeNames, useJourney } from './journey'

export function RiftDiscover(context: ThemeContext) {
  const journey = useJourney(),
    queryClient = useQueryClient()
  const [lens, setLens] = useViewState('rift:discover:lens', 'recommended')
  const [selected, setSelected] = useViewState(`rift:${context.mode}:discover:selection`, 0)
  const [query, setQuery] = useViewState(`rift:${context.mode}:discover:query`, '')
  const [indexOpen, setIndexOpen] = useState(false)
  const [pending, setPending] = useState(false),
    [message, setMessage] = useState('')
  const [receipt, setReceipt] = useState<{ releaseId: number; kind: number; title: string } | null>(null)
  const portalRef = useRef<HTMLDivElement>(null)
  const browserRef = useRef<HTMLElement>(null)
  const deckRef = useRef<HTMLDivElement>(null)
  const shuffle = useRef(new DeckShuffle())
  const shuffleRequest = useRef<{ poses: DeckPoses; direction: number; workId: number; mode: string } | null>(
    null,
  )
  const scrollKey = `rift:discover:${context.mode}`
  useLayoutEffect(() => {
    if (browserRef.current) browserRef.current.scrollTop = libraryScroll.get(scrollKey) ?? 0
  }, [scrollKey, context.loading])
  const shelves = context.feed?.shelves ?? []
  const primary = shelves.find((shelf) => shelf.supportsFeedback) ?? shelves[0]
  const shelf = lens === 'recommended' ? primary : shelves.find((item) => item.id === lens)
  const candidates = useMemo(() => {
    if (lens === 'returning')
      return [...context.games]
        .filter((game) => game.lastPlayedAt)
        .sort((a, b) => (b.lastPlayedAt ?? '').localeCompare(a.lastPlayedAt ?? ''))
        .slice(0, 8)
    if (shelf) {
      const items = shelf.items
        .map((item) =>
          context.games.find((game) => game.entries.some((entry) => entry.releaseId === item.releaseId)),
        )
        .filter((game) => game !== undefined)
      return [...new Map(items.map((game) => [game.workId, game])).values()]
    }
    return context.games.slice(0, 24)
  }, [context.games, shelf, lens])
  const games = candidates.filter((game) =>
    game.title.toLocaleLowerCase().includes(query.trim().toLocaleLowerCase()),
  )
  const index = Math.max(
    0,
    games.findIndex((game) => game.workId === selected),
  )
  const game = games[index]
  useLayoutEffect(() => {
    const request = shuffleRequest.current
    shuffleRequest.current = null
    if (deckRef.current && request?.workId === game?.workId && request?.mode === context.mode)
      shuffle.current.play(deckRef.current, request.poses, request.direction, journey.reducedMotion)
    else shuffle.current.stop()
  }, [game?.workId, context.mode, journey.reducedMotion])
  useEffect(() => {
    const controller = shuffle.current
    const stop = () => controller.stop()
    document.addEventListener('visibilitychange', stop)
    window.addEventListener('resize', stop)
    return () => {
      controller.stop()
      document.removeEventListener('visibilitychange', stop)
      window.removeEventListener('resize', stop)
    }
  }, [])
  const picked = shelf?.items.find((item) =>
    game?.entries.some((entry) => entry.releaseId === item.releaseId),
  )
  const choose = (step: number) => {
    if (games.length > 1) {
      const next = games[(index + step + games.length) % games.length]
      if (deckRef.current)
        shuffleRequest.current = {
          poses: shuffle.current.capture(deckRef.current),
          direction: step,
          workId: next.workId,
          mode: context.mode,
        }
      const refocus = document.activeElement?.classList.contains('rift-deck-selected')
      setSelected(next.workId)
      if (refocus)
        requestAnimationFrame(() =>
          document.querySelector<HTMLButtonElement>('.rift-deck-selected')?.focus({ preventScroll: true }),
        )
    }
  }
  const selectLens = (value: string) => {
    setLens(value)
    setQuery('')
  }
  async function feedback(kind: number) {
    if (!picked || pending) return
    setPending(true)
    setMessage('')
    try {
      const result = await window.winnow.request<{ saved: boolean }>({
        route: 'feedFeedback',
        body: { releaseId: picked.releaseId, kind },
      })
      if (!result.ok || !result.data?.saved) throw Error(result.message ?? 'Feedback could not be saved.')
      setReceipt({ releaseId: picked.releaseId, kind, title: picked.title })
      await queryClient.invalidateQueries({ queryKey: ['api', 'feed.get'] })
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
    } finally {
      setPending(false)
    }
  }
  async function undo() {
    if (!receipt || pending) return
    setPending(true)
    setMessage('')
    try {
      const result = await window.winnow.request({
        route: 'feedRevoke',
        body: { releaseId: receipt.releaseId, kind: receipt.kind },
      })
      if (!result.ok) throw Error(result.message ?? 'Feedback could not be undone.')
      setReceipt(null)
      await queryClient.invalidateQueries({ queryKey: ['api', 'feed.get'] })
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
    } finally {
      setPending(false)
    }
  }
  if (context.loading)
    return (
      <div className="loading-state">
        <span className="loading-ring" />
        Opening your library…
      </div>
    )
  if (!context.games.length)
    return (
      <Empty title="A new world starts here.">
        <p>Connect a library in Settings, or add a game in Library.</p>
        <button onClick={() => context.setPage('settings')}>
          Open settings <ArrowRight size={16} />
        </button>
      </Empty>
    )
  return (
    <div className="rift-discover" data-index-open={indexOpen || undefined}>
      <aside className="rift-index" aria-label="Discovery index">
        <div className="rift-index-heading">
          <span className="eyebrow">YOUR COLLECTION</span>
          <h1>Worlds within.</h1>
        </div>
        <label className="rift-search">
          <Search size={15} />
          <input
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="Find a world…"
            aria-label="Search recommendations"
          />
        </label>
        <div className="rift-lenses">
          <button aria-pressed={lens === 'recommended'} onClick={() => selectLens('recommended')}>
            <Sparkles size={15} />
            Drawn to you <small>{primary?.items.length ?? candidates.length}</small>
          </button>
          <button aria-pressed={lens === 'returning'} onClick={() => selectLens('returning')}>
            Pick up the thread{' '}
            <small>{Math.min(8, context.games.filter((item) => item.lastPlayedAt).length)}</small>
          </button>
          {shelves
            .filter((item) => item.id !== primary?.id)
            .map((item) => (
              <button key={item.id} aria-pressed={lens === item.id} onClick={() => selectLens(item.id)}>
                {item.title}
                <small>{item.items.length}</small>
              </button>
            ))}
        </div>
        <span className="rift-index-label">{games.length} WORLDS TO EXPLORE</span>
        <div className="rift-index-scroll">
          {games.map((item) => (
            <button
              key={item.workId}
              className="rift-index-game"
              aria-pressed={item.workId === game?.workId}
              onClick={() => {
                setSelected(item.workId)
                setIndexOpen(false)
              }}
            >
              <Artwork workId={item.headerWorkId ?? item.workId} />
              <span>
                <strong>{item.title}</strong>
                <small>
                  {storeNames(item)} · {hours(item.playtimeMinutes)}
                </small>
              </span>
            </button>
          ))}
          {!games.length && <p className="muted">No matching worlds.</p>}
        </div>
        <button className="rift-index-foot" onClick={() => context.setPage('library')}>
          Explore every game <ArrowRight size={14} />
        </button>
      </aside>
      <section
        className="rift-browser"
        ref={browserRef}
        onScroll={(event) => libraryScroll.set(scrollKey, event.currentTarget.scrollTop)}
      >
        <header className="rift-browser-heading">
          <div>
            <span className="eyebrow">A DIFFERENT WAY IN</span>
            <h2>{lens === 'returning' ? 'Pick up the thread.' : (shelf?.title ?? 'Drawn to you.')}</h2>
          </div>
          <div className="rift-heading-tools">
            <span className="rift-position">
              {games.length
                ? `${String(index + 1).padStart(2, '0')} / ${String(games.length).padStart(2, '0')}`
                : '00 / 00'}
            </span>
            <button
              aria-label={indexOpen ? 'Close discovery index' : 'Open discovery index'}
              aria-expanded={indexOpen}
              onClick={() => setIndexOpen(!indexOpen)}
            >
              <ListFilter size={18} />
            </button>
          </div>
        </header>
        {context.feed?.failed && (
          <p className="status-banner" role="status">
            Recommendations are unavailable. Explore games from your collection.
          </p>
        )}
        {message && (
          <p className="error-banner" role="alert">
            {message}
          </p>
        )}
        {receipt && (
          <div className="rift-receipt" role="status">
            <span>
              {receipt.kind === 1
                ? `We’ll leave ${receipt.title} for later.`
                : `We won’t recommend ${receipt.title}.`}
            </span>
            <button onClick={() => void undo()} disabled={pending}>
              Undo
            </button>
            <button aria-label="Dismiss feedback notice" onClick={() => setReceipt(null)}>
              <X size={14} />
            </button>
          </div>
        )}
        {game ? (
          <div className="rift-world-stage">
            <div className="rift-deck-zone">
              <div className="rift-deck-orbit" aria-hidden="true" />
              <div
                ref={deckRef}
                className="rift-deck"
                onKeyDown={(event) => {
                  if (event.key === 'ArrowRight' || event.key === 'ArrowLeft') {
                    event.preventDefault()
                    choose(event.key === 'ArrowRight' ? 1 : -1)
                  }
                }}
              >
                {[
                  ...(games.length > 2
                    ? [{ game: games[(index - 1 + games.length) % games.length], slot: 'previous', step: -1 }]
                    : []),
                  ...(games.length > 1
                    ? [{ game: games[(index + 1) % games.length], slot: 'next', step: 1 }]
                    : []),
                  { game, slot: 'selected', step: 0 },
                ].map(({ game: cardGame, slot, step }) => (
                  <RiftCover
                    key={cardGame.workId}
                    game={cardGame}
                    data-deck-key={cardGame.workId}
                    data-deck-slot={slot}
                    className={`rift-deck-${slot}`}
                    aria-label={
                      step ? `${step < 0 ? 'Previous' : 'Next'} recommendation` : `View ${cardGame.title}`
                    }
                    tabIndex={step ? -1 : 0}
                    onClick={() => (step ? choose(step) : journey.open(cardGame.workId, portalRef.current))}
                  />
                ))}
              </div>
              <div className="rift-deck-caption">
                <strong>{game.title}</strong>
                <span className="eyebrow">ALREADY YOURS. STILL FULL OF POSSIBILITY.</span>
              </div>
              <div className="rift-deck-controls">
                <button
                  aria-label="Previous recommendation"
                  disabled={games.length < 2}
                  onClick={() => choose(-1)}
                >
                  <ChevronLeft size={18} />
                </button>
                <div className="rift-sequence" aria-hidden="true">
                  {games.slice(0, 16).map((item, i) => (
                    <i key={item.workId} data-active={index === i || undefined} />
                  ))}
                </div>
                <button
                  aria-label="Next recommendation"
                  disabled={games.length < 2}
                  onClick={() => choose(1)}
                >
                  <ChevronRight size={18} />
                </button>
              </div>
            </div>
            <div className="rift-portal-zone">
              <span className="rift-aperture-label">
                <i /> A WINDOW INTO THIS WORLD <span />
              </span>
              <div ref={portalRef} className="rift-world-portal">
                <PortalSurface
                  options={journey.options}
                  reducedMotion={journey.reducedMotion}
                  artwork={<Artwork workId={game.workId} hero eager />}
                >
                  <div className="rift-reading">
                    <span className="eyebrow">{bucketLabel(game.bucket)}</span>
                    <h2 className={game.title.length > 45 ? 'long-title' : ''}>{game.title}</h2>
                    <div className="rift-meta">
                      <span>{storeNames(game)}</span>
                      <span>{hours(game.playtimeMinutes)} played</span>
                      {game.firstReleaseYear && <span>{game.firstReleaseYear}</span>}
                    </div>
                    <p className="rift-reason">{picked?.reason ?? bucketLabel(game.bucket)}</p>
                    <p className="rift-description">
                      {game.summary ??
                        'There’s more to discover in your collection. View the game for its library record, artwork and activity.'}
                    </p>
                    <div className="rift-portal-actions">
                      <button
                        className="primary"
                        onClick={() => journey.open(game.workId, portalRef.current)}
                      >
                        View game <ArrowUpRight size={17} />
                      </button>
                      {game.entries.some((entry) => entry.installed) && (
                        <span>
                          <HardDrive size={13} />
                          Installed
                        </span>
                      )}
                    </div>
                  </div>
                </PortalSurface>
                {picked && shelf && <DiscoveryImpression item={picked} shelf={shelf} />}
              </div>
              <div className="rift-portal-foot">
                {picked && shelf?.supportsFeedback ? (
                  <>
                    <button disabled={pending} onClick={() => void feedback(1)}>
                      Not now
                    </button>
                    <button disabled={pending} onClick={() => void feedback(0)}>
                      Not interested
                    </button>
                  </>
                ) : (
                  <span>From the worlds you already own.</span>
                )}
              </div>
            </div>
          </div>
        ) : (
          <Empty title="Nothing through this lens.">
            <p>Try a different search or choose another collection.</p>
            <button
              onClick={() => {
                setQuery('')
                setLens('recommended')
              }}
            >
              Reset discovery
            </button>
          </Empty>
        )}
        <footer className="rift-browser-foot">
          <span>
            ← → Explore the deck <b>·</b> Enter to view
          </span>
          <button onClick={() => context.setPage('library')}>
            Your complete collection <ArrowRight size={15} />
          </button>
        </footer>
      </section>
    </div>
  )
}

function DiscoveryImpression({ item, shelf }: { item: FeedItem; shelf: FeedShelf }) {
  return (
    <div className="rift-impression">
      <Impression releaseId={item.releaseId} shelfId={shelf.id} enabled={shelf.supportsFeedback}>
        <span />
      </Impression>
    </div>
  )
}
