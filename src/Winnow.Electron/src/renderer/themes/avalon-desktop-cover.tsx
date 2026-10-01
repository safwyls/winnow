import {
  createContext,
  useEffect,
  useId,
  useRef,
  useState,
  type CSSProperties,
  type KeyboardEvent,
  type MouseEvent,
  type ReactNode,
  type HTMLAttributes,
} from 'react'
import type { ThemeContext } from '../../shared/theme'
import { primaryAction, primaryEntry } from '../../shared/game-actions'
import type { LibraryGame, Workspace } from '../api/types'
import type { ExpansionMark } from '../features/parity-library-projection'
import { storeLabel } from '../api/client'
import { libraryBucketLabel, libraryIdle, libraryPlaytime } from './avalon-library-chrome'
import { ownershipDescription, ownershipStores } from './avalon-store-marks'
import { useAvalonPreview } from './avalon-preview'
import { FeedReason } from './avalon-feed'
import { unreadLabel } from './avalon-unread'
import './avalon-desktop-cover.css'

// The shell already observes this snapshot; tiles share it without another query per cover.
export const AvalonCoverWorkspace = createContext<Workspace | undefined>(undefined)

export type AvalonCoverProps = {
  context: ThemeContext
  game: LibraryGame
  reason?: string
  selected?: boolean
  onFocus?: () => void
  onKeyDown?: (event: KeyboardEvent<HTMLButtonElement>) => void
  onClick?: (event: MouseEvent<HTMLButtonElement>) => void
  onContextMenu?: (event: MouseEvent<HTMLButtonElement>) => void
  feed?: { actions: ReactNode; receipt?: ReactNode; countdown?: ReactNode }
  expansion?: ExpansionMark
}

