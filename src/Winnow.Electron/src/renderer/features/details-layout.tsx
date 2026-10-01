import { SectionLabel } from '../components/SectionNavigation'
import {
  useEffect,
  useId,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type KeyboardEvent,
  type ReactNode,
} from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { ArrowLeft, ChevronDown, ListPlus, X, Image, Pencil, Search, EyeOff } from 'lucide-react'
import { useApiQuery, useCommand, useDetails, useLibrary, useWorkspace } from '../api/hooks'
import { dateLabel, hours, storeLabel } from '../api/client'
import { primaryEntry, primaryAction, noActionSentence } from '../../shared/game-actions'
import type { GameDetails, LibraryGame, Metadata, Mode, Workspace } from '../api/types'
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
import { DetailsRelationships, detailsRelationships } from './parity-details-identity'
import { AddToListButton, AddToListDialog } from './parity-list-prompt'
import { ActivityTimeline } from './activity-timeline'
import { SteamReportedActivity } from './activity-steam'
import { DetailsJournal, JournalEditor, SessionRows, useJournalSending } from './Journal'
import { Empty, Notice } from './shared'
import { gameLinks, type GameLink } from '../api/gameLinks'
import { useViewState } from '../viewState'
import { InstallFolderButton } from './install-folder'
import { detailIdle, detailPlaytime } from './details-facts'
import { librarySourceSummary } from './library-source'
import { ArtworkBrowserDialog } from './artwork-browser'
import { AvalonBackdrop } from '../themes/avalon-backdrop'
import { restoreFocusWhenReady } from './restore-focus'
import { AvalonAction, AvalonActions } from '../themes/avalon-actions'
import { ChooseLaunchVersion } from './choose-launch-version'
import { ReleaseAchievements } from './detail-achievements'
import type { IgdbState } from './igdb-match'
import './details-layout.css'
import { cachedGogPatchNotes, GogPatchNotes, GogPatchNotesText, GogPatchNotesHints } from './GogPatchNotes'

