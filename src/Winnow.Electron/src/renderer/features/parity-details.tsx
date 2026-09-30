import { useEffect, useImperativeHandle, useRef, useState, type Ref } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { ChevronLeft, ChevronRight, X } from 'lucide-react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, dateLabel, hours, request, storeLabel, openExternal } from '../api/client'
import { useApiQuery, useCommand, useWorkspace } from '../api/hooks'
import type { GameDetails, LibraryGame, Metadata } from '../api/types'
import { useViewState } from '../viewState'
import { Artwork } from '../components/Artwork'
import { Empty, Notice } from './shared'
import './parity-details.css'
import { acquisitionFacts, playtimeRecordLine, updateHeadline, updatePageUrl } from './details-facts'
import { updateFlagState } from './update-flags'
import { ArtworkBrowserDialog, type ArtworkSlot } from './artwork-browser'
import { InstallFolderButton } from './install-folder'

export { IgdbMatch } from './igdb-match'

type DetailFacts = GameDetails & {
  acknowledgements?: Record<string, string>
  ownerships?: {
    id: number
    releaseId: number
    store: string
    acquiredAt?: string | null
    licenseType?: string | null
    installPath?: string | null
  }[]
  history?: Record<string, { id: number; playtimeMinutes: number; observedAt: string }[]>
  images?: {
    source: string
    kind: string
    imageIds: string
    ids?: string[]
    images?: { imageId: string; url?: string | null }[]
  }[]
}