export function AvalonDesktopCover({
  context,
  game,
  reason,
  selected,
  onFocus,
  onKeyDown,
  onClick,
  onContextMenu,
  expansion,
  patched,
  unreadCount,
  style,
  workspace,
  feed,
}: AvalonCoverProps & {
  patched: boolean
  unreadCount: number
  style: CSSProperties
  workspace?: Workspace
}) {
  const { Artwork } = context.components
  const reasonId = useId()
  const hover = useAvalonPreview(context, game, feed ? undefined : reason)
  const [pointer, setPointer] = useState(false)
  const [keyboard, setKeyboard] = useState(false)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState('')
  const busy = useRef(false)
  const enteredFeed = useRef(false)
  const alive = useRef(true)
  const pointerFocus = useRef(false)
  useEffect(() => {
    alive.current = true
    const blur = () => setPointer(false)
    window.addEventListener('blur', blur)
    return () => {
      alive.current = false
      window.removeEventListener('blur', blur)
    }
  }, [])
  const entry = primaryEntry(game.entries, workspace)
  const action = entry ? primaryAction(entry, workspace) : null
  const stores = ownershipStores(game)
  const revealed = pointer || keyboard
  const stat =
    game.playtimeMinutes <= 0
      ? 'never opened'
      : `${libraryPlaytime(game.playtimeMinutes)}${game.lastPlayedAt ? ` · idle ${libraryIdle(game.lastPlayedAt)}` : ''}`
  async function launch() {
    if (!entry || !action || busy.current) return
    busy.current = true
    setPending(true)
    setError('')
    hover.close()
    try {
      await context.actions.launch(entry.ownershipId)
    } catch {
      if (alive.current) setError(`${action} could not finish. Try again.`)
    } finally {
      busy.current = false
      if (alive.current) setPending(false)
    }
  }
  const interaction: HTMLAttributes<HTMLDivElement> = {
    // Fresh motion prevents a recycled card appearing hovered under a stationary pointer.
    onMouseMove: (event) => {
      if (!pointer) setPointer(true)
      if (feed && !enteredFeed.current) {
        enteredFeed.current = true
        const button = event.currentTarget.querySelector<HTMLButtonElement>('.avalon-cover')
        if (button && !feed.receipt) hover.open(button)
      }
    },
    onMouseLeave: () => {
      enteredFeed.current = false
      setPointer(false)
      hover.close()
    },
    onPointerDownCapture: () => {
      pointerFocus.current = true
      setKeyboard(false)
    },
    onKeyDownCapture: () => {
      pointerFocus.current = false
      setKeyboard(true)
    },
    onFocusCapture: () => {
      if (!pointerFocus.current) setKeyboard(true)
      onFocus?.()
    },
    onBlurCapture: (event) => {
      if (!(event.relatedTarget instanceof Node) || !event.currentTarget.contains(event.relatedTarget)) {
        setKeyboard(false)
        pointerFocus.current = false
        hover.close()
      }
    },
    onClickCapture: () => hover.close(),
  }
  const face = (
    <div
      className="avalon-desktop-cover"
      data-feed={!!feed || undefined}
      data-receipt={!!feed?.receipt || undefined}
      data-revealed={revealed || undefined}
      data-pointer={pointer || undefined}
      data-selected={selected || undefined}
      {...(!feed ? interaction : {})}
    >
      <button
        className="avalon-cover"
        style={style}
        data-avalon-game={game.workId}
        data-work-id={game.workId}
        data-selected={selected || undefined}
        aria-label={`View ${game.title}${unreadLabel(patched, unreadCount)}${ownershipDescription(game)}${expansion ? `. ${expansion.text}` : ''}`}
        aria-description={reason ?? libraryBucketLabel(game.bucket)}
        aria-describedby={feed && reason ? reasonId : undefined}
        onMouseEnter={(event) => {
          if (!feed) hover.open(event.currentTarget)
        }}
        onMouseLeave={feed ? undefined : hover.close}
        onClick={(event) => {
          hover.close()
          if (onClick) onClick(event)
          else context.openGame(game.workId)
        }}
        onContextMenu={onContextMenu}
        onKeyDown={onKeyDown}
      >
        <Artwork workId={game.headerWorkId ?? game.workId} />
        <span className="avalon-cover-fallback" aria-hidden="true">
          {game.title}
        </span>
        {patched && <span className="avalon-unread" title="Patched since you played" />}
        {stores.length > 1 && (
          <span
            className="avalon-store-initials"
            aria-hidden="true"
            title={stores.map((store) => store.label).join(', ')}
          >
            {stores.map((store) => (
              <span key={store.key}>{store.initial}</span>
            ))}
          </span>
        )}
        {expansion && (
          <span className="avalon-expansion-mark" aria-hidden="true" title={expansion.text}>
            +{expansion.count}
          </span>
        )}
        <span className="avalon-cover-caption avalon-tile-scrim" aria-hidden="true">
          <strong aria-hidden="true">{game.title}</strong>
          <span className="avalon-tile-stat" title={reason ?? stat}>
            {reason && !feed ? <FeedReason reason={reason} /> : stat}
          </span>
          <span className="avalon-store-chips avalon-tile-stores" aria-hidden="true">
            {stores.map((store) => (
              <span key={store.key} title={store.label}>
                {store.badge}
              </span>
            ))}
          </span>
        </span>
      </button>
      {action && (
        <button
          className="avalon-tile-primary"
          aria-label={action}
          aria-hidden={!revealed}
          title={`${action === 'Play' ? 'Launch' : 'Install'} through ${storeLabel(entry!.store)}`}
          tabIndex={revealed ? 0 : -1}
          aria-disabled={pending || undefined}
          onClick={() => void launch()}
        >
          <svg
            viewBox="0 0 16 16"
            width="16"
            height="16"
            aria-hidden="true"
            data-glyph={action.toLowerCase()}
          >
            <path
              fill="currentColor"
              d={action === 'Play' ? 'M4 2L14 8L4 14Z' : 'M7 2L9 2L9 9L12 6L14 8L8 14L2 8L4 6L7 9Z'}
            />
          </svg>
        </button>
      )}
      <button
        className="avalon-tile-details"
        aria-label="Details"
        aria-hidden={!revealed}
        title="Full details"
        tabIndex={revealed ? 0 : -1}
        onClick={() => {
          hover.close()
          context.openGame(game.workId)
        }}
      >
        <svg viewBox="0 0 40 40" width="40" height="40" aria-hidden="true">
          <path className="avalon-tile-fold" d="M0 0H40V40Z" />
          <path d="M0 0L40 40" stroke="var(--line)" />
          <g transform="translate(21 5)" stroke="var(--text)" strokeWidth="1.3" fill="none">
            <path d="M2 2H10V10H2ZM4 4H8M4 6H8M4 8H7" />
          </g>
          <path className="avalon-tile-fold-focus" d="M1 1H39V39Z" fill="none" strokeWidth="2" />
        </svg>
      </button>
      {feed && !feed.receipt && (
        <div className="avalon-feed-card-actions" aria-hidden={!revealed}>
          {feed.actions}
        </div>
      )}
      {feed?.receipt}
      {feed?.countdown}
      <span className="avalon-tile-ring" aria-hidden="true" />
      {error && (
        <span className="avalon-tile-error" role="alert">
          {error}
        </span>
      )}
    </div>
  )
  return (
    <>
      {feed ? (
        <div className="avalon-feed-card" {...interaction}>
          {face}
          <div
            className="avalon-feed-card-caption"
            role="presentation"
            onClick={() => {
              hover.close()
              context.openGame(game.workId)
            }}
          >
            <strong title={game.title}>{game.title}</strong>
            <span id={reasonId} title={reason}>
              <FeedReason reason={reason ?? ''} />
            </span>
          </div>
        </div>
      ) : (
        face
      )}
      {hover.preview}
    </>
  )
}
