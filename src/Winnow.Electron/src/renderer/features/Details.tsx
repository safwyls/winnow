import { useLayoutEffect, useRef, useState } from 'react'
import { useQueryClient, type QueryClient } from '@tanstack/react-query'
import { ArrowLeft, ArrowUpRight, Play, Download, RefreshCw } from 'lucide-react'
import {
  ApiError,
  dateLabel,
  hours,
  launchMessage,
  primaryAction,
  request,
  storeLabel,
  openExternal,
} from '../api/client'
import { useApiQuery, useCommand, useDetails, useLibrary, useWorkspace } from '../api/hooks'
import type { GameEntry, Mode, Workspace } from '../api/types'
import { JournalEditor, SessionRows } from './Journal'
import { Empty, Notice } from './shared'
import { Artwork } from '../components/Artwork'
import { useViewState } from '../viewState'
import { gameLinks, type GameLink } from '../api/gameLinks'
import { IgdbMatch, LibraryFacts, MetadataEditor, Screenshots, UpdateSignals } from './parity-details'
import { SteamReportedActivity } from './activity-steam'
import { ActivityTimeline } from './activity-timeline'
import { ListMembershipChoice } from './parity-list-membership'
import { ArtworkBrowser as ArtworkEditor } from './artwork-browser'
import { AddToListButton, orderedLists } from './parity-list-prompt'
import { DetailsRelationships } from './parity-details-identity'
import { AvalonDetailsLayout } from './details-layout'
import { noActionSentence } from '../../shared/game-actions'

const detailScrollPositions = new WeakMap<QueryClient, Map<string, number>>()
const editorSections = new Set(['Metadata', 'Game match', 'Artwork'])

export function GameLinks({ links }: { links: GameLink[] }) {
  const [error, setError] = useState<unknown>(null)
  async function open(url: string) {
    setError(null)
    try {
      await openExternal(url, { failure: 'inline' })
    } catch {
      setError(
        new Error('Could not open this link. Check that a browser or Steam is available, then try again.'),
      )
    }
  }
  if (!links.length) return null
  return (
    <section className="feature-panel game-links">
      <h2>Explore the game</h2>
      <nav aria-label="Game links">
        {links.map((link) => (
          <button key={link.url} title={link.url} onClick={() => void open(link.url)}>
            <span>
              {link.label}
              {link.detail && <small>{link.detail}</small>}
            </span>
            <ArrowUpRight size={16} aria-hidden="true" />
          </button>
        ))}
      </nav>
      <Notice error={error} />
    </section>
  )
}

export function EntryActions({
  entry,
  workspace,
  primaryOnly = false,
  managementOnly = false,
}: {
  entry: GameEntry
  workspace?: Workspace
  primaryOnly?: boolean
  managementOnly?: boolean
}) {
  const [pending, setPending] = useState(false)
  const [message, setMessage] = useState('')
  const [error, setError] = useState<unknown>(null)
  const [attempt, setAttempt] = useState<{ operationId: string; action: string } | null>(null)
  const action = managementOnly ? null : primaryAction(entry, workspace)
  const unavailable =
    entry.store === 'manual'
      ? 'Manual entries can be tracked here. Launch this game from its shortcut.'
      : noActionSentence(entry, workspace)
  async function dispatch(kind: string, reuse = false) {
    const operation = reuse && attempt ? attempt : { operationId: crypto.randomUUID(), action: kind }
    setAttempt(operation)
    setPending(true)
    setError(null)
    setMessage('')
    try {
      setMessage(
        launchMessage(
          await request<number>('actions.execute', { ownershipId: entry.ownershipId }, operation),
        ),
      )
      setAttempt(null)
    } catch (failure) {
      setError(failure)
      if (!(failure instanceof ApiError) || !failure.uncertain) setAttempt(null)
    } finally {
      setPending(false)
    }
  }
  return (
    <div className="entry-actions">
      {!primaryOnly && !managementOnly && (
        <div>
          <strong>{storeLabel(entry.store)}</strong>
          <span>
            {typeof entry.installed === 'boolean' && `${entry.installed ? 'Installed' : 'Not installed'} · `}
            {hours(entry.playtimeMinutes)}
          </span>
        </div>
      )}
      {action ? (
        <button
          className="primary-button"
          data-controller-play={primaryOnly || undefined}
          disabled={pending || Boolean(attempt)}
          onClick={() => void dispatch(action)}
        >
          {action === 'Play' ? <Play size={16} /> : <Download size={16} />}
          {pending ? 'Sending…' : action}
        </button>
      ) : (
        !managementOnly && unavailable && <p className="muted">{unavailable}</p>
      )}
      {!primaryOnly &&
        entry.store === 'steam' &&
        entry.installed &&
        workspace?.externalIds.some(
          (id) =>
            id.releaseId === entry.releaseId && id.provider === 'steam' && /^\d{1,10}$/.test(id.providerId),
        ) && (
          <button disabled={pending || Boolean(attempt)} onClick={() => void dispatch('Uninstall')}>
            Uninstall in Steam
          </button>
        )}
      {!primaryOnly &&
        (entry.store === 'epic' ||
          (entry.store === 'gog' &&
            workspace?.externalIds.some(
              (id) =>
                id.releaseId === entry.releaseId && id.provider === 'gog' && /^\d{1,12}$/.test(id.providerId),
            ))) && (
          <button disabled={pending || Boolean(attempt)} onClick={() => void dispatch('Manage')}>
            Manage in launcher
          </button>
        )}
      {!primaryOnly &&
        entry.store.startsWith('plugin:') &&
        workspace?.pluginActions[String(entry.ownershipId)]?.canOpenStore && (
          <button disabled={pending || Boolean(attempt)} onClick={() => void dispatch('OpenStore')}>
            Open store
          </button>
        )}
      <Notice error={error} message={message} />
      {attempt && !pending && (
        <div className="conflict-panel">
          <p>The response was interrupted. The launcher may already have received the action.</p>
          <button onClick={() => void dispatch(attempt.action, true)}>Check the same action again</button>
          <button
            onClick={() => {
              setAttempt(null)
              setError(null)
            }}
          >
            Dismiss
          </button>
        </div>
      )}
    </div>
  )
}