export function Screenshots({ details, previewCount }: { details?: GameDetails; previewCount?: number }) {
  const [selected, setSelected] = useState<string | null>(null)
  const [open, setOpen] = useState(false)
  const origin = useRef<HTMLButtonElement | null>(null)
  const closeButton = useRef<HTMLButtonElement | null>(null)
  const strip = useRef<HTMLDivElement>(null)
  const images = (details as DetailFacts | undefined)?.images ?? []
  const keys = images
    .filter((row) => row.kind === 'screenshot')
    .flatMap((row) =>
      row.source === 'igdb'
        ? (row.ids ?? row.imageIds.split(','))
            .filter((id) => /^[a-zA-Z0-9_-]+$/.test(id))
            .map((id) => ({ provider: 'igdb-shot', id }))
        : row.source.startsWith('plugin:')
          ? (row.images ?? [])
              .filter((image) => image.url)
              .map((image) => ({ provider: `plugin-${row.source.slice(7)}`, id: image.url! }))
          : [],
    )
  const shots = [...new Map(keys.map((key) => [`${key.provider}:${key.id}`, key])).values()]
  useEffect(() => {
    const node = strip.current
    if (!node) return
    function wheel(event: WheelEvent) {
      if (!node) return
      event.preventDefault()
      event.stopPropagation()
      const max = node.scrollWidth - node.clientWidth
      const delta = Math.abs(event.deltaX) > Math.abs(event.deltaY) ? event.deltaX : event.deltaY
      node.scrollLeft = Math.max(
        0,
        Math.min(
          max,
          node.scrollLeft +
            delta * (event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? node.clientWidth : 1),
        ),
      )
    }
    node.addEventListener('wheel', wheel, { passive: false })
    return () => node.removeEventListener('wheel', wheel)
  }, [shots.length])
  const index = shots.findIndex((shot) => `${shot.provider}:${shot.id}` === selected)
  const current = shots[index]
  const move = (delta: number) => {
    if (!open || shots.length < 2) return
    const shot = shots[(index + delta + shots.length) % shots.length]
    if (shot) setSelected(`${shot.provider}:${shot.id}`)
  }
  if (!shots.length) return null
  return (
    <section className="feature-panel">
      <h2>Screenshots</h2>
      <div
        className={`screenshot-strip${previewCount ? ' screenshot-previews' : ''}`}
        aria-label="Screenshots"
        ref={strip}
      >
        {(previewCount ? shots.slice(0, previewCount) : shots).map((shot, index) => (
          <button
            key={`${shot.provider}:${shot.id}`}
            aria-label={`Open screenshot ${index + 1} of ${shots.length}`}
            aria-pressed={`${shot.provider}:${shot.id}` === selected}
            onFocus={(event) => event.currentTarget.scrollIntoView?.({ block: 'nearest', inline: 'nearest' })}
            onClick={(event) => {
              origin.current = event.currentTarget
              setSelected(`${shot.provider}:${shot.id}`)
              setOpen(true)
            }}
          >
            <ScreenshotImage asset={shot} width={previewCount ? 1280 : 400} />
          </button>
        ))}
      </div>
      <p className="muted screenshot-caption">
        {shots.length} {shots.length === 1 ? 'screenshot' : 'screenshots'} from{' '}
        {[
          ...new Set(shots.map((shot) => (shot.provider === 'igdb-shot' ? 'IGDB' : shot.provider.slice(7)))),
        ].join(', ')}
      </p>
      {previewCount && (
        <button
          className="screenshot-gallery-link"
          onClick={(event) => {
            origin.current = event.currentTarget
            if (!selected || !current) setSelected(`${shots[0].provider}:${shots[0].id}`)
            setOpen(true)
          }}
        >
          View gallery →
        </button>
      )}
      <Dialog.Root open={open && Boolean(current)} onOpenChange={setOpen}>
        <Dialog.Portal>
          <Dialog.Overlay className="dialog-overlay" />
          <Dialog.Content
            className="dialog-content screenshot-dialog"
            onOpenAutoFocus={(event) => {
              event.preventDefault()
              closeButton.current?.focus()
            }}
            onCloseAutoFocus={(event) => {
              event.preventDefault()
              if (origin.current?.isConnected) {
                origin.current.scrollIntoView?.({ block: 'nearest', inline: 'nearest' })
                origin.current.focus({ preventScroll: true })
              }
            }}
            onEscapeKeyDown={(event) => {
              event.preventDefault()
              event.stopPropagation()
              setOpen(false)
            }}
            onKeyDown={(event) => {
              if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
                event.preventDefault()
                event.stopPropagation()
                move(event.key === 'ArrowRight' ? 1 : -1)
              }
            }}
          >
            <Dialog.Description className="sr-only">
              Use the previous and next buttons or the arrow keys to browse.
            </Dialog.Description>
            <div className="screenshot-frame">
              {open && current && <ScreenshotImage asset={current} width={1280} />}
              <Dialog.Close
                ref={closeButton}
                className="screenshot-close"
                aria-label="Close screenshots"
                title="Close screenshots"
              >
                <X size={24} aria-hidden="true" />
              </Dialog.Close>
              {shots.length > 1 && (
                <>
                  <button
                    className="screenshot-previous"
                    aria-label="Previous screenshot"
                    title="Previous screenshot"
                    onClick={() => move(-1)}
                  >
                    <ChevronLeft size={24} aria-hidden="true" />
                  </button>
                  <button
                    className="screenshot-next"
                    aria-label="Next screenshot"
                    title="Next screenshot"
                    onClick={() => move(1)}
                  >
                    <ChevronRight size={24} aria-hidden="true" />
                  </button>
                </>
              )}
            </div>
            <Dialog.Title aria-live="polite" className="screenshot-position">
              Screenshot {index + 1} of {shots.length}
            </Dialog.Title>
          </Dialog.Content>
        </Dialog.Portal>
      </Dialog.Root>
    </section>
  )
}

function ScreenshotImage({ asset, width }: { asset: { provider: string; id: string }; width: number }) {
  const query = useQuery({
    queryKey: ['artwork', 'screenshot', asset.provider, asset.id, width],
    staleTime: 120_000,
    retry: false,
    queryFn: async () => {
      let id = asset.id
      if (asset.provider.startsWith('plugin-')) {
        const url = new URL(id)
        if (url.protocol !== 'https:' || url.username || url.password || url.hash || url.port) return null
        id = Array.from(
          new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(url.href))),
        )
          .map((byte) => byte.toString(16).padStart(2, '0'))
          .join('')
      }
      return window.winnow.artwork(asset.provider, id, width)
    },
  })
  return query.data ? (
    <img src={query.data} alt="Game screenshot" />
  ) : (
    <span className="art-placeholder">
      {query.isPending ? 'Loading screenshot…' : 'Screenshot unavailable'}
    </span>
  )
}

