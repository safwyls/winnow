import {
  useEffect,
  useId,
  useLayoutEffect,
  useRef,
  useState,
  type KeyboardEvent,
  type ReactNode,
} from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { ArrowLeft, ChevronDown, ListPlus, X } from 'lucide-react'
import { useApiQuery, useDetails, useLibrary, useWorkspace } from '../api/hooks'
import { dateLabel, hours, storeLabel } from '../api/client'
import { primaryEntry } from '../../shared/game-actions'
import type { GameDetails, LibraryGame, Mode, Workspace } from '../api/types'
import { Artwork } from '../components/Artwork'
import { ArtworkEditor, EntryActions, GameLinks, HideGame, ListMembership } from './Details'
import { IgdbMatch, LibraryFacts, MetadataEditor, Screenshots, UpdateSignals } from './parity-details'
import { ReceptionLine, MetadataRefresh, LifecycleEvidence } from './details-presentation'
import { DetailsRelationships } from './parity-details-identity'
import { AddToListButton } from './parity-list-prompt'
import { ActivityTimeline } from './activity-timeline'
import { SteamReportedActivity } from './activity-steam'
import { JournalEditor, SessionRows } from './Journal'
import { Empty, Notice } from './shared'
import { gameLinks, type GameLink } from '../api/gameLinks'
import { useViewState } from '../viewState'
import { InstallFolderButton } from './install-folder'
import './details-layout.css'

const desktopSections = ['Overview', 'Activity', 'Updates', 'Journal', 'Library'] as const
const televisionSections = ['Overview', 'Updates', 'Journal', 'Library'] as const
type Reading = 'About' | 'History'
type Tool = 'Metadata' | 'Game match' | 'Artwork'
const tools: Tool[] = ['Game match', 'Metadata', 'Artwork']

export function descriptionPreview(text: string, limit = 360) {
  if (text.length <= limit) return text
  const cut = text.slice(0, limit),
    word = cut.lastIndexOf(' ')
  return `${cut.slice(0, word > limit / 2 ? word : limit).trimEnd()}…`
}

export function detailsGame(workId: number, games: LibraryGame[], workspace?: Workspace) {
  const exact = games.find((game) => game.workId === workId)
  if (exact) return exact
  const resolved = ((workspace?.buckets ?? []) as { workId: number; resolvedWorkId: number }[]).find(
    (row) => row.workId === workId,
  )?.resolvedWorkId
  return (
    games.find((game) => game.workId === resolved) ??
    games.find((game) => game.entries.some((entry) => entry.workId === workId))
  )
}

