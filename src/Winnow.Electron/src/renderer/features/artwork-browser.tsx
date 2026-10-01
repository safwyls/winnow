import { useEffect, useLayoutEffect, useRef, useState, type CSSProperties } from 'react'
import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query'
import * as Dialog from '@radix-ui/react-dialog'
import { ApiError, openExternal, request } from '../api/client'
import type { ArtworkCandidate, ArtworkPage, ArtworkState, CoverKey, Mode } from '../api/types'
import type { ArtworkSaveResult } from '../../shared/bridge'
import { updatePageUrl } from './details-facts'
import { Empty, Notice } from './shared'
import { useArtworkSelection } from '../components/Artwork'
import leftTrigger from './assets/xbox_lt_outline.svg?raw'
import rightTrigger from './assets/xbox_rt_outline.svg?raw'
import selectButton from './assets/xbox_button_a_outline.svg?raw'
import backButton from './assets/xbox_button_b_outline.svg?raw'
import keyboardButton from './assets/xbox_button_y_outline.svg?raw'
import './artwork-browser.css'

export const artworkSlots = ['Hero', 'Cover', 'Icon'] as const
export type ArtworkSlot = (typeof artworkSlots)[number]
const controlArtwork = {
  LT: leftTrigger,
  RT: rightTrigger,
  A: selectButton,
  B: backButton,
  Y: keyboardButton,
}
function ArtworkControlGlyph({ button }: { button: keyof typeof controlArtwork }) {
  return (
    <span
      className="artwork-control-glyph"
      data-artwork-glyph={button}
      aria-hidden="true"
      dangerouslySetInnerHTML={{
        __html: controlArtwork[button].replace(
          '<svg ',
          `<svg viewBox="${button.length === 2 ? '0 0 64 64' : '8 8 48 48'}" `,
        ),
      }}
    />
  )
}
interface ArtworkSource {
  id: string
  name: string
  slots: (number | ArtworkSlot)[]
}
type BrowseState = { source: string; selected: ArtworkCandidate | null; scroll: number }
const freshSlot = (): BrowseState => ({ source: 'all', selected: null, scroll: 0 })
const sameArtwork = (a?: ArtworkCandidate | null, b?: ArtworkCandidate | null) =>
  Boolean(a && b && a.sourceId === b.sourceId && a.assetId === b.assetId)
export function artworkDescription(candidate: ArtworkCandidate) {
  return [
    candidate.sourceName,
    candidate.width && candidate.height ? `${candidate.width} × ${candidate.height}` : null,
    candidate.creator,
  ]
    .filter(Boolean)
    .join(' · ')
}

export function ArtworkImage({
  imageKey,
  preview = false,
  className = '',
}: {
  imageKey?: CoverKey | null
  preview?: boolean
  className?: string
}) {
  const image = useQuery({
    queryKey: ['artwork-image', imageKey, preview ? 1200 : 400],
    enabled: Boolean(imageKey),
    staleTime: Infinity,
    retry: false,
    queryFn: async ({ signal }) => {
      const requestId = crypto.randomUUID().replaceAll('-', '')
      const cancel = () => {
        void window.winnow.cancelRequest?.(requestId)
      }
      signal.addEventListener('abort', cancel, { once: true })
      try {
        const value = await window.winnow.artwork(
          imageKey!.provider,
          imageKey!.id,
          preview ? 1200 : 400,
          requestId,
        )
        signal.throwIfAborted()
        return value
      } finally {
        signal.removeEventListener('abort', cancel)
      }
    },
  })
  return image.data ? (
    <img className={className} src={image.data} alt={preview ? 'Selected artwork preview' : ''} />
  ) : (
    <span className="artwork-browser-placeholder">
      {image.isFetching ? 'Loading artwork…' : 'Preview unavailable'}
    </span>
  )
}