export function UpdateSignals({ details, game }: { details?: GameDetails; game?: LibraryGame }) {
  const facts = details as DetailFacts | undefined
  const client = useQueryClient()
  const [busy, setBusy] = useViewState(`updates:${details?.workId}:sending`, false)
  const sending = useRef(false)
  const actionFocus = useRef<HTMLElement | null>(null)
  const [message, setMessage] = useState('')
  const [actionError, setActionError] = useState<unknown>(null)
  const [linkError, setLinkError] = useState<unknown>(null)
  const notice = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (message || actionError || linkError) notice.current?.scrollIntoView?.({ block: 'nearest' })
  }, [message, actionError, linkError])
  useEffect(() => {
    if (busy || !actionFocus.current) return
    const origin = actionFocus.current
    actionFocus.current = null
    const section = notice.current?.parentElement
    // Native disabled buttons lose focus. Restore within this action only if the user has not moved on.
    if (section && (document.activeElement === origin || document.activeElement?.contains(section)))
      section.querySelector<HTMLButtonElement>('.form-actions button')?.focus({ preventScroll: true })
  }, [busy])
  const flags = updateFlagState(
    [...(facts?.events ?? [])].sort(
      (a, b) => Date.parse(b.occurredAt) - Date.parse(a.occurredAt) || b.id - a.id,
    ),
    facts?.acknowledgements ?? {},
    game?.lastPlayedAt,
    game?.playtimeMinutes ?? 0,
  )
  const events = flags.rows
  const standing = Object.keys(facts?.acknowledgements ?? {}).map(Number)
  const available = typeof window.winnow?.request === 'function'
  async function change(restore: boolean) {
    if (busy || sending.current || !available || !details) return
    actionFocus.current =
      document.activeElement instanceof HTMLElement &&
      notice.current?.parentElement?.contains(document.activeElement)
        ? document.activeElement
        : null
    sending.current = true
    setBusy(true)
    setMessage('')
    setActionError(null)
    let stored = 0
    let failed = 0
    let uncertain = false
    try {
      // Capture the entire batch before awaiting; incoming patches belong to the next operation.
      const releaseIds = restore ? standing : flags.unreadReleases
      for (const releaseId of releaseIds) {
        try {
          const result = await request<{ result: string; acknowledgedThrough?: string | null }>(
            restore ? 'updates.restore' : 'updates.acknowledge',
            { releaseId },
            restore
              ? undefined
              : {
                  observedEventIds: events
                    .filter((event) => event.releaseId === releaseId)
                    .map((event) => event.id),
                },
          )
          const saved = restore
            ? result.result === 'Stored' || result.result === 'NothingToDo'
            : result.result === 'Stored' && !!result.acknowledgedThrough
          if (!saved) {
            failed++
            continue
          }
          stored++
          // Cancel older reads before publishing the confirmed watermark into the shared detail cache.
          const queryKey = ['api', 'game.details', { workId: details.workId }]
          await client.cancelQueries({ queryKey })
          client.setQueryData<DetailFacts>(queryKey, (current) => {
            const acknowledgements = { ...(current ?? facts)?.acknowledgements }
            if (restore) delete acknowledgements[releaseId]
            else acknowledgements[releaseId] = result.acknowledgedThrough!
            return { ...(current ?? details), acknowledgements }
          })
        } catch {
          failed++
          uncertain = true
        }
      }
      if (failed)
        setActionError(
          new Error(
            restore
              ? "Couldn't undo that just now."
              : stored
                ? "Couldn't mark every patch read. Try again."
                : uncertain
                  ? "Couldn't confirm that. Check the refreshed flags and try again."
                  : "Couldn't save that — nothing changed.",
          ),
        )
      else
        setMessage(
          stored
            ? restore
              ? 'Update flags restored.'
              : 'These update flags are marked read.'
            : 'No update flags needed changing.',
        )
    } finally {
      // A definite refusal changes nothing. A lost response must reconcile with the server.
      if (stored || uncertain) await client.invalidateQueries({ queryKey: ['api'] })
      sending.current = false
      setBusy(false)
    }
  }
  return (
    <section className="feature-panel">
      <h2>Updates</h2>
      <p className="update-gap-caption">{flags.caption}</p>
      {!events.length ? (
        <Empty>No update signals recorded.</Empty>
      ) : (
        events.map((event) => (
          <article
            className="update-row"
            key={event.id}
            data-unread={event.unread}
            aria-label={`${updateHeadline(event)} · ${dateLabel(event.occurredAt)}${event.unread ? ' · unread' : ''}`}
          >
            <div>
              <time>{dateLabel(event.occurredAt)}</time>
              <h3>
                {event.unread && (
                  <span className="update-unread-dot" aria-hidden="true">
                    ●{' '}
                  </span>
                )}
                {updateHeadline(event)}
              </h3>
            </div>
            {updatePageUrl(event.url) ? (
              <button
                onClick={async () => {
                  try {
                    setLinkError(null)
                    await openExternal(event.url!, { failure: 'inline' })
                  } catch {
                    setLinkError(
                      new Error('Could not open the update. Try again after checking your browser.'),
                    )
                  }
                }}
              >
                Read
              </button>
            ) : (
              <p className="muted">No patch notes page was recorded for this update.</p>
            )}
          </article>
        ))
      )}
      <div className="form-actions">
        {available && flags.unread > 0 && (
          <button className="acknowledge-updates" disabled={busy} onClick={() => void change(false)}>
            {busy ? 'Updating flags…' : 'Mark as read'}
          </button>
        )}
        {available && !flags.unread && !!standing.length && (
          <button disabled={busy} onClick={() => void change(true)}>
            Show it again
          </button>
        )}
      </div>
      {available && (flags.unread > 0 || standing.length > 0) && (
        <p className="muted">
          {flags.unread
            ? 'Removes from Patched. A newer patch puts it back.'
            : 'Marked read. A newer patch will flag it again.'}
        </p>
      )}
      <div ref={notice} className="update-result">
        <Notice error={actionError || linkError} message={message} />
      </div>
    </section>
  )
}