export function Details(props: {
  workId: number
  editText?(input: HTMLInputElement | HTMLTextAreaElement): void
  mode?: Mode
  onClose?: () => void
  presentation?: 'shared' | 'avalon'
}) {
  return props.presentation === 'avalon' ? (
    <AvalonDetailsLayout key={props.workId} {...props} />
  ) : (
    <SharedDetails {...props} />
  )
}

function SharedDetails({
  workId,
  mode = 'desktop',
  onClose,
}: {
  workId: number
  mode?: Mode
  onClose?: () => void
}) {
  const library = useLibrary()
  const workspace = useWorkspace()
  const client = useQueryClient()
  let detailPositions = detailScrollPositions.get(client)
  if (!detailPositions) {
    detailPositions = new Map()
    detailScrollPositions.set(client, detailPositions)
  }
  const details = useDetails(workId)
  const journalPreferences = useApiQuery<{ promptAfterPlay: boolean }>('journal.preferences.get')
  const game = library.data?.games.find((item) => item.workId === workId)
  const unreadRows = (workspace.data?.buckets ?? []) as {
    resolvedWorkId: number
    releaseId: number
    game: { unreadUpdateCount: number }
  }[]
  // Every ownership row repeats the resolved game's aggregate, including editions on other stores.
  const unreadCount =
    game && (game.playtimeMinutes > 0 || game.lastPlayedAt)
      ? Math.max(
          0,
          ...unreadRows
            .filter((row) => row.resolvedWorkId === workId)
            .map((row) => row.game.unreadUpdateCount),
        )
      : 0
  const [tab, setTab] = useViewState(`${mode}:details:${workId}:tab`, 'Overview')
  const [matchNote, setMatchNote] = useState('')
  const [previousSection, setPreviousSection] = useViewState(
    `${mode}:details:${workId}:previous-section`,
    'Overview',
  )
  const page = useRef<HTMLElement>(null),
    tabs = useRef<HTMLElement>(null)
  const viewport = useRef<HTMLElement | null>(null)
  const focusAfterReturn = useRef(false)
  const scrollKey = `${mode}:${workId}:${tab}`
  useLayoutEffect(() => {
    let node = page.current?.parentElement ?? null
    while (node && !/auto|scroll/.test(getComputedStyle(node).overflowY || getComputedStyle(node).overflow))
      node = node.parentElement
    viewport.current = node
    if (node) node.scrollTop = detailPositions!.get(scrollKey) ?? 0
    const remember = () => {
      if (node) detailPositions!.set(scrollKey, node.scrollTop)
    }
    node?.addEventListener('scroll', remember)
    if (focusAfterReturn.current) {
      tabs.current?.querySelector<HTMLButtonElement>('[aria-pressed="true"]')?.focus({ preventScroll: true })
      focusAfterReturn.current = false
    }
    return () => {
      remember()
      node?.removeEventListener('scroll', remember)
    }
  }, [scrollKey])
  function changeTab(next: string) {
    if (viewport.current) detailPositions!.set(scrollKey, viewport.current.scrollTop)
    if (editorSections.has(next) && !editorSections.has(tab)) setPreviousSection(tab)
    setTab(next)
  }
  function backToSection() {
    focusAfterReturn.current = true
    changeTab(previousSection)
  }
  const [editing, setEditing] = useViewState<number | null>(`${mode}:details:${workId}:editing`, null)
  const command = useCommand()
  const sessions = Object.values(details.data?.sessions ?? {})
    .flat()
    .sort((a, b) => b.startedAt.localeCompare(a.startedAt))
  return (
    <section
      ref={page}
      className={`feature-page details-page mode-${mode}`}
      onKeyDown={(event) => {
        if (
          event.key === 'Escape' &&
          editorSections.has(tab) &&
          !event.defaultPrevented &&
          !(event.target as HTMLElement).closest('[role="dialog"], [role="alertdialog"]')
        ) {
          event.preventDefault()
          event.stopPropagation()
          backToSection()
        }
      }}
    >
      {onClose && (
        <button className="back-button" onClick={onClose}>
          <ArrowLeft size={16} /> Back to your library
        </button>
      )}
      <Artwork workId={workId} hero eager className="detail-hero" />
      <header className="feature-heading">
        <div>
          <p className="eyebrow">{game?.bucket.replaceAll('_', ' ') ?? 'YOUR LIBRARY'}</p>
          <h1>{game?.title ?? 'Game details'}</h1>
          <p>{[game?.firstReleaseYear, game?.publisher].filter(Boolean).join(' · ')}</p>
          {unreadCount > 0 && (
            <button
              onClick={() => {
                focusAfterReturn.current = true
                changeTab('Updates')
              }}
            >
              {unreadCount} unread {unreadCount === 1 ? 'update' : 'updates'}
            </button>
          )}
        </div>
        <button
          disabled={command.isPending}
          onClick={() => command.mutate({ route: 'game.refetch', params: { workId } })}
        >
          <RefreshCw size={16} /> Refresh metadata
        </button>
      </header>
      <Notice
        error={details.error || library.error || workspace.error || command.error}
        message={matchNote}
      />
      <nav
        ref={tabs}
        className="tabs"
        aria-label="Game information"
        onKeyDown={(event) => {
          if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return
          const buttons = Array.from(event.currentTarget.querySelectorAll<HTMLButtonElement>('button'))
          const index = buttons.indexOf(event.target as HTMLButtonElement)
          if (index < 0) return
          event.preventDefault()
          const next =
            event.key === 'Home'
              ? 0
              : event.key === 'End'
                ? buttons.length - 1
                : (index + (event.key === 'ArrowRight' ? 1 : -1) + buttons.length) % buttons.length
          buttons[next]?.focus()
          buttons[next]?.click()
        }}
      >
        {['Overview', 'History', 'Updates', 'Journal', 'Library', 'Metadata', 'Game match', 'Artwork'].map(
          (name) => (
            <button
              key={name}
              data-controller-tab
              aria-pressed={name === tab}
              onClick={() => changeTab(name)}
            >
              {name}
            </button>
          ),
        )}
      </nav>
      {editorSections.has(tab) && (
        <button className="back-button" onClick={backToSection}>
          <ArrowLeft size={16} /> Back to {previousSection}
        </button>
      )}
      <div className="detail-body">
        <div className="detail-main">
          {tab === 'Overview' && (
            <>
              <section className="feature-panel">
                <h2>About the game</h2>
                <p className="game-summary">
                  {game?.summary ?? 'There is no description for this game yet.'}
                </p>
                <div className="stat-strip">
                  <div className="stat">
                    <strong>{game ? hours(game.playtimeMinutes) : '—'}</strong>
                    <span>Store playtime</span>
                  </div>
                  <div className="stat">
                    <strong>{game?.lastPlayedAt ? dateLabel(game.lastPlayedAt) : 'Not recorded'}</strong>
                    <span>Last played</span>
                  </div>
                </div>
              </section>
              {!!details.data?.ratings.filter((rating) => rating.hasFigure).length && (
                <section className="feature-panel">
                  <h2>What players say</h2>
                  {details.data.ratings
                    .filter((rating) => rating.hasFigure)
                    .map((rating) => (
                      <p key={rating.source}>
                        <strong>{rating.label ?? `${rating.score?.toFixed(0)} / 100`}</strong> ·{' '}
                        {rating.source} · {rating.ratingCount?.toLocaleString()} ratings
                      </p>
                    ))}
                </section>
              )}
              {!!details.data?.achievements.length && (
                <section className="feature-panel">
                  <h2>Achievements by edition</h2>
                  {details.data.achievements.map((item) => (
                    <p key={item.releaseId}>
                      {storeLabel(
                        game?.entries.find((entry) => entry.releaseId === item.releaseId)?.store ?? 'Edition',
                      )}
                      :{' '}
                      {item.hasKnownProgress && item.total > 0
                        ? `${item.unlocked} of ${item.total} unlocked`
                        : 'Progress unavailable'}
                      {item.isStale && ' · Last known reading'}
                    </p>
                  ))}
                </section>
              )}
              <Screenshots key={workId} details={details.data} />
              <UpdateSignals details={details.data} />
            </>
          )}
          {tab === 'History' && (
            <>
              {game && details.data && <ActivityTimeline game={game} details={details.data} mode={mode} />}
              <section className="feature-panel">
                <h2>Recorded sessions</h2>
                <SessionRows sessions={sessions} onEdit={setEditing} />
              </section>
              {game && <SteamReportedActivity games={[game]} mode={mode} />}
            </>
          )}
          {tab === 'Updates' && <UpdateSignals details={details.data} />}
          {tab === 'Journal' && (
            <section className="feature-panel">
              <h2>Your notes</h2>
              {!details.data?.journalEntries.length ? (
                <Empty>
                  {journalPreferences.data?.promptAfterPlay === false
                    ? 'Journal prompts are off. Turn them on in Display preferences after a game.'
                    : journalPreferences.data?.promptAfterPlay
                      ? 'No notes yet. After you play, Winnow will ask how it went.'
                      : 'No notes yet. Add one to a recorded session from History.'}
                </Empty>
              ) : (
                details.data.journalEntries.map((note) => (
                  <article className="timeline-entry" key={note.sessionId}>
                    <time>{dateLabel(note.sessionAt)}</time>
                    <div>
                      <blockquote>{note.note || 'A session to remember.'}</blockquote>
                      {note.rating && <p>{note.rating} / 5</p>}
                    </div>
                    <button onClick={() => setEditing(note.sessionId)}>Edit note</button>
                  </article>
                ))
              )}
            </section>
          )}
          {tab === 'Metadata' && <MetadataEditor key={workId} workId={workId} mode={mode} />}
          {tab === 'Game match' && (
            <IgdbMatch
              key={workId}
              workId={workId}
              title={game?.title ?? ''}
              onChanged={(note) => {
                setMatchNote(note)
                backToSection()
              }}
            />
          )}
          {tab === 'Library' && (
            <>
              <LibraryFacts game={game} details={details.data} />
              {game && <DetailsRelationships game={game} mode={mode} />}
            </>
          )}
          {tab === 'Artwork' && (
            <ArtworkEditor key={workId} workId={workId} mode={mode} title={game?.title} />
          )}
        </div>
        <aside className="detail-sidebar">
          <section className="feature-panel">
            <p className="eyebrow">YOUR EDITIONS</p>
            {game?.entries.map((entry) => (
              <EntryActions key={entry.ownershipId} entry={entry} workspace={workspace.data} />
            ))}
          </section>
          {game && workspace.data && (
            <GameLinks
              key={`links:${workId}`}
              links={gameLinks(game, workspace.data, details.data?.events)}
            />
          )}
          <ListMembership workId={workId} mode={mode} />
          <HideGame key={`visibility:${workId}`} workId={workId} onHidden={onClose} />
        </aside>
      </div>
      {editing != null && <JournalEditor sessionId={editing} onClose={() => setEditing(null)} />}
    </section>
  )
}

