import { useEffect, useRef, useState } from 'react'
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
import { acquisitionFacts, playtimeRecordLine, updatePageUrl } from './details-facts'
import { timelineUpdates } from './activity-timeline-model'
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
  const command = useCommand<{ result: string }>()
  const [busy, setBusy] = useViewState(`updates:${details?.workId}:sending`, false)
  const [message, setMessage] = useState('')
  const [actionError, setActionError] = useState<unknown>(null)
  const [linkError, setLinkError] = useState<unknown>(null)
  const events = timelineUpdates(
    facts?.events ?? [],
    facts?.acknowledgements ?? {},
    game?.lastPlayedAt,
    game?.playtimeMinutes ?? 0,
  )
  const standing = Object.keys(facts?.acknowledgements ?? {}).map(Number)
  async function change(restore: boolean) {
    if (busy) return
    setBusy(true)
    setMessage('')
    setActionError(null)
    try {
      // Each release owns its watermark. Finish the captured observed set before reporting completion.
      const releaseIds = restore ? standing : [...new Set(events.map((event) => event.releaseId))]
      let stored = 0
      let failed = 0
      for (const releaseId of releaseIds) {
        const result = await command.mutateAsync({
          route: restore ? 'updates.restore' : 'updates.acknowledge',
          params: { releaseId },
          ...(restore
            ? {}
            : {
                body: {
                  observedEventIds: events
                    .filter((event) => event.releaseId === releaseId)
                    .map((event) => event.id),
                },
              }),
        })
        if (result.result === 'Stored') stored++
        if (result.result === 'NotStored') failed++
      }
      if (failed)
        setActionError(
          new Error('Some update flags could not be saved. Check the refreshed flags and try again.'),
        )
      else
        setMessage(
          stored
            ? restore
              ? 'Update flags restored.'
              : 'These update flags are marked read.'
            : 'No update flags needed changing.',
        )
    } catch {
      /* Completed release writes stay visible after the normal refresh. */
    } finally {
      setBusy(false)
    }
  }
  return (
    <section className="feature-panel">
      <h2>Updates</h2>
      {!events.length ? (
        <Empty>No update signals recorded.</Empty>
      ) : (
        events.map((event) => (
          <article
            className="update-row"
            key={event.id}
            data-unread={event.unread}
            aria-label={`${event.title ?? event.kind.replaceAll('_', ' ')} · ${dateLabel(event.occurredAt)}${event.unread ? ' · unread' : ''}`}
          >
            <div>
              <time>{dateLabel(event.occurredAt)}</time>
              <h3>
                {event.unread && (
                  <span className="update-unread-dot" aria-hidden="true">
                    ●{' '}
                  </span>
                )}
                {event.title ?? event.kind.replaceAll('_', ' ')}
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
        {!!events.length && (
          <button className="acknowledge-updates" disabled={busy} onClick={() => void change(false)}>
            {busy ? 'Updating flags…' : 'Mark these updates read'}
          </button>
        )}
        {!!standing.length && (
          <button disabled={busy} onClick={() => void change(true)}>
            Restore update flags
          </button>
        )}
      </div>
      <Notice error={command.error || actionError || linkError} message={message} />
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

export function MetadataEditor({ workId }: { workId: number }) {
  const metadata = useApiQuery<Metadata>('metadata.get', { workId })
  const [drafts, setDrafts] = useViewState<Record<string, FieldDraft>>(`draft:metadata-fields:${workId}`, {})
  const [sending, setSending] = useViewState<string | null>(`metadata-fields:${workId}:sending`, null)
  const [messages, setMessages] = useState<Record<string, string>>({})
  const [errors, setErrors] = useState<Record<string, unknown>>({})
  const client = useQueryClient()
  const draft = (field: Metadata['fields'][number]) =>
    drafts[field.field] ?? { value: field.value ?? '', revision: metadata.data!.revision }
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
    <section className="feature-panel">
      <h2>Metadata & sources</h2>
      <p className="muted">
        Save each field separately. Fields you edit stay yours until you return them to automatic updates.
      </p>
      <Notice error={metadata.error} />
      {metadata.isPending && <p role="status">Loading fields…</p>}
      {metadata.data?.fields.map((field) => {
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
                {field.source === 'user' ? 'YOU' : (field.source?.toUpperCase() ?? 'AUTO')}
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
              {label}
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
                  autoFocus={field.field === 'name'}
                  type={field.field === 'first_release_year' ? 'number' : 'text'}
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
              <button disabled={Boolean(sending) || conflict}>
                {sending === field.field ? 'Saving…' : `Save ${label.toLowerCase()}`}
              </button>
              {field.source === 'user' && (
                <button
                  type="button"
                  disabled={Boolean(sending) || conflict}
                  onClick={() => void save(field, 'reset')}
                >
                  Use automatic {label.toLowerCase()}
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
    </section>
  )
}