export function AcquisitionSummary({
  ownerships,
}: {
  ownerships?: import('./details-facts').AcquisitionInput[]
}) {
  const facts = acquisitionFacts(ownerships)
  if (!facts) return null
  return (
    <div className="acquisition-facts">
      {facts.dateText && <p>Acquired {facts.dateText}</p>}
      {facts.licenseText && <p>{facts.licenseText}</p>}
    </div>
  )
}

export function LibraryFacts({ game, details }: { game?: LibraryGame; details?: GameDetails }) {
  const facts = details as DetailFacts | undefined
  const workspace = useWorkspace()
  const command = useCommand()
  return (
    <>
      <section className="feature-panel">
        <h2>Your library records</h2>
        {facts?.ownerships?.map((entry) => (
          <article className="metadata-row" key={entry.id}>
            <div>
              <h3>{storeLabel(entry.store)}</h3>
              <AcquisitionSummary ownerships={[entry]} />
              <details>
                <summary>Installation & identifiers</summary>
                <p>{entry.installPath ?? 'Installation path not recorded'}</p>
                <InstallFolderButton
                  ownershipId={entry.id}
                  installed={game?.entries.find((copy) => copy.ownershipId === entry.id)?.installed}
                  installPath={entry.installPath}
                />
                {workspace.data?.externalIds
                  .filter((id) => id.releaseId === entry.releaseId)
                  .map((id) => (
                    <p key={`${id.provider}:${id.providerId}`}>
                      {id.provider}: {id.providerId}
                    </p>
                  ))}
              </details>
            </div>
          </article>
        ))}
        {!facts?.ownerships?.length && <Empty>No acquisition records available.</Empty>}
        {game?.bucket === 'derelict' && (
          <button
            disabled={command.isPending}
            onClick={() =>
              command.mutate({ route: 'library.derelict-exemptions', body: { workIds: [game.workId] } })
            }
          >
            Keep this game in circulation
          </button>
        )}
        <Notice
          error={command.error || workspace.error}
          message={command.isSuccess ? 'This game stays in circulation.' : undefined}
        />
      </section>
      <section className="feature-panel">
        <h2>Store playtime history</h2>
        {!Object.values(facts?.history ?? {}).some((rows) => rows.length) && (
          <Empty>No playtime readings recorded yet.</Empty>
        )}
        {Object.entries(facts?.history ?? {}).map(([ownershipId, rows]) => (
          <details key={ownershipId}>
            <summary>
              {storeLabel(
                game?.entries.find((entry) => entry.ownershipId === Number(ownershipId))?.store ?? 'Edition',
              )}{' '}
              · {rows.length} readings
            </summary>
            <p>{playtimeRecordLine(rows)}</p>
            {[...rows]
              .sort((a, b) => b.observedAt.localeCompare(a.observedAt))
              .map((row) => (
                <p key={row.id}>
                  <time>{dateLabel(row.observedAt)}</time> · {hours(row.playtimeMinutes)}
                </p>
              ))}
          </details>
        ))}
      </section>
    </>
  )
}