export function HideGame({
  workId,
  onHidden,
  compact = false,
}: {
  workId: number
  onHidden?: () => void
  compact?: boolean
}) {
  const [confirming, setConfirming] = useState(false)
  const command = useCommand()
  async function hide() {
    try {
      await command.mutateAsync({ route: 'hidden.put', body: { workIds: [workId], hidden: true } })
      setConfirming(false)
      onHidden?.()
    } catch {
      /* Keep the confirmation and render the returned error. */
    }
  }
  return (
    <section className={compact ? 'details-hide-action' : 'feature-panel'}>
      {!compact && <h2>Keep your library yours</h2>}
      {confirming ? (
        <div className="conflict-panel">
          <p>
            Hide this game from your library? Its history stays saved. You can restore it from Library tools.
          </p>
          <div className="form-actions">
            <button disabled={command.isPending} onClick={() => void hide()}>
              Hide game
            </button>
            <button disabled={command.isPending} onClick={() => setConfirming(false)}>
              Keep visible
            </button>
          </div>
        </div>
      ) : (
        <button onClick={() => setConfirming(true)}>Hide game…</button>
      )}
      <Notice error={command.error} />
    </section>
  )
}

export function ListMembership({ workId, mode }: { workId: number; mode: Mode }) {
  const library = useLibrary()
  const game = library.data?.games.find((item) => item.workId === workId)
  const lists = orderedLists(library.data?.lists ?? []).filter((list) => !list.isLive)
  return (
    <section className="feature-panel">
      <h2>In your lists</h2>
      {game && <AddToListButton games={[game]} mode={mode} origin="details" />}
      {!lists.length ? (
        <p className="muted">Create a list in Library tools.</p>
      ) : (
        game && lists.map((list) => <ListMembershipChoice key={list.id} list={list} game={game} />)
      )}
    </section>
  )
}

export { ArtworkBrowser as ArtworkEditor } from './artwork-browser'
