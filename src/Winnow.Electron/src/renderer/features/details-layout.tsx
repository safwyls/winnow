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
import { ArrowLeft, ChevronDown, ListPlus, X, Image, Pencil, Search, EyeOff } from 'lucide-react'
import { useApiQuery, useCommand, useDetails, useLibrary, useWorkspace } from '../api/hooks'
import { dateLabel, hours, storeLabel } from '../api/client'
import { primaryEntry } from '../../shared/game-actions'
import type { GameDetails, LibraryGame, Mode, Workspace } from '../api/types'
import { Artwork } from '../components/Artwork'
import { EntryActions, GameLinks, HideGame, ListMembership } from './Details'
import { IgdbMatch, LibraryFacts, Screenshots, UpdateSignals } from './parity-details'
import { MetadataDialog } from './metadata-dialog'
import {
  ReceptionLine,
  MetadataRefreshButton,
  MetadataRefreshStatus,
  useMetadataRefresh,
  LifecycleEvidence,
} from './details-presentation'
import { DetailsRelationships } from './parity-details-identity'
import { AddToListButton, AddToListDialog } from './parity-list-prompt'
import { ActivityTimeline } from './activity-timeline'
import { SteamReportedActivity } from './activity-steam'
import { JournalEditor, SessionRows } from './Journal'
import { Empty, Notice } from './shared'
import { gameLinks, type GameLink } from '../api/gameLinks'
import { useViewState } from '../viewState'
import { InstallFolderButton } from './install-folder'
import { detailIdle, detailPlaytime } from './details-facts'
import { ArtworkBrowserDialog } from './artwork-browser'
import { AvalonBackdrop } from '../themes/avalon-backdrop'
import { restoreFocusWhenReady } from './restore-focus'
import { AvalonAction, AvalonActions } from '../themes/avalon-actions'
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
  editText,
}: {
  workId: number
  mode?: Mode
  onClose?: () => void
  editText?(input: HTMLInputElement | HTMLTextAreaElement): void
}) {
  const fullscreen = mode === 'fullscreen',
    id = useId()
  const library = useLibrary(),
    workspace = useWorkspace(),
    details = useDetails(workId)
  const preferences = useApiQuery<{ promptAfterPlay: boolean }>('journal.preferences.get')
  const refresh = useMetadataRefresh(workId)
  const game = detailsGame(workId, library.data?.games ?? [], workspace.data)
  const provisionalTitle = workspace.data?.works.find((work) => work.id === game?.workId)?.nameIsProvisional
  const [section, setSection] = useState<string>('Overview')
  const [listPrompt, setListPrompt] = useState(false)
  const [reading, setReading] = useState<Reading | null>(null),
    [tool, setTool] = useState<Tool | null>(null)
  const [expanded, setExpanded] = useState(false)
  const [matchNote, setMatchNote] = useState('')
  const [artworkOpen, setArtworkOpen] = useState(false)
  const [metadataOpen, setMetadataOpen] = useState(false)
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
          : fullscreen
            ? Math.max(0, Math.min(buttons.length - 1, index + (event.key === 'ArrowRight' ? 1 : -1)))
            : (index + (event.key === 'ArrowRight' ? 1 : -1) + buttons.length) % buttons.length
    buttons[next]?.focus()
    if (!fullscreen) buttons[next]?.click()
  }
  function fullscreenKeys(event: KeyboardEvent<HTMLElement>) {
    escape(event)
    if (event.defaultPrevented || !['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(event.key))
      return
    const target = event.target as HTMLElement
    if (target.closest('[role="dialog"],[role="alertdialog"],[role="menu"]') || tool) return
    if (reading === 'About' && (event.key === 'ArrowUp' || event.key === 'ArrowDown')) {
      event.preventDefault()
      event.stopPropagation()
      body.current?.scrollBy({ top: event.key === 'ArrowUp' ? -160 : 160, behavior: 'instant' })
      return
    }
    if (target.matches('input,textarea,select')) return
    const visible = (element: HTMLElement) =>
      !element.matches(':disabled') &&
      !element.closest('[hidden],[inert],[aria-hidden="true"]') &&
      element.getBoundingClientRect().height > 0
    const row = (selector: string) =>
      [...root.current!.querySelectorAll<HTMLElement>(selector)].filter(visible)
    const rows = [
      row('.avalon-details-actions button'),
      row('.avalon-details-tabs [role="tab"], .avalon-details-back-row button'),
      ...(!reading && section === 'Overview'
        ? [
            row('[data-details-reading="History"], [data-details-reading="About"]'),
            row('.avalon-latest-note button, .screenshot-strip button'),
            row('.screenshot-gallery-link'),
          ]
        : row('.avalon-details-reading button, .avalon-details-reading a[href]').map((element) => [element])),
    ].filter((entries) => entries.length)
    const rowIndex = rows.findIndex((entries) => entries.includes(target))
    if (rowIndex < 0) return
    const column = rows[rowIndex]!.indexOf(target)
    const vertical = event.key === 'ArrowUp' || event.key === 'ArrowDown'
    const delta = event.key === 'ArrowUp' || event.key === 'ArrowLeft' ? -1 : 1
    const nextRow = vertical ? Math.max(0, Math.min(rows.length - 1, rowIndex + delta)) : rowIndex
    const nextColumn = Math.max(0, Math.min(rows[nextRow]!.length - 1, column + (vertical ? 0 : delta)))
    event.preventDefault()
    event.stopPropagation()
    const next = rows[nextRow]![nextColumn]!
    next.focus({ preventScroll: true })
    next.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'instant' })
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
          <strong>{game ? detailPlaytime(game.playtimeMinutes) : '—'}</strong>
          <span>played</span>
        </div>
        {game?.lastPlayedAt && (
          <div>
            <strong>{detailIdle(game.lastPlayedAt)}</strong>
            <span>since last played</span>
          </div>
        )}
      </div>
      {game?.lastPlayedAt && <p className="detail-support">{dateLabel(game.lastPlayedAt)}</p>}
      {game && !game.lastPlayedAt && (
        <p className="detail-history-absence">
          {game.playtimeMinutes <= 0
            ? "You've never opened this."
            : `${storeLine || 'Your library'} has no date for your last session.`}
        </p>
      )}
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
  if (tool === 'Game match')
    panel = (
      <IgdbMatch
        workId={workId}
        title={game?.title ?? ''}
        onChanged={(note) => {
          setMatchNote(note)
          setTool(null)
          returnFocus.current = more.current
        }}
      />
    )
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
        <LibraryFacts game={game} details={details.data} showTechnicalFacts />
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
      <AvalonBackdrop
        workId={game?.workId ?? workId}
        fullscreen={fullscreen}
        cinematic={fullscreen}
        className="avalon-detail-backdrop"
      />
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
          {(game?.firstReleaseYear || game?.publisher) && (
            <p className="detail-support" data-details-identity-line>
              {[game?.firstReleaseYear, game?.publisher].filter(Boolean).join(' · ')}
            </p>
          )}
          {provisionalTitle && (
            <p className="detail-support">Name not yet available. Showing the app id until metadata loads.</p>
          )}
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
            {game && !fullscreen && (
              <AddToListButton
                games={[game]}
                mode={mode}
                origin="details"
                label="Add to list"
                icon={<ListPlus size={16} aria-hidden="true" />}
              />
            )}
            <MoreActions
              addToList={game ? () => setListPrompt(true) : undefined}
              fullscreen={fullscreen}
              open={moreOpen}
              setOpen={setMoreOpen}
              buttonRef={more}
              onChoose={(next) =>
                next === 'Artwork'
                  ? setArtworkOpen(true)
                  : next === 'Metadata'
                    ? setMetadataOpen(true)
                    : setTool(next)
              }
              workId={workId}
              refresh={refresh}
              gameTitle={game?.title ?? 'this game'}
              onHidden={onClose}
              links={links}
              management={
                primary && <EntryActions entry={primary} workspace={workspace.data} managementOnly />
              }
              hide={<HideGame workId={workId} onHidden={onClose} compact />}
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
      <Notice error={details.error || library.error || workspace.error} message={matchNote} />
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
        tabIndex={fullscreen ? -1 : 0}
        data-controller-scroll-step={fullscreen && reading === 'About' ? 160 : undefined}
      >
        {panel}
      </div>
      <MetadataRefreshStatus state={refresh} className="detail-refetch-status" polite />
      {editing != null && <JournalEditor sessionId={editing} onClose={() => setEditing(null)} />}
      {listPrompt && game && (
        <AddToListDialog
          games={[game]}
          mode={mode}
          origin="details"
          onClose={() => setListPrompt(false)}
          restoreFocus={() => more.current?.focus({ preventScroll: true })}
        />
      )}
      {metadataOpen && (
        <MetadataDialog
          workId={workId}
          title={game?.title ?? 'Game'}
          mode={mode}
          editText={editText}
          onClose={() => {
            setMetadataOpen(false)
            requestAnimationFrame(() => more.current?.focus({ preventScroll: true }))
          }}
        />
      )}
      {artworkOpen && (
        <ArtworkBrowserDialog
          workId={workId}
          title={game?.title ?? 'Game artwork'}
          mode={mode}
          onClose={() => {
            setArtworkOpen(false)
            requestAnimationFrame(() => more.current?.focus({ preventScroll: true }))
          }}
        />
      )}
    </>
  )
  if (fullscreen)
    return (
      <div ref={root} className="avalon-details fullscreen" onKeyDown={fullscreenKeys}>
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
            restoreFocusWhenReady(opener.current)
          }}
        >
          {content}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
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
  addToList,
  fullscreen,
  open,
  setOpen,
  buttonRef,
  onChoose,
  workId,
  refresh,
  gameTitle,
  onHidden,
  links,
  folder,
  management,
  hide,
}: {
  addToList?(): void
  fullscreen: boolean
  open: boolean
  setOpen(open: boolean): void
  buttonRef: React.RefObject<HTMLButtonElement | null>
  onChoose(tool: Tool): void
  workId: number
  refresh: ReturnType<typeof useMetadataRefresh>
  gameTitle: string
  onHidden?(): void
  links: GameLink[]
  folder: ReactNode
  management: ReactNode
  hide: ReactNode
}) {
  const menu = useRef<HTMLDivElement>(null),
    root = useRef<HTMLDivElement>(null)
  useLayoutEffect(() => {
    if (open && !fullscreen) menu.current?.querySelector<HTMLButtonElement>('button')?.focus()
  }, [open, fullscreen])
  useEffect(() => {
    const dismiss = (event: PointerEvent) => {
      if (!root.current?.contains(event.target as Node)) setOpen(false)
    }
    if (open && !fullscreen) document.addEventListener('pointerdown', dismiss)
    return () => document.removeEventListener('pointerdown', dismiss)
  }, [open, fullscreen])
  const skipRestore = useRef(false)
  const [confirmHide, setConfirmHide] = useState(false)
  const hideCommand = useCommand()
  async function hideGame() {
    try {
      await hideCommand.mutateAsync({ route: 'hidden.put', body: { workIds: [workId], hidden: true } })
      onHidden?.()
    } catch {
      /* The retained page displays the failure after the action panel closes. */
    }
  }
  const items = (
    <>
      {fullscreen && addToList && (
        <AvalonAction
          label="Add to list"
          icon={ListPlus}
          onChoose={() => {
            skipRestore.current = true
            setOpen(false)
            addToList()
          }}
        />
      )}
      <GameLinks links={links} />
      {management}
      {folder}
      <MetadataRefreshButton
        state={refresh}
        onInvoked={() => {
          setOpen(false)
          buttonRef.current?.focus({ preventScroll: true })
        }}
      />
      {tools.map((tool) => {
        const label =
          tool === 'Game match' ? 'Wrong game?' : tool === 'Metadata' ? 'Edit details' : 'Artwork…'
        const choose = () => {
          skipRestore.current = true
          setOpen(false)
          onChoose(tool)
        }
        return fullscreen ? (
          <AvalonAction
            key={tool}
            label={label}
            icon={tool === 'Game match' ? Search : tool === 'Metadata' ? Pencil : Image}
            description={tool === 'Game match' ? 'Search IGDB for the right entry' : undefined}
            onChoose={choose}
          />
        ) : (
          <button
            key={tool}
            title={tool === 'Game match' ? 'Search IGDB for the right entry' : undefined}
            onClick={choose}
          >
            {label}
          </button>
        )
      })}
      {fullscreen ? (
        <AvalonAction
          label="Hide game…"
          icon={EyeOff}
          disabled={hideCommand.isPending}
          onChoose={() => {
            setConfirmHide(true)
            setOpen(true)
          }}
        />
      ) : (
        hide
      )}
    </>
  )
  return (
    <div
      className="avalon-details-more"
      ref={root}
      onKeyDown={(event) => {
        if (!open || fullscreen) return
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
      <button
        ref={buttonRef}
        aria-expanded={open}
        title="Store and news links, installation, folder, metadata, corrections and hide"
        data-controller-context
        onClick={() => {
          skipRestore.current = false
          setConfirmHide(false)
          setOpen(!open)
        }}
      >
        More <ChevronDown size={16} />
      </button>
      {fullscreen ? (
        <AvalonActions
          open={open}
          title={confirmHide ? `Hide ${gameTitle}?` : 'More game actions'}
          close={() => setOpen(false)}
          restoreFocus={() => {
            if (!skipRestore.current) buttonRef.current?.focus()
          }}
        >
          {confirmHide ? (
            <>
              <p>Its history stays saved. You can restore it from Library tools.</p>
              <AvalonAction label="Cancel" icon={X} onChoose={() => {}} />
              <AvalonAction
                label="Hide game"
                icon={EyeOff}
                disabled={hideCommand.isPending}
                onChoose={() => void hideGame()}
              />
            </>
          ) : (
            items
          )}
        </AvalonActions>
      ) : (
        open && (
          <div
            className="avalon-details-menu"
            data-controller-scope
            ref={menu}
            aria-label="More game actions"
          >
            {items}
          </div>
        )
      )}
      {fullscreen && <Notice error={hideCommand.error} />}
    </div>
  )
}