const fieldLabels: Record<string, string> = {
  name: 'Name',
  first_release_year: 'Release year',
  summary: 'About',
  publisher: 'Publisher',
  cover_url: 'Cover art',
  background_url: 'Background art',
}
const metadataMessages: Record<string, string> = {
  Applied: 'Saved.',
  InvalidValue: 'Enter a name, or a release year between 1900 and 2200.',
  UnknownField: 'This field is no longer editable.',
  WorkNotFound: 'This game is no longer in your library.',
  FileNotFound: 'The image file could not be found.',
  Unreadable: 'The image could not be read.',
  TooLarge: 'Choose an image no larger than 16 MiB.',
  NotAnImage: 'Choose a supported image file.',
  BadUrl: 'Enter a valid image URL.',
  DownloadFailed: 'The image could not be downloaded.',
  Failed: 'The change could not be saved.',
  Conflict: 'The metadata changed elsewhere. Refresh before saving again.',
}
type FieldDraft = { value: string; revision: string }
export interface MetadataEditorNavigation {
  back(): boolean
  focus(): void
}
const metadataOrder = ['name', 'first_release_year', 'summary', 'cover_url', 'publisher', 'background_url']
const desktopMetadataOrder = [
  'name',
  'first_release_year',
  'publisher',
  'summary',
  'cover_url',
  'background_url',
]
const metadataSource = (source?: string | null) =>
  source === 'user' ? 'YOU' : (source?.toUpperCase() ?? 'AUTO')