const desktopSections = ['Overview', 'Activity', 'Updates', 'Journal', 'Library'] as const
const televisionSections = ['Overview', 'Updates', 'Journal', 'Library'] as const
type Reading = 'About' | 'History' | 'Patch notes'
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
  onOpenGame,
  editText,
}: {
  workId: number
  mode?: Mode
  onClose?: () => void
  onOpenGame?(workId: number): void
  editText?(input: HTMLInputElement | HTMLTextAreaElement): void
}) {
  const fullscreen = mode === 'fullscreen',
    id = useId()
  const library = useLibrary(),
    workspace = useWorkspace(),
    details = useDetails(workId)
  const preferences = useApiQuery<{ promptAfterPlay: boolean }>('journal.preferences.get')
  const igdb = useApiQuery<IgdbState>('metadata.igdb', { workId })
  const metadata = useApiQuery<Metadata>('metadata.get', { workId })
  const refresh = useMetadataRefresh(workId)
  const game = detailsGame(workId, library.data?.games ?? [], workspace.data)
  const provisionalTitle = workspace.data?.works.find((work) => work.id === game?.workId)?.nameIsProvisional
  const [inlineEditing, setInlineEditing] = useViewState<{ id: number; deleting: boolean } | null>(
    `${mode}:details:${workId}:journal-inline`,
    null,
  )
  const [section, setSection] = useState<string>(inlineEditing ? 'Journal' : 'Overview')
  const closing = useRef(false)
  const closeDetails = () => {
    if (journalSending || closing.current) return
    closing.current = true
    setInlineEditing(null)
    onClose?.()
  }
  const [listPrompt, setListPrompt] = useState(false)
  const [reading, setReading] = useState<Reading | null>(null),
    [tool, setTool] = useState<Tool | null>(null)
  const [expanded, setExpanded] = useState(false)
  const [relationshipsExpanded, setRelationshipsExpanded] = useState(false)
  const [matchNote, setMatchNote] = useState('')
  const [artworkOpen, setArtworkOpen] = useState(false)
  const [metadataOpen, setMetadataOpen] = useState(false)
  const [moreOpen, setMoreOpen] = useState(false)
  const [editing, setEditing] = useViewState<number | null>(`${mode}:details:${workId}:editing`, null)
  const inlineSending = useJournalSending(inlineEditing?.id)
  const modalSending = useJournalSending(editing)
  const journalSending = inlineSending || modalSending
  const body = useRef<HTMLDivElement>(null),
    root = useRef<HTMLDivElement>(null),
    more = useRef<HTMLButtonElement>(null)
  const positions = useRef(new Map<string, number>()),
    returnFocus = useRef<HTMLElement | null>(null)
  const opener = useRef(document.activeElement instanceof HTMLElement ? document.activeElement : null)
  const region = tool ?? reading ?? section
  useEffect(() => {
    if (igdb.data?.available === false && tool === 'Game match') {
      setTool(null)
      returnFocus.current = more.current
    }
  }, [igdb.data?.available, tool])
  useEffect(() => {
    if (metadata.data?.available === false) setMetadataOpen(false)
  }, [metadata.data?.available])
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
  const gogNotes = cachedGogPatchNotes(primary, workspace.data)
  const ownerships = (details.data?.ownerships ?? []) as { id: number; installPath?: string | null }[]
  const links = game && workspace.data ? gameLinks(game, workspace.data, details.data?.events) : []
  const hasExpansions =
    game && detailsRelationships(game, workspace.data, 'expansions', library.data?.games ?? []).length > 0
  const sessions = useMemo(
    () =>
      Object.values(details.data?.sessions ?? {})
        .flat()
        .sort((a, b) => b.startedAt.localeCompare(a.startedAt)),
    [details.data?.sessions],
  )
  const notes = useMemo(
    () => [...(details.data?.journalEntries ?? [])].sort((a, b) => b.sessionAt.localeCompare(a.sessionAt)),
    [details.data?.journalEntries],
  )
  const summary = game?.summary?.trim() || 'No description yet. Metadata fills in automatically.'
  const storeLine = [...new Set(game?.entries.map((entry) => storeLabel(entry.store)) ?? [])].join(' · ')
  const sourceSummary = librarySourceSummary(game?.entries ?? [], workspace.data)
  useLayoutEffect(() => {
    const node = body.current
    if (!node) return
    node.scrollTop = positions.current.get(region) ?? 0
    const remember = () => positions.current.set(region, node.scrollTop)
    node.addEventListener('scroll', remember)
    return () => {
      // The next region's shorter DOM can already have clamped scrollTop during cleanup.
      node.removeEventListener('scroll', remember)
    }
  }, [region])
  useLayoutEffect(() => {
    if (tool) {
      const focus = () => {
        const input = root.current?.querySelector<HTMLInputElement>('.avalon-details-reading input')
        if (!input) return false
        if (fullscreen) restoreFocusWhenReady(input)
        else input.focus()
        return true
      }
      if (focus()) return
      const observer = new MutationObserver(() => {
        if (focus()) observer.disconnect()
      })
      if (body.current) observer.observe(body.current, { childList: true, subtree: true })
      return () => observer.disconnect()
    } else if (reading) {
      const focus = () => {
        const target = root.current?.querySelector<HTMLButtonElement>(
          fullscreen && reading === 'History'
            ? '.activity-tracker [aria-label="Play history range"] button'
            : '.avalon-details-back-row button',
        )
        target?.focus({ preventScroll: true })
        return Boolean(target)
      }
      if (focus()) return
      const observer = new MutationObserver(() => {
        if (focus()) observer.disconnect()
      })
      if (body.current) observer.observe(body.current, { childList: true, subtree: true })
      return () => observer.disconnect()
    } else if (returnFocus.current) {
      const target = returnFocus.current.isConnected
        ? returnFocus.current
        : root.current?.querySelector<HTMLElement>(
            `[data-details-reading="${returnFocus.current.dataset.detailsReading}"]`,
          )
      target?.focus({ preventScroll: true })
      returnFocus.current = null
    }
  }, [tool, reading, fullscreen])
  useEffect(() => {
    if (!fullscreen) return
    const frame = requestAnimationFrame(() => {
      const page = root.current
      if (!page || page.closest('[inert]') || page.contains(document.activeElement)) return
      page.querySelector<HTMLElement>('[data-controller-play], .avalon-details-more > button')?.focus()
    })
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
    if (journalSending) return true
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
    if (
      (reading === 'About' || reading === 'Patch notes') &&
      (event.key === 'ArrowUp' || event.key === 'ArrowDown')
    ) {
      event.preventDefault()
      event.stopPropagation()
      body.current?.scrollBy({ top: event.key === 'ArrowUp' ? -160 : 160, behavior: 'instant' })
      return
    }
    if (target.matches('textarea,select,input:not([type="checkbox"]):not([type="radio"])')) return
    const visible = (element: HTMLElement) =>
      !element.matches(':disabled') &&
      !element.closest('[hidden],[inert],[aria-hidden="true"]') &&
      element.getBoundingClientRect().height > 0
    const row = (selector: string) =>
      [...root.current!.querySelectorAll<HTMLElement>(selector)].filter(visible)
    const bodyActions =
      '.avalon-details-reading :is(button,a[href],input[type="checkbox"],input[type="radio"],select,summary)'
    const rows = [
      row('.avalon-details-actions button'),
      row('.avalon-details-tabs [role="tab"], .avalon-details-back-row button'),
      ...(!reading && section === 'Overview'
        ? [
            row('[data-details-reading="History"], [data-details-reading="About"]'),
            row('.avalon-latest-note button, .screenshot-strip button'),
            row('.screenshot-gallery-link'),
          ]
        : reading === 'History'
          ? [
              row('.activity-tracker [aria-label="Play history range"] button'),
              ...row(bodyActions)
                .filter((element) => !element.closest('[aria-label="Play history range"]'))
                .map((element) => [element]),
            ]
          : row(bodyActions).map((element) => [element])),
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
    <DetailsJournal
      notes={notes}
      promptAfterPlay={preferences.data?.promptAfterPlay}
      onEdit={setEditing}
      mode={mode}
      scopeKey={`${mode}:details:${workId}`}
    />
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
      {!fullscreen && game && hasExpansions && (
        <details
          className="detail-expansions"
          open={relationshipsExpanded}
          onToggle={(event) => setRelationshipsExpanded(event.currentTarget.open)}
        >
          <summary>Expansions & base game</summary>
          <DetailsRelationships game={game} mode={mode} scope="expansions" onOpenGame={onOpenGame} />
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
  if (tool === 'Game match' && igdb.data?.available !== false)
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
        <p className="game-summary reading-prose">{summary}</p>
        {game?.publisher && <p>Published by {game.publisher}</p>}
        <ReceptionLine ratings={details.data?.ratings} />
        {game && workspace.data && (
          <GameLinks links={gameLinks(game, workspace.data, details.data?.events)} />
        )}
      </section>
    )
  else if (reading === 'Patch notes') panel = <GogPatchNotesText notes={gogNotes} />
  else if (section === 'Updates')
    panel = (
      <>
        <UpdateSignals details={details.data} game={game} hasCachedNotes={!!gogNotes} />
        <GogPatchNotes
          notes={gogNotes}
          read={fullscreen ? (origin) => openReading('Patch notes', origin) : undefined}
        />
      </>
    )
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
              <h3>{workspace.data?.works.find((work) => work.id === entry.workId)?.name ?? entry.title}</h3>
              <EntryActions
                entry={entry}
                workspace={workspace.data}
                launchTitle={game?.title}
                compactPlaytime
              />
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
          <ReleaseAchievements game={game} details={details.data} />
        </section>
        <ListMembership workId={workId} mode={mode} />
        <LibraryFacts game={game} details={details.data} showTechnicalFacts />
        {game && (
          <DetailsRelationships
            game={game}
            mode={mode}
            scope={fullscreen ? 'all' : 'editions'}
            onOpenGame={onOpenGame}
          />
        )}
        {game && workspace.data && (
          <GameLinks links={gameLinks(game, workspace.data, details.data?.events)} />
        )}
        <HideGame workId={workId} onHidden={closeDetails} />
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
        coverWorkId={game?.headerWorkId}
        fullscreen={fullscreen}
        cinematic={fullscreen}
        className="avalon-detail-backdrop"
      />
      <header className="avalon-details-header" aria-label="Game identity">
        {!fullscreen && (
          <Artwork workId={game?.headerWorkId ?? workId} eager className="avalon-detail-cover" />
        )}
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
          {sourceSummary && (
            <p className="detail-support" data-library-source-summary>
              {sourceSummary}
            </p>
          )}
          <div className="avalon-details-actions" aria-label="Game actions">
            {primary && primaryAction(primary, workspace.data) && (
              <EntryActions
                key={primary.ownershipId}
                entry={primary}
                workspace={workspace.data}
                primaryOnly
                launchTitle={game?.title}
              />
            )}
            {fullscreen && game && game.entries.length > 1 && (
              <ChooseLaunchVersion game={game} workspace={workspace.data} />
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
              metadataAvailable={metadata.data?.available !== false}
              matchAvailable={igdb.data?.available !== false}
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
              onHidden={closeDetails}
              links={links}
              management={
                primary && <EntryActions entry={primary} workspace={workspace.data} managementOnly />
              }
              hide={<HideGame workId={workId} onHidden={closeDetails} compact />}
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
          {!fullscreen && primary && !primaryAction(primary, workspace.data) && (
            <p className="detail-support">
              {primary.store === 'manual'
                ? 'Manual entries can be tracked here. Launch this game from its shortcut.'
                : noActionSentence(primary, workspace.data)}
            </p>
          )}
        </div>
        {!fullscreen && (
          <button
            className="avalon-details-close"
            aria-label="Close game details"
            disabled={journalSending}
            onClick={closeDetails}
          >
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
              {fullscreen ? (
                <SectionLabel>{name === 'Updates' && unread ? `Updates ${unread}` : name}</SectionLabel>
              ) : (
                name
              )}
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
        data-controller-scroll-step={
          fullscreen && (reading === 'About' || reading === 'Patch notes') ? 160 : undefined
        }
      >
        {fullscreen &&
          section === 'Overview' &&
          !reading &&
          !tool &&
          primary &&
          !primaryAction(primary, workspace.data) && (
            <p className="detail-support">
              {primary.store === 'manual'
                ? 'Manual entries can be tracked here. Launch this game from its shortcut.'
                : noActionSentence(primary, workspace.data)}
            </p>
          )}
        {panel}
      </div>
      {fullscreen && reading === 'Patch notes' && <GogPatchNotesHints />}
      <MetadataRefreshStatus state={refresh} className="detail-refetch-status" polite />
      {editing != null && (
        <JournalEditor sessionId={editing} onClose={() => setEditing(null)} mode={mode} editText={editText} />
      )}
      {listPrompt && game && (
        <AddToListDialog
          games={[game]}
          mode={mode}
          origin="details"
          onClose={() => setListPrompt(false)}
          restoreFocus={() => more.current?.focus({ preventScroll: true })}
        />
      )}
      {metadataOpen && metadata.data?.available !== false && (
        <MetadataDialog
          workId={workId}
          coverWorkId={game?.headerWorkId ?? game?.workId}
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
          coverWorkId={game?.headerWorkId ?? game?.workId}
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
      <div
        ref={root}
        className="avalon-details fullscreen"
        data-reading={reading ?? undefined}
        onKeyDown={fullscreenKeys}
      >
        {content}
      </div>
    )
  return (
    <Dialog.Root
      open
      onOpenChange={(open) => {
        if (!open) closeDetails()
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay avalon-details-scrim" />
        <Dialog.Content
          ref={root}
          className="avalon-details desktop"
          aria-describedby={undefined}
          onEscapeKeyDown={(event) => {
            if (journalSending) {
              event.preventDefault()
              return
            }
            const dialog = (event.target as HTMLElement).closest('[role="dialog"]')
            if (dialog && dialog !== root.current) {
              event.preventDefault()
              return
            }
            if (closeLayer()) event.preventDefault()
          }}
          onKeyDown={escape}
          onInteractOutside={(event) => {
            if (journalSending) event.preventDefault()
          }}
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

function MoreActions({
  metadataAvailable,
  matchAvailable,
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
  metadataAvailable: boolean
  matchAvailable: boolean
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
      {tools
        .filter((tool) => tool !== 'Game match' || matchAvailable)
        .filter((tool) => tool !== 'Metadata' || metadataAvailable)
        .map((tool) => {
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
              description={
                tool === 'Game match'
                  ? 'Search IGDB for the right entry'
                  : tool === 'Metadata'
                    ? 'Edit each field by hand'
                    : undefined
              }
              onChoose={choose}
            />
          ) : (
            <button
              key={tool}
              title={
                tool === 'Game match'
                  ? 'Search IGDB for the right entry'
                  : tool === 'Metadata'
                    ? 'Edit each field by hand'
                    : undefined
              }
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