export function ArtworkBrowserDialog({
  workId,
  coverWorkId,
  title,
  mode,
  initialSlot = 'Hero',
  onClose,
}: {
  workId: number
  coverWorkId?: number
  title: string
  mode: Mode
  initialSlot?: ArtworkSlot
  onClose(): void
}) {
  const busy = useRef(false),
    root = useRef<HTMLDivElement>(null)
  const origin = useRef(document.activeElement instanceof HTMLElement ? document.activeElement : null)
  const [desktopRatio] = useState(() => {
    const bounds = document.querySelector('.avalon-details.desktop')?.getBoundingClientRect()
    return bounds?.height ? bounds.width / bounds.height : 4 / 3
  })
  return (
    <Dialog.Root
      open
      onOpenChange={(open) => {
        if (!open && !busy.current) onClose()
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay artwork-browser-scrim" />
        <Dialog.Content
          ref={root}
          className={`artwork-browser-dialog mode-${mode}`}
          aria-describedby={undefined}
          onEscapeKeyDown={(event) => {
            event.preventDefault()
            event.stopPropagation()
            if (!busy.current) onClose()
          }}
          onOpenAutoFocus={(event) => {
            event.preventDefault()
            root.current?.querySelector<HTMLButtonElement>(`[data-artwork-slot="${initialSlot}"]`)?.focus()
          }}
          onCloseAutoFocus={(event) => {
            event.preventDefault()
            if (origin.current?.isConnected) origin.current.focus({ preventScroll: true })
          }}
        >
          <ArtworkBrowser
            workId={workId}
            coverWorkId={coverWorkId}
            title={title}
            mode={mode}
            initialSlot={initialSlot}
            desktopRatio={desktopRatio}
            onClose={onClose}
            onBusyChange={(value) => {
              busy.current = value
            }}
          />
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}

export function ArtworkBrowser({
  workId,
  coverWorkId,
  title = 'Artwork',
  mode = 'desktop',
  initialSlot = 'Hero',
  desktopRatio = 4 / 3,
  onClose,
  onBusyChange,
}: {
  workId: number
  coverWorkId?: number
  title?: string
  mode?: Mode
  initialSlot?: ArtworkSlot
  desktopRatio?: number
  onClose?(): void
  onBusyChange?(busy: boolean): void
}) {
  const [editingUrl, setEditingUrl] = useState(false)
  const [slot, setSlot] = useState<ArtworkSlot>(initialSlot)
  const [slots, setSlots] = useState<Record<ArtworkSlot, BrowseState>>({
    Hero: freshSlot(),
    Cover: freshSlot(),
    Icon: freshSlot(),
  })
  const [url, setUrl] = useState(''),
    [crop, setCrop] = useState<'desktop' | 'fullscreen'>('desktop')
  const [busy, setBusy] = useState(false),
    [message, setMessage] = useState(''),
    [error, setError] = useState<unknown>(null)
  const [refreshFailed, setRefreshFailed] = useState(false)
  const busyRef = useRef(false),
    mounted = useRef(true),
    gallery = useRef<HTMLDivElement>(null),
    slotRow = useRef<HTMLDivElement>(null)
  const scrolls = useRef<Record<ArtworkSlot, number>>({ Hero: 0, Cover: 0, Icon: 0 })
  const client = useQueryClient()
  const current = useQuery({
    queryKey: ['api', 'artwork.get', { workId, slot }],
    queryFn: ({ signal }) => request<ArtworkState>('artwork.get', { workId, slot }, undefined, signal),
    staleTime: 0,
    retry: false,
  })
  const sources = useQuery({
    queryKey: ['api', 'artwork.sources'],
    queryFn: ({ signal }) => request<ArtworkSource[]>('artwork.sources', undefined, undefined, signal),
    staleTime: 30_000,
    retry: false,
  })
  const preferences = useQuery({
    queryKey: ['api', 'preferences.get'],
    queryFn: ({ signal }) => request<Record<string, string>>('preferences.get', undefined, undefined, signal),
    staleTime: 30_000,
    retry: false,
  })
  const automaticCover =
    Boolean(current.data) &&
    slot === 'Cover' &&
    (!current.data?.current || current.data.current.sourceId === 'automatic')
  const displayed = useArtworkSelection(coverWorkId ?? workId, false, automaticCover)
  const displayedKey = displayed.data?.selection?.current?.previewKey
  const currentCandidate: ArtworkCandidate | null | undefined =
    automaticCover && displayedKey
      ? {
          sourceId: 'automatic',
          sourceName: 'Automatic',
          assetId: `${displayedKey.provider}:${displayedKey.id}`,
          previewKey: displayedKey,
          isCurrent: true,
        }
      : current.data?.current
  const selected = slots[slot].selected ?? currentCandidate
  const isCurrent = sameArtwork(selected, currentCandidate)
  const needsRefresh = refreshFailed || (error instanceof ApiError && (error.conflict || error.uncertain))
  const writable = Boolean(current.data) && !current.isFetching && !current.error && !needsRefresh && !busy
  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])
  useLayoutEffect(() => {
    const node = gallery.current
    if (!node) return
    node.scrollTop = scrolls.current[slot]
    const record = () => {
      scrolls.current[slot] = node.scrollTop
    }
    node.addEventListener('scroll', record)
    return () => {
      record()
      node.removeEventListener('scroll', record)
    }
  }, [slot])
  function chooseSlot(next: ArtworkSlot, focus = false) {
    if (busyRef.current) return
    setSlot(next)
    setError(null)
    setRefreshFailed(false)
    if (focus) slotRow.current?.querySelector<HTMLButtonElement>(`[data-artwork-slot="${next}"]`)?.focus()
  }
  function patchSlot(patch: Partial<BrowseState>) {
    setSlots((value) => ({ ...value, [slot]: { ...value[slot], ...patch } }))
  }
  async function change(route: 'artwork.put' | 'artwork.reset' | 'artwork.url' | 'file') {
    if (!writable || busyRef.current || !current.data) return
    if (route === 'artwork.put' && (!selected?.offerId || isCurrent)) return
    if (route === 'artwork.url' && !updatePageUrl(url.trim())) {
      setError(new Error('Enter a valid image URL.'))
      return
    }
    busyRef.current = true
    setBusy(true)
    onBusyChange?.(true)
    setError(null)
    setMessage('Saving artwork…')
    let committed = false
    try {
      let result: ArtworkSaveResult
      if (route === 'file') {
        const reply = await window.winnow.importArtwork?.({ workId, slot, revision: current.data.revision })
        if (!reply) {
          setMessage('')
          return
        }
        if (!reply.ok)
          throw new ApiError(reply.status, reply.message ?? 'The artwork could not be imported.', reply.data)
        result = reply.data!
      } else
        result = await request<ArtworkSaveResult>(
          route,
          { workId, slot },
          {
            revision: current.data.revision,
            ...(route === 'artwork.put'
              ? { offerId: selected!.offerId }
              : route === 'artwork.url'
                ? { url: url.trim() }
                : {}),
          },
        )
      if (!mounted.current) return
      if (!result.success) {
        setMessage('')
        setError(new Error(result.message))
        return
      }
      committed = true
      setMessage(result.message)
      // Refresh library projections before replacing Current; a completed write is never retried as a read failure.
      await Promise.all(
        ['library.get', 'library.workspace'].map((name) =>
          client.invalidateQueries({ queryKey: ['api', name] }, { throwOnError: true }),
        ),
      )
      await Promise.all([
        client.invalidateQueries({ queryKey: ['artwork'] }),
        client.invalidateQueries({ queryKey: ['artwork-image'] }),
      ])
      await current.refetch({ throwOnError: true })
      patchSlot({ selected: null })
    } catch (failure) {
      if (!mounted.current) return
      if (!committed) setMessage('')
      setRefreshFailed(committed)
      setError(
        committed
          ? new Error('Artwork saved, but the library could not refresh. Reopen this game to refresh it.')
          : failure,
      )
    } finally {
      busyRef.current = false
      onBusyChange?.(false)
      if (mounted.current) setBusy(false)
    }
  }
  const sourceLink = updatePageUrl(selected?.pageUrl)
  const previewRatio =
    slot === 'Cover' ? 2 / 3 : crop === 'fullscreen' ? 16 / 9 : mode === 'fullscreen' ? 4 / 3 : desktopRatio
  return (
    <section className={`artwork-browser mode-${mode}`} data-artwork-slot-active={slot}>
      <header className="artwork-browser-heading">
        {onClose && (
          <button disabled={busy} onClick={onClose}>
            Back
          </button>
        )}
        {onClose ? <Dialog.Title>{title}</Dialog.Title> : <h2>{title}</h2>}
        <p>Change artwork</p>
      </header>
      <div className="artwork-browser-controls">
        <div
          ref={slotRow}
          className="artwork-browser-slots"
          aria-label="Artwork slots"
          data-controller-page
          onKeyDown={(event) => {
            if (!['PageUp', 'PageDown'].includes(event.key)) return
            event.preventDefault()
            event.stopPropagation()
            chooseSlot(
              artworkSlots[(artworkSlots.indexOf(slot) + (event.key === 'PageDown' ? 1 : 2)) % 3],
              true,
            )
          }}
        >
          {mode === 'fullscreen' && <ArtworkControlGlyph button="LT" />}
          {artworkSlots.map((name) => (
            <button
              key={name}
              data-artwork-slot={name}
              aria-label={`${name} artwork`}
              aria-pressed={slot === name}
              disabled={busy}
              onClick={() => chooseSlot(name)}
            >
              {name}
            </button>
          ))}
          {mode === 'fullscreen' && <ArtworkControlGlyph button="RT" />}
        </div>
        <nav className="artwork-browser-sources" aria-label="Artwork sources">
          <button
            aria-pressed={slots[slot].source === 'all'}
            disabled={busy}
            onClick={() => patchSlot({ source: 'all' })}
          >
            All sources
          </button>
          {sources.data?.map((source) => (
            <button
              key={source.id}
              aria-pressed={slots[slot].source === source.id}
              disabled={busy}
              onClick={() => patchSlot({ source: source.id })}
            >
              {source.name}
            </button>
          ))}
        </nav>
      </div>
      <div className="artwork-browser-body">
        <div ref={gallery} className="artwork-browser-gallery" tabIndex={-1}>
          {currentCandidate && (
            <ArtworkChoice
              candidate={currentCandidate}
              selected={isCurrent}
              current
              disabled={busy}
              onSelect={() => patchSlot({ selected: null })}
            />
          )}
          {sources.data?.map((source) => (
            <ArtworkSourceGroup
              key={`${slot}:${source.id}`}
              workId={workId}
              slot={slot}
              source={source}
              visible={slots[slot].source === 'all' || slots[slot].source === source.id}
              selected={selected}
              current={currentCandidate}
              disabled={busy}
              onSelect={(candidate) => patchSlot({ selected: candidate })}
            />
          ))}
          {sources.isPending && <Empty>Loading artwork sources…</Empty>}
          {sources.data?.length === 0 && <Empty>No artwork sources available.</Empty>}
          {sources.error && (
            <>
              <Notice error={sources.error} />
              <button onClick={() => void sources.refetch()}>Retry artwork sources</button>
            </>
          )}
        </div>
        <section className="artwork-browser-preview" aria-label="Artwork preview">
          <header>
            <h3>Preview</h3>
            <p>{selected ? artworkDescription(selected) : 'Select artwork to preview it.'}</p>
            {sourceLink && (
              <button
                onClick={async () => {
                  try {
                    const result = await openExternal(sourceLink, { failure: 'inline' })
                    setMessage(result.message ?? 'Opened in your browser.')
                  } catch (failure) {
                    setError(failure)
                  }
                }}
              >
                Open artwork source
              </button>
            )}
          </header>
          {slot === 'Hero' && (
            <div className="artwork-crop-controls">
              <button aria-pressed={crop === 'desktop'} onClick={() => setCrop('desktop')}>
                Desktop crop{mode === 'fullscreen' ? ' · 4:3' : ''}
              </button>
              <button aria-pressed={crop === 'fullscreen'} onClick={() => setCrop('fullscreen')}>
                Fullscreen crop · 16:9
              </button>
            </div>
          )}
          {slot === 'Icon' ? (
            <div className="artwork-icon-previews">
              <div className="artwork-icon-small">
                <ArtworkImage imageKey={selected?.previewKey} preview />
                <span>Icon · 32 px</span>
              </div>
              <p>Transparency on light and dark</p>
              <div className="artwork-icon-pair">
                <div>
                  <ArtworkImage imageKey={selected?.previewKey} preview />
                </div>
                <div>
                  <ArtworkImage imageKey={selected?.previewKey} preview />
                </div>
              </div>
            </div>
          ) : (
            <div
              className="artwork-preview-stage"
              style={{ '--artwork-ratio': previewRatio } as CSSProperties}
            >
              <div
                className={`artwork-preview-frame slot-${slot.toLowerCase()}`}
                data-cover-fit={preferences.data?.CoverArtMode === 'fill' ? 'fill' : 'fit'}
                style={{
                  aspectRatio:
                    slot === 'Cover'
                      ? '2 / 3'
                      : crop === 'fullscreen'
                        ? '16 / 9'
                        : String(mode === 'fullscreen' ? 4 / 3 : desktopRatio),
                }}
              >
                <ArtworkImage imageKey={selected?.previewKey} preview />
              </div>
            </div>
          )}
        </section>
      </div>
      <footer className="artwork-browser-actions">
        <Notice message={message} />
        <Notice error={error || current.error} />
        {(needsRefresh || current.error) && (
          <button
            disabled={busy}
            onClick={async () => {
              try {
                await current.refetch({ throwOnError: true })
                setError(null)
                setRefreshFailed(false)
                if (refreshFailed) patchSlot({ selected: null })
              } catch (failure) {
                setError(failure)
              }
            }}
          >
            Refresh current artwork
          </button>
        )}
        <form
          onSubmit={(event) => {
            event.preventDefault()
            void change('artwork.url')
          }}
        >
          <label className="field">
            Image URL
            <input
              aria-label="Artwork image URL"
              value={url}
              disabled={busy}
              onFocus={() => setEditingUrl(true)}
              onBlur={() => setEditingUrl(false)}
              onChange={(event) => setUrl(event.target.value)}
            />
          </label>
          <button disabled={!writable}>Import URL</button>
        </form>
        <div className="form-actions">
          <button
            className="primary-button"
            disabled={!writable || !selected?.offerId || isCurrent}
            onClick={() => void change('artwork.put')}
          >
            Use artwork
          </button>
          <button disabled={!writable} onClick={() => void change('artwork.reset')}>
            Use automatic
          </button>
          {window.winnow.importArtwork && (
            <button disabled={!writable} onClick={() => void change('file')}>
              Choose file
            </button>
          )}
        </div>
        {mode === 'fullscreen' && (
          <div className="artwork-controller-hints" role="group" aria-label="Artwork controls">
            <span>
              <ArtworkControlGlyph button="LT" />
              <ArtworkControlGlyph button="RT" />
              <span className="sr-only">LT / RT </span>Artwork type
            </span>
            <span>
              <ArtworkControlGlyph button="A" />
              <span className="sr-only">A </span>Preview or select
            </span>
            <span>
              <ArtworkControlGlyph button="B" />
              <span className="sr-only">B </span>Back
            </span>
            {editingUrl && !busy && (
              <span>
                <ArtworkControlGlyph button="Y" />
                <span className="sr-only">Y </span>Keyboard
              </span>
            )}
          </div>
        )}
      </footer>
    </section>
  )
}

function ArtworkSourceGroup({
  workId,
  slot,
  source,
  visible,
  selected,
  current,
  disabled,
  onSelect,
}: {
  workId: number
  slot: ArtworkSlot
  source: ArtworkSource
  visible: boolean
  selected?: ArtworkCandidate | null
  current?: ArtworkCandidate | null
  disabled: boolean
  onSelect(candidate: ArtworkCandidate): void
}) {
  const supported = source.slots.includes(artworkSlots.indexOf(slot)) || source.slots.includes(slot)
  const page = useInfiniteQuery({
    queryKey: ['api', 'artwork.browse', workId, slot, source.id],
    initialPageParam: null as string | null,
    queryFn: ({ pageParam, signal }) =>
      request<ArtworkPage>(
        'artwork.browse',
        { workId, slot, source: source.id, ...(pageParam ? { cursor: pageParam } : {}) },
        undefined,
        signal,
      ),
    enabled: supported,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
    staleTime: 5 * 60_000,
    retry: false,
  })
  const candidates = [
    ...new Map(
      (page.data?.pages.flatMap((part) => part.items) ?? []).map((candidate) => [
        `${candidate.sourceId}:${candidate.assetId}`,
        candidate,
      ]),
    ).values(),
  ]
  const last = page.data?.pages.at(-1)
  return (
    <section className="artwork-source-group" hidden={!visible} aria-label={`${source.name} artwork`}>
      <h3>{source.name}</h3>
      {!supported ? (
        <p>
          {source.name} does not offer {slot.toLowerCase()} artwork.
        </p>
      ) : (
        <>
          {page.isPending && <p>Loading artwork…</p>}
          <div className="artwork-browser-candidates">
            {candidates.map((candidate) => (
              <ArtworkChoice
                key={`${candidate.sourceId}:${candidate.assetId}`}
                candidate={candidate}
                selected={sameArtwork(candidate, selected)}
                current={sameArtwork(candidate, current)}
                disabled={disabled}
                onSelect={() => onSelect(candidate)}
              />
            ))}
          </div>
          {last?.message && <p>{last.message}</p>}
          {!page.isPending && !page.error && !last?.message && !candidates.length && (
            <p>No artwork available for this game.</p>
          )}
          {page.error && <p role="alert">Could not load artwork. Try again.</p>}
          {(page.error || last?.canRetry) && (
            <button disabled={disabled || page.isFetching} onClick={() => void page.refetch()}>
              Retry {source.name}
            </button>
          )}
          {page.hasNextPage && (
            <button disabled={disabled || page.isFetching} onClick={() => void page.fetchNextPage()}>
              {page.isFetchingNextPage ? 'Loading artwork…' : `Load more ${source.name}`}
            </button>
          )}
        </>
      )}
    </section>
  )
}
function ArtworkChoice({
  candidate,
  selected,
  current,
  disabled,
  onSelect,
}: {
  candidate: ArtworkCandidate
  selected: boolean
  current: boolean
  disabled: boolean
  onSelect(): void
}) {
  return (
    <button
      className="artwork-choice"
      aria-label={`${artworkDescription(candidate)} · ${candidate.assetId}`}
      aria-pressed={selected}
      data-artwork-candidate={`${candidate.sourceId}:${candidate.assetId}`}
      disabled={disabled}
      onClick={onSelect}
    >
      <ArtworkImage imageKey={candidate.thumbnailKey ?? candidate.previewKey} />
      <span>{[current && 'Current', selected && 'Selected'].filter(Boolean).join(' · ') || 'Preview'}</span>
      <small>{artworkDescription(candidate)}</small>
    </button>
  )
}