export function MetadataEditor({
  workId,
  mode = 'desktop',
  navigation = 'form',
  navigationRef,
  onBusyChange,
  editText,
}: {
  workId: number
  mode?: 'desktop' | 'fullscreen'
  navigation?: 'form' | 'fields'
  navigationRef?: Ref<MetadataEditorNavigation>
  onBusyChange?(busy: boolean): void
  editText?(input: HTMLInputElement | HTMLTextAreaElement): void
}) {
  const [artworkSlot, setArtworkSlot] = useState<ArtworkSlot | null>(null)
  const [activeField, setActiveField] = useState<string | null>(null)
  const [resetField, setResetField] = useState<Metadata['fields'][number] | null>(null)
  const editor = useRef<HTMLElement>(null),
    fieldOrigin = useRef<HTMLButtonElement | null>(null)
  const originalDraft = useRef<FieldDraft | undefined>(undefined)
  const metadata = useApiQuery<Metadata>('metadata.get', { workId })
  const [drafts, setDrafts] = useViewState<Record<string, FieldDraft>>(`draft:metadata-fields:${workId}`, {})
  const [sending, setSending] = useViewState<string | null>(`metadata-fields:${workId}:sending`, null)
  const [messages, setMessages] = useState<Record<string, string>>({})
  const [errors, setErrors] = useState<Record<string, unknown>>({})
  const client = useQueryClient()
  const draft = (field: Metadata['fields'][number]) =>
    drafts[field.field] ?? { value: field.value ?? '', revision: metadata.data!.revision }
  const menu = navigation === 'fields'
  function returnToFields(discard: boolean) {
    if (!activeField) return false
    if (sending) return true
    if (discard) {
      const initial = originalDraft.current
      setDrafts((previous) => {
        const next = { ...previous }
        if (initial) next[activeField] = initial
        else delete next[activeField]
        return next
      })
      setErrors((previous) => ({ ...previous, [activeField]: null }))
    }
    setActiveField(null)
    requestAnimationFrame(() => fieldOrigin.current?.focus({ preventScroll: true }))
    return true
  }
  const focus = () =>
    editor.current
      ?.querySelector<HTMLElement>(
        menu ? '.metadata-field-menu button' : '[data-metadata-field="name"] input',
      )
      ?.focus()
  useImperativeHandle(navigationRef, () => ({ back: () => returnToFields(true), focus }))
  useEffect(() => {
    if (!metadata.data || activeField || artworkSlot) return
    focus()
  }, [Boolean(metadata.data)])
  useEffect(() => {
    onBusyChange?.(Boolean(sending))
  }, [sending, onBusyChange])
  async function save(field: Metadata['fields'][number], action: 'save' | 'reset' | 'upload', file?: File) {
    if (sending || !metadata.data) return
    const current = draft(field)
    const art = ['cover_url', 'background_url'].includes(field.field)
    if (action === 'save' && art && !current.value.trim()) {
      setErrors((previous) => ({ ...previous, [field.field]: new Error(metadataMessages.BadUrl) }))
      return
    }
    if (action === 'save' && field.field === 'name' && !current.value.trim()) {
      setErrors((previous) => ({ ...previous, [field.field]: new Error('Enter a name for this game.') }))
      return
    }
    if (
      action === 'save' &&
      field.field === 'first_release_year' &&
      current.value &&
      (!/^\d{4}$/.test(current.value) || Number(current.value) < 1900 || Number(current.value) > 2200)
    ) {
      setErrors((previous) => ({
        ...previous,
        [field.field]: new Error('Enter a release year between 1900 and 2200.'),
      }))
      return
    }
    setSending(field.field)
    setErrors((previous) => ({ ...previous, [field.field]: null }))
    setMessages((previous) => ({ ...previous, [field.field]: '' }))
    try {
      let content: string | undefined
      if (file) {
        if (file.size > 16 * 1024 * 1024) throw new Error('Choose an image no larger than 16 MiB.')
        content = await new Promise<string>((resolve, reject) => {
          const reader = new FileReader()
          reader.onload = () => resolve(String(reader.result).split(',')[1])
          reader.onerror = () => reject(new Error('The image could not be read.'))
          reader.readAsDataURL(file)
        })
      }
      const route =
        action === 'reset'
          ? 'metadata.reset'
          : action === 'upload'
            ? 'metadata.art-upload'
            : art && current.value
              ? 'metadata.art-download'
              : 'metadata.put'
      const result = await request<{ outcome: string }>(
        route,
        { workId },
        {
          field: field.field,
          expectedRevision: current.revision,
          ...(action === 'reset'
            ? {}
            : content
              ? { content }
              : art && current.value
                ? { url: current.value }
                : { value: current.value || null }),
        },
      )
      if (result.outcome !== 'Applied') {
        setErrors((previous) => ({
          ...previous,
          [field.field]: new Error(metadataMessages[result.outcome] ?? metadataMessages.Failed),
        }))
        return
      }
      setMessages((previous) => ({
        ...previous,
        [field.field]:
          action === 'reset'
            ? `${fieldLabels[field.field] ?? field.field} returned to automatic.`
            : metadataMessages.Applied,
      }))
      if (result.outcome === 'Applied') {
        const before = metadata.data
        const saved = await metadata.refetch()
        setDrafts((previous) => {
          const next = { ...previous }
          delete next[field.field]
          // Advance other drafts only when their saved field stayed unchanged during this write.
          for (const [key, value] of Object.entries(next)) {
            const oldField = before.fields.find((item) => item.field === key),
              newField = saved.data?.fields.find((item) => item.field === key)
            if (
              oldField &&
              newField &&
              oldField.value === newField.value &&
              oldField.source === newField.source &&
              value.revision === before.revision
            )
              next[key] = { ...value, revision: saved.data!.revision }
          }
          return next
        })
        await client.invalidateQueries({
          predicate: (query) => query.queryKey[0] === 'api' && query.queryKey[1] !== 'metadata.get',
        })
        if (art)
          await client.invalidateQueries({
            predicate: (query) => ['artwork', 'artwork-image'].includes(String(query.queryKey[0])),
          })
        if (menu) {
          setActiveField(null)
          requestAnimationFrame(() => fieldOrigin.current?.focus({ preventScroll: true }))
        }
      }
    } catch (failure) {
      setErrors((previous) => ({ ...previous, [field.field]: failure }))
      if (failure instanceof ApiError && failure.uncertain)
        void client.invalidateQueries({ queryKey: ['api'] })
      else if (failure instanceof ApiError && failure.conflict) void metadata.refetch()
    } finally {
      setSending(null)
    }
  }
  return (
    <section ref={editor} className={`feature-panel metadata-editor mode-${mode}`}>
      <h2>Metadata & sources</h2>
      <p className="muted">
        Save each field separately. Fields you edit stay yours until you return them to automatic updates.
      </p>
      <Notice error={metadata.error} />
      {metadata.isPending && <p role="status">Loading fields…</p>}
      {menu && (
        <div className="metadata-field-menu" hidden={activeField !== null}>
          {[...(metadata.data?.fields ?? [])]
            .sort((a, b) => metadataOrder.indexOf(a.field) - metadataOrder.indexOf(b.field))
            .map((field) => (
              <button
                key={field.field}
                disabled={Boolean(sending)}
                onClick={(event) => {
                  fieldOrigin.current = event.currentTarget
                  if (field.field === 'cover_url' || field.field === 'background_url')
                    setArtworkSlot(field.field === 'cover_url' ? 'Cover' : 'Hero')
                  else {
                    originalDraft.current = drafts[field.field]
                    setActiveField(field.field)
                    requestAnimationFrame(() =>
                      editor.current
                        ?.querySelector<HTMLElement>(
                          `[data-metadata-field="${field.field}"] .metadata-edit-value`,
                        )
                        ?.focus(),
                    )
                  }
                }}
              >
                {fieldLabels[field.field] ?? field.field} · {metadataSource(field.source)}
              </button>
            ))}
          <Notice message={Object.values(messages).filter(Boolean).at(-1)} />
        </div>
      )}
      <div
        className={`metadata-fields-grid ${menu ? 'field-page' : 'all-fields'}`}
        hidden={menu && activeField === null}
      >
        {[...(metadata.data?.fields ?? [])]
          .sort(
            (a, b) =>
              (menu ? metadataOrder : desktopMetadataOrder).indexOf(a.field) -
              (menu ? metadataOrder : desktopMetadataOrder).indexOf(b.field),
          )
          .map((field) => {
            const current = draft(field),
              label = fieldLabels[field.field] ?? field.field
            const error = errors[field.field],
              conflict = error instanceof ApiError && error.conflict
            const art = ['cover_url', 'background_url'].includes(field.field)
            return (
              <form
                className="editor-form"
                noValidate
                key={field.field}
                data-metadata-field={field.field}
                hidden={menu && activeField !== field.field}
                onSubmit={(e) => {
                  e.preventDefault()
                  void save(field, 'save')
                }}
              >
                <div className="feature-heading">
                  <h3>{label}</h3>
                  <small
                    title={
                      field.source === 'user'
                        ? 'You own this field. Automatic enrichment leaves it alone.'
                        : `Last supplied by ${field.source ?? 'no provider'}.`
                    }
                  >
                    {metadataSource(field.source)}
                  </small>
                </div>
                {art && (
                  <Artwork
                    workId={workId}
                    hero={field.field === 'background_url'}
                    className="metadata-art-preview"
                  />
                )}
                <label className="field">
                  <span className="sr-only">{label}</span>
                  {field.field === 'summary' ? (
                    <textarea
                      rows={5}
                      disabled={Boolean(sending)}
                      value={current.value}
                      onChange={(e) =>
                        setDrafts({ ...drafts, [field.field]: { ...current, value: e.target.value } })
                      }
                    />
                  ) : (
                    <input
                      autoFocus={!menu && field.field === 'name'}
                      type="text"
                      inputMode={field.field === 'first_release_year' ? 'numeric' : undefined}
                      min={field.field === 'first_release_year' ? 1900 : undefined}
                      max={field.field === 'first_release_year' ? 2200 : undefined}
                      required={field.field === 'name'}
                      disabled={Boolean(sending)}
                      value={current.value}
                      onChange={(e) =>
                        setDrafts({ ...drafts, [field.field]: { ...current, value: e.target.value } })
                      }
                    />
                  )}
                </label>
                {menu && (
                  <button
                    type="button"
                    className="metadata-edit-value"
                    data-controller-context={activeField === field.field || undefined}
                    disabled={Boolean(sending)}
                    onClick={(event) => {
                      const input = event.currentTarget
                        .closest('form')
                        ?.querySelector<HTMLInputElement | HTMLTextAreaElement>('input, textarea')
                      input?.focus()
                      if (input) editText?.(input)
                    }}
                  >
                    Edit value
                  </button>
                )}
                {conflict && (
                  <div className="conflict-panel">
                    <p>This game changed elsewhere. Your {label.toLowerCase()} draft is preserved.</p>
                    <button type="button" disabled={Boolean(sending)} onClick={() => void metadata.refetch()}>
                      Refresh saved metadata
                    </button>
                    {current.revision !== metadata.data!.revision && (
                      <button
                        type="button"
                        onClick={() => {
                          setDrafts({
                            ...drafts,
                            [field.field]: { ...current, revision: metadata.data!.revision },
                          })
                          setErrors({ ...errors, [field.field]: null })
                        }}
                      >
                        Keep this draft for the next save
                      </button>
                    )}
                  </div>
                )}
                <div className="form-actions">
                  {art && (
                    <button
                      type="button"
                      disabled={Boolean(sending)}
                      onClick={() => setArtworkSlot(field.field === 'cover_url' ? 'Cover' : 'Hero')}
                    >
                      Browse {field.field === 'cover_url' ? 'cover' : 'background'} artwork
                    </button>
                  )}
                  <button disabled={Boolean(sending) || conflict}>
                    {sending === field.field ? 'Saving…' : `Save ${label.toLowerCase()}`}
                  </button>
                  {field.source === 'user' && (
                    <button
                      type="button"
                      disabled={Boolean(sending) || conflict}
                      onClick={() => (menu ? setResetField(field) : void save(field, 'reset'))}
                    >
                      Use automatic {label.toLowerCase()}
                    </button>
                  )}
                  {menu && (
                    <button type="button" disabled={Boolean(sending)} onClick={() => returnToFields(true)}>
                      Cancel
                    </button>
                  )}
                  {drafts[field.field] && (
                    <button
                      type="button"
                      disabled={Boolean(sending)}
                      onClick={() => {
                        const next = { ...drafts }
                        delete next[field.field]
                        setDrafts(next)
                        setErrors({ ...errors, [field.field]: null })
                      }}
                    >
                      Discard {label.toLowerCase()} draft
                    </button>
                  )}
                </div>
                {art && (
                  <label className="field">
                    Choose {label.toLowerCase()} file
                    <input
                      type="file"
                      accept="image/*"
                      disabled={Boolean(sending) || conflict}
                      onChange={(e) => {
                        const file = e.target.files?.[0]
                        e.target.value = ''
                        if (file) void save(field, 'upload', file)
                      }}
                    />
                  </label>
                )}
                <Notice error={error} message={messages[field.field]} />
              </form>
            )
          })}
      </div>
      {resetField && (
        <Dialog.Root
          open
          onOpenChange={(open) => {
            if (!open && !sending) setResetField(null)
          }}
        >
          <Dialog.Portal>
            <Dialog.Overlay className="dialog-overlay metadata-confirm-overlay" />
            <Dialog.Content
              className="dialog-content metadata-confirm"
              aria-describedby={undefined}
              onOpenAutoFocus={(event) => {
                event.preventDefault()
                document.querySelector<HTMLButtonElement>('.metadata-confirm [data-cancel]')?.focus()
              }}
              onCloseAutoFocus={(event) => {
                event.preventDefault()
                if (activeField)
                  editor.current
                    ?.querySelector<HTMLElement>(
                      `[data-metadata-field="${activeField}"] .metadata-edit-value`,
                    )
                    ?.focus()
                else fieldOrigin.current?.focus()
              }}
              onEscapeKeyDown={(event) => {
                if (sending) event.preventDefault()
              }}
            >
              <Dialog.Title>Reset {fieldLabels[resetField.field]}?</Dialog.Title>
              <div className="form-actions">
                <button data-cancel disabled={Boolean(sending)} onClick={() => setResetField(null)}>
                  Cancel
                </button>
                <button
                  disabled={Boolean(sending)}
                  onClick={async () => {
                    await save(resetField, 'reset')
                    setResetField(null)
                  }}
                >
                  Use automatic {fieldLabels[resetField.field].toLowerCase()}
                </button>
              </div>
            </Dialog.Content>
          </Dialog.Portal>
        </Dialog.Root>
      )}
      {artworkSlot && (
        <ArtworkBrowserDialog
          workId={workId}
          title={metadata.data?.title ?? 'Game artwork'}
          mode={mode}
          initialSlot={artworkSlot}
          onClose={() => setArtworkSlot(null)}
        />
      )}
    </section>
  )
}