export function AvalonDetailsLayout({
  workId,
  mode = 'desktop',
  onClose,
}: {
  workId: number
  mode?: Mode
  onClose?: () => void
}) {
  const fullscreen = mode === 'fullscreen',
    id = useId()
  const library = useLibrary(),
    workspace = useWorkspace(),
    details = useDetails(workId)
  const preferences = useApiQuery<{ promptAfterPlay: boolean }>('journal.preferences.get')
  const game = detailsGame(workId, library.data?.games ?? [], workspace.data)
  const [section, setSection] = useState<string>('Overview')
  const [reading, setReading] = useState<Reading | null>(null),
    [tool, setTool] = useState<Tool | null>(null)
  const [expanded, setExpanded] = useState(false)
  const [moreOpen, setMoreOpen] = useState(false)
  const [editing, setEditing] = useViewState<number | null>(`${mode}:details:${workId}:editing`, null)
  const body = useRef<HTMLDivElement>(null),
    root = useRef<HTMLDivElement>(null),
    more = useRef<HTMLButtonElement>(null)
  const positions = useRef(new Map<string, number>()),
    returnFocus = useRef<HTMLElement | null>(null)
  const opener = useRef(document.activeElement instanceof HTMLElement ? document.activeElement : null)
  const region = tool ?? reading ?? section
  const sections = fullscreen ? televisionSections : desktopSections
  const buckets = (workspace.data?.buckets ?? []) as {
    resolvedWorkId: number
    game: { unreadUpdateCount: number }
  }[]
  const unread =
    game && (game.playtimeMinutes > 0 || game.lastPlayedAt)
      ? Math.max(
          0,
          ...buckets.filter((row) => row.resolvedWorkId === workId).map((row) => row.game.unreadUpdateCount),
        )
      : 0
  const primary = primaryEntry(game?.entries ?? [], workspace.data) ?? game?.entries[0]
  const ownerships = (details.data?.ownerships ?? []) as { id: number; installPath?: string | null }[]
  const links = game && workspace.data ? gameLinks(game, workspace.data, details.data?.events) : []
  const relatedIds = new Set([workId, ...(game?.entries.map((entry) => entry.workId) ?? [])])
  const hasRelationships = (
    (workspace.data?.identityLinks ?? []) as {
      retractedAt?: string | null
      parentWorkId: number
      childWorkId: number
    }[]
  ).some(
    (link) => !link.retractedAt && (relatedIds.has(link.parentWorkId) || relatedIds.has(link.childWorkId)),
  )
  const sessions = Object.values(details.data?.sessions ?? {})
    .flat()
    .sort((a, b) => b.startedAt.localeCompare(a.startedAt))
  const notes = [...(details.data?.journalEntries ?? [])].sort((a, b) =>
    b.sessionAt.localeCompare(a.sessionAt),
  )
  const summary = game?.summary?.trim() || 'No description yet. Metadata fills in automatically.'
  const storeLine = [...new Set(game?.entries.map((entry) => storeLabel(entry.store)) ?? [])].join(' · ')
  useLayoutEffect(() => {
    const node = body.current
    if (!node) return
    node.scrollTop = positions.current.get(region) ?? 0
    const remember = () => positions.current.set(region, node.scrollTop)
    node.addEventListener('scroll', remember)
    return () => {
      remember()
      node.removeEventListener('scroll', remember)
    }
  }, [region])
  useLayoutEffect(() => {
    if (tool) {
      const focus = () => {
        const input = root.current?.querySelector<HTMLInputElement>('.avalon-details-reading input')
        if (!input) return false
        input.focus()
        return true
      }
      if (focus()) return
      const observer = new MutationObserver(() => {
        if (focus()) observer.disconnect()
      })
      if (body.current) observer.observe(body.current, { childList: true, subtree: true })
      return () => observer.disconnect()
    } else if (reading) {
      root.current
        ?.querySelector<HTMLButtonElement>('.avalon-details-back-row button')
        ?.focus({ preventScroll: true })
    } else if (returnFocus.current) {
      const target = returnFocus.current.isConnected
        ? returnFocus.current
        : root.current?.querySelector<HTMLElement>(
            `[data-details-reading="${returnFocus.current.dataset.detailsReading}"]`,
          )
      target?.focus({ preventScroll: true })
      returnFocus.current = null
    }
  }, [tool, reading])
  useEffect(() => {
    if (!fullscreen) return
    const frame = requestAnimationFrame(() =>
      root.current
        ?.querySelector<HTMLElement>('[data-controller-play], .avalon-details-more > button')
        ?.focus(),
    )
    return () => cancelAnimationFrame(frame)
  }, [fullscreen, Boolean(primary)])
  function change(next: string) {
    if (body.current) positions.current.set(region, body.current.scrollTop)
    setSection(next)
  }
  function openReading(next: Reading, origin: HTMLElement) {
    returnFocus.current = origin
    setReading(next)
  }
  function closeLayer() {
    if (moreOpen) {
      setMoreOpen(false)
      more.current?.focus()
      return true
    }
    if (tool) {
      setTool(null)
      returnFocus.current = more.current
      return true
    }
    if (reading) {
      setReading(null)
      return true
    }
    return false
  }
  function escape(event: KeyboardEvent) {
    if (event.key !== 'Escape' || event.defaultPrevented) return
    const dialog = (event.target as HTMLElement).closest('[role="dialog"]')
    if (dialog && dialog !== root.current) return
    if (closeLayer()) {
      event.preventDefault()
      event.stopPropagation()
    }
  }
  function tabsKey(event: KeyboardEvent<HTMLElement>) {
    if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return
    const buttons = [...event.currentTarget.querySelectorAll<HTMLButtonElement>('[role="tab"]')]
    const index = buttons.indexOf(event.target as HTMLButtonElement)
    if (index < 0) return
    event.preventDefault()
    event.stopPropagation()
    const next =
      event.key === 'Home'
        ? 0
        : event.key === 'End'
          ? buttons.length - 1
          : (index + (event.key === 'ArrowRight' ? 1 : -1) + buttons.length) % buttons.length
    buttons[next]?.focus()
    buttons[next]?.click()
  }
  const history = (
    <>
      {game && details.data && <ActivityTimeline game={game} details={details.data} mode={mode} />}
      <section className="feature-panel">
        <h2>Recorded sessions</h2>
        <SessionRows sessions={sessions} onEdit={setEditing} />
      </section>
      {game && <SteamReportedActivity games={[game]} mode={mode} />}
    </>
  )
  const journal = (
    <section className="feature-panel">
      <h2>Journal</h2>
      {!notes.length ? (
        <Empty>
          {preferences.data?.promptAfterPlay === false
            ? 'Journal prompts are off. Turn them on in Display preferences after a game.'
            : preferences.data?.promptAfterPlay
              ? 'No notes yet. After you play, Winnow will ask how it went.'
              : 'No notes yet. Add one to a recorded session from Activity.'}
        </Empty>
      ) : (
        notes.map((note) => (
          <article className="timeline-entry" key={note.sessionId}>
            <time>{dateLabel(note.sessionAt)}</time>
            <div>
              {note.note && <blockquote>{note.note}</blockquote>}
              {note.rating && <p>{note.rating} / 5</p>}
            </div>
            <button onClick={() => setEditing(note.sessionId)}>Edit note</button>
          </article>
        ))
      )}
    </section>
  )
  const about = (
    <section className="avalon-about feature-panel">
      <h2>About</h2>
      <p className={`game-summary${fullscreen && !reading ? ' synopsis' : ''}`}>
        {reading || expanded ? summary : descriptionPreview(summary)}
      </p>
      {fullscreen ? (
        <button
          className="detail-text-link"
          data-details-reading="About"
          onClick={(event) => openReading('About', event.currentTarget)}
        >
          Read more →
        </button>
      ) : (
        summary.length > 360 && (
          <button className="detail-text-link" onClick={() => setExpanded((value) => !value)}>
            {expanded ? 'Show less' : 'Read more'}
          </button>
        )
      )}
      <Screenshots details={details.data} {...(fullscreen ? { previewCount: 2 } : {})} />
      {!fullscreen && <ReceptionLine ratings={details.data?.ratings} />}
      {!fullscreen && game && hasRelationships && (
        <details className="detail-expansions">
          <summary>Related games & expansions</summary>
          <DetailsRelationships game={game} mode={mode} />
        </details>
      )}
    </section>
  )
  const personal = (
    <section className="avalon-personal-history" aria-label="Play history">
      <div className="avalon-history-figures">
        <div>
          <strong>{game ? hours(game.playtimeMinutes) : '—'}</strong>
          <span>played</span>
        </div>
        <div>
          <strong>
            {game?.lastPlayedAt
              ? relativePlayed(game.lastPlayedAt)
              : game?.playtimeMinutes
                ? 'Unknown'
                : 'Never'}
          </strong>
          <span>
            {game?.lastPlayedAt ? 'since last played' : game?.playtimeMinutes ? 'last-played date' : 'opened'}
          </span>
        </div>
      </div>
      {game?.lastPlayedAt && <p className="detail-support">{dateLabel(game.lastPlayedAt)}</p>}
      <LifecycleEvidence workId={workId} workspace={workspace.data} />
      {unread > 0 && (
        <button className="detail-text-link" onClick={() => change('Updates')}>
          {unread} unread {unread === 1 ? 'update' : 'updates'} →
        </button>
      )}
      {fullscreen && (
        <>
          <button
            className="detail-text-link"
            data-details-reading="History"
            onClick={(event) => openReading('History', event.currentTarget)}
          >
            Play history →
          </button>
          <div className="avalon-latest-note">
            <h2>{notes[0] ? 'Latest note' : 'Journal'}</h2>
            {notes[0] && (
              <>
                {notes[0].note && <p>{notes[0].note}</p>}
                {notes[0].rating && <p>{notes[0].rating} / 5</p>}
              </>
            )}
            <button className="detail-text-link" onClick={() => change('Journal')}>
              Open journal →
            </button>
          </div>
        </>
      )}
    </section>
  )
  let panel: ReactNode
  if (tool === 'Metadata') panel = <MetadataEditor workId={workId} />
  else if (tool === 'Game match') panel = <IgdbMatch workId={workId} title={game?.title ?? ''} />
  else if (tool === 'Artwork') panel = <ArtworkEditor workId={workId} />
  else if (reading === 'History' || section === 'Activity') panel = history
  else if (reading === 'About')
    panel = (
      <section className="feature-panel">
        <h2>About</h2>
        <p className="game-summary">{summary}</p>
        {game?.publisher && <p>Published by {game.publisher}</p>}
        <ReceptionLine ratings={details.data?.ratings} />
        {game && workspace.data && (
          <GameLinks links={gameLinks(game, workspace.data, details.data?.events)} />
        )}
      </section>
    )
  else if (section === 'Updates') panel = <UpdateSignals details={details.data} game={game} />
  else if (section === 'Journal') panel = journal
  else if (section === 'Library')
    panel = (
      <>
        <section className="feature-panel">
          <h2>Owned copies</h2>
          {(game?.entries.length ?? 0) > 1 && (
            <p>
              {hours(game!.playtimeMinutes)} summed across {game!.entries.length} listed entries.
            </p>
          )}
          {game?.entries.map((entry) => (
            <div className="avalon-copy" key={entry.ownershipId}>
              <EntryActions entry={entry} workspace={workspace.data} />
              <p>
                Last played{' '}
                {entry.lastPlayedAt
                  ? dateLabel(entry.lastPlayedAt)
                  : entry.playtimeMinutes
                    ? 'date not recorded'
                    : 'never'}
              </p>
            </div>
          ))}
          <Achievements game={game} details={details.data} />
        </section>
        <ListMembership workId={workId} mode={mode} />
        <LibraryFacts game={game} details={details.data} />
        {game && <DetailsRelationships game={game} mode={mode} />}
        {game && workspace.data && (
          <GameLinks links={gameLinks(game, workspace.data, details.data?.events)} />
        )}
        <HideGame workId={workId} onHidden={onClose} />
      </>
    )
  else
    panel = (
      <div className="avalon-details-overview">
        {personal}
        {about}
      </div>
    )
  const content = (
    <>
      <Artwork workId={workId} hero eager className="avalon-detail-backdrop" />
      <header className="avalon-details-header" aria-label="Game identity">
        {!fullscreen && <Artwork workId={workId} eager className="avalon-detail-cover" />}
        <div className="avalon-details-identity">
          {fullscreen ? (
            <h1
              id={`${id}-title`}
              title={game?.title}
              data-long={Boolean(game && game.title.length > 45) || undefined}
            >
              {game?.title ?? 'Game details'}
            </h1>
          ) : (
            <Dialog.Title asChild>
              <h1 title={game?.title}>{game?.title ?? 'Game details'}</h1>
            </Dialog.Title>
          )}
          <p className="detail-support">
            {[game?.firstReleaseYear, game?.publisher].filter(Boolean).join(' · ')}
          </p>
          {!fullscreen && <ReceptionLine ratings={details.data?.ratings} compact />}
          <p className="detail-support">
            {storeLine}
            {primary &&
              typeof primary.installed === 'boolean' &&
              ` · ${primary.installed ? 'Installed' : 'Not installed'}`}
          </p>
          <div className="avalon-details-actions" aria-label="Game actions">
            {primary && (
              <EntryActions
                key={primary.ownershipId}
                entry={primary}
                workspace={workspace.data}
                primaryOnly
              />
            )}
            {game && (
              <AddToListButton
                games={[game]}
                mode={mode}
                origin="details"
                label="Add to list"
                icon={<ListPlus size={16} aria-hidden="true" />}
              />
            )}
            <MoreActions
              open={moreOpen}
              setOpen={setMoreOpen}
              buttonRef={more}
              onChoose={(next) => setTool(next)}
              workId={workId}
              links={links}
              folder={
                primary && (
                  <InstallFolderButton
                    ownershipId={primary.ownershipId}
                    installed={primary.installed}
                    installPath={ownerships.find((entry) => entry.id === primary.ownershipId)?.installPath}
                  />
                )
              }
            />
          </div>
        </div>
        {!fullscreen && (
          <button className="avalon-details-close" aria-label="Close game details" onClick={onClose}>
            <X size={20} />
          </button>
        )}
      </header>
      <Notice error={details.error || library.error || workspace.error} />
      {tool || reading ? (
        <div className="avalon-details-back-row">
          <button onClick={closeLayer}>
            <ArrowLeft size={18} /> Back to {section}
          </button>
          <h2>{tool ?? reading}</h2>
        </div>
      ) : (
        <nav className="avalon-details-tabs" role="tablist" aria-label="Game information" onKeyDown={tabsKey}>
          {sections.map((name) => (
            <button
              key={name}
              id={`${id}-${name}`}
              role="tab"
              aria-selected={section === name}
              aria-controls={`${id}-reading`}
              tabIndex={section === name ? 0 : -1}
              data-controller-tab
              aria-label={
                name === 'Updates' && unread
                  ? `Updates, ${unread} unread ${unread === 1 ? 'update' : 'updates'}`
                  : name
              }
              onClick={() => change(name)}
            >
              {name}
              {name === 'Updates' && unread > 0 && <i className="avalon-details-unread" aria-hidden="true" />}
            </button>
          ))}
        </nav>
      )}
      <div
        ref={body}
        className="avalon-details-reading"
        id={`${id}-reading`}
        role={tool || reading ? 'region' : 'tabpanel'}
        aria-labelledby={tool || reading ? undefined : `${id}-${section}`}
        aria-label={tool ?? reading ?? undefined}
        tabIndex={0}
      >
        {panel}
      </div>
      {editing != null && <JournalEditor sessionId={editing} onClose={() => setEditing(null)} />}
    </>
  )
  if (fullscreen)
    return (
      <div ref={root} className="avalon-details fullscreen" onKeyDown={escape}>
        {content}
      </div>
    )
  return (
    <Dialog.Root
      open
      onOpenChange={(open) => {
        if (!open) onClose?.()
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay avalon-details-scrim" />
        <Dialog.Content
          ref={root}
          className="avalon-details desktop"
          aria-describedby={undefined}
          onEscapeKeyDown={(event) => {
            if (closeLayer()) event.preventDefault()
          }}
          onKeyDown={escape}
          onOpenAutoFocus={(event) => {
            event.preventDefault()
            root.current?.querySelector<HTMLElement>('[data-controller-play], .avalon-details-close')?.focus()
          }}
          onCloseAutoFocus={(event) => {
            event.preventDefault()
            requestAnimationFrame(() => {
              if (opener.current?.isConnected) opener.current.focus({ preventScroll: true })
            })
          }}
        >
          {content}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}

function relativePlayed(date: string) {
  const days = Math.max(0, Math.floor((Date.now() - Date.parse(date)) / 86_400_000))
  return days >= 365 ? `${Math.floor(days / 365)}y` : days >= 30 ? `${Math.floor(days / 30)}mo` : `${days}d`
}
function Achievements({ game, details }: { game?: LibraryGame; details?: GameDetails }) {
  if (!details?.achievements.length) return null
  return (
    <div className="detail-achievements" aria-label="Achievements by release">
      {details.achievements.map((item) => (
        <p key={item.releaseId}>
          {storeLabel(game?.entries.find((entry) => entry.releaseId === item.releaseId)?.store ?? 'Edition')}:{' '}
          {item.hasKnownProgress
            ? item.total
              ? `${item.unlocked} of ${item.total} unlocked · ${Math.round((item.unlocked / item.total) * 100)}%`
              : 'No achievements'
            : 'Progress unavailable'}
          {item.isStale ? ' · last known' : ''}
        </p>
      ))}
    </div>
  )
}
function MoreActions({
  open,
  setOpen,
  buttonRef,
  onChoose,
  workId,
  links,
  folder,
}: {
  open: boolean
  setOpen(open: boolean): void
  buttonRef: React.RefObject<HTMLButtonElement | null>
  onChoose(tool: Tool): void
  workId: number
  links: GameLink[]
  folder: ReactNode
}) {
  const menu = useRef<HTMLDivElement>(null),
    root = useRef<HTMLDivElement>(null)
  useLayoutEffect(() => {
    if (open) menu.current?.querySelector<HTMLButtonElement>('button')?.focus()
  }, [open])
  useEffect(() => {
    const dismiss = (event: PointerEvent) => {
      if (!root.current?.contains(event.target as Node)) setOpen(false)
    }
    if (open) document.addEventListener('pointerdown', dismiss)
    return () => document.removeEventListener('pointerdown', dismiss)
  }, [open])
  return (
    <div
      className="avalon-details-more"
      ref={root}
      onKeyDown={(event) => {
        if (!open) return
        if (event.key === 'Escape') {
          event.preventDefault()
          event.stopPropagation()
          setOpen(false)
          buttonRef.current?.focus()
        }
        if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
          event.preventDefault()
          event.stopPropagation()
          const actions = [...(menu.current?.querySelectorAll<HTMLButtonElement>('button') ?? [])],
            index = actions.indexOf(event.target as HTMLButtonElement)
          actions[
            event.key === 'Home'
              ? 0
              : event.key === 'End'
                ? actions.length - 1
                : (index + (event.key === 'ArrowDown' ? 1 : -1) + actions.length) % actions.length
          ]?.focus()
        }
      }}
    >
      <button ref={buttonRef} aria-expanded={open} data-controller-context onClick={() => setOpen(!open)}>
        More <ChevronDown size={16} />
      </button>
      {open && (
        <div className="avalon-details-menu" ref={menu} aria-label="More game actions">
          {tools.map((tool) => (
            <button
              key={tool}
              onClick={() => {
                setOpen(false)
                onChoose(tool)
              }}
            >
              {tool === 'Game match' ? 'Wrong game…' : tool === 'Metadata' ? 'Edit metadata…' : 'Artwork…'}
            </button>
          ))}
          <MetadataRefresh workId={workId} />
          {folder}
          <GameLinks links={links} />
        </div>
      )}
    </div>
  )
}
