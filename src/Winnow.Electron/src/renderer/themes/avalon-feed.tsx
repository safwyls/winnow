import {
  createContext,
  useContext,
  type ReactNode,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  useSyncExternalStore,
} from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import type { ThemeContext } from '../../shared/theme'
import type { FeedSnapshot, FeedVerdict, LibraryGame } from '../api/types'
import { request } from '../api/client'
import { feedRefresh, invalidateFeed } from '../api/feed-refresh'
import { feedSchema, feedSupplementSchema, useWorkspace } from '../api/hooks'
import { primaryAction } from '../../shared/game-actions'
import { avalonShelves } from './avalon-data'
import { FeedDeck, receiptDuration, type FeedDeckShelf, type FeedRow } from './avalon-feed-model'
import './avalon-feed.css'

export function useAvalonFeed(context: ThemeContext) {
  const client = useQueryClient(),
    current = useRef(context)
  current.current = context
  const held = useRef(new Map<FeedRow, Set<string>>())
  const optionalReads = useRef(new Set<AbortController>())
  const coordinator = feedRefresh(client)
  useSyncExternalStore(coordinator.subscribe, coordinator.snapshot)
  const [deck] = useState(
    () =>
      new FeedDeck(
        async (releaseId, kind, undo) => {
          const result = await request<{ saved: boolean; expiresAt?: string | null } | boolean>(
            undo ? 'feedRevoke' : 'feedFeedback',
            undefined,
            { releaseId, kind },
          )
          return typeof result === 'boolean' ? { saved: result } : result
        },
        async () => {
          const optionalController = new AbortController()
          optionalReads.current.add(optionalController)
          try {
            const [primary, optional] = await Promise.allSettled([
              client.fetchQuery({
                queryKey: ['api', 'feed.get'],
                queryFn: async ({ signal }) =>
                  feedSchema.parse(await request('feed.get', undefined, undefined, signal)) as FeedSnapshot,
                staleTime: 0,
              }),
              request('feed.supplement', undefined, undefined, optionalController.signal).then((value) =>
                feedSupplementSchema.parse(value),
              ),
            ])
            if (primary.status !== 'fulfilled') throw Error('Recommendations could not be refreshed.')
            const extra = optional.status === 'fulfilled' ? optional.value.shelves : []
            const feed = {
              ...primary.value,
              shelves: [
                ...primary.value.shelves,
                ...extra.filter(
                  (shelf) => !primary.value.shelves.some((existing) => existing.id === shelf.id),
                ),
              ],
            }
            return avalonShelves(current.current.games, feed, current.current.mode === 'fullscreen')
          } finally {
            optionalReads.current.delete(optionalController)
          }
        },
        () => {
          for (const controller of optionalReads.current) controller.abort()
          void client.invalidateQueries({ queryKey: ['api', 'feed.history'] })
          invalidateFeed(client)
        },
      ),
  )
  useSyncExternalStore(deck.subscribe, deck.snapshot)
  useLayoutEffect(() => {
    deck.retireBackfill()
    for (const controller of optionalReads.current) controller.abort()
  }, [deck, coordinator.epoch])
  const source = useMemo(
    () =>
      context.feedFailed && !context.feed
        ? []
        : avalonShelves(context.games, context.feed, context.mode === 'fullscreen'),
    [context.games, context.feed, context.mode, context.feedFailed],
  )
  const libraryKey = JSON.stringify([
    context.mode,
    context.games.map((game) => [game.workId, game.entries.map((entry) => entry.releaseId)]),
  ])
  useLayoutEffect(() => {
    deck.activate()
    return () => {
      deck.dispose()
      for (const controller of optionalReads.current) controller.abort()
      optionalReads.current.clear()
      if (
        !client
          .getQueryCache()
          .find({ queryKey: ['api', 'feed.get'], exact: true })
          ?.getObserversCount()
      )
        void client.cancelQueries({ queryKey: ['api', 'feed.get'], exact: true })
    }
  }, [deck])
  useLayoutEffect(() => {
    deck.receive(source, libraryKey, false, context.feedFailed || context.feed?.failed)
  }, [deck, source, libraryKey, context.feedFailed])
  useEffect(() => {
    const media = window.matchMedia?.('(prefers-reduced-motion: reduce)')
    let timer: ReturnType<typeof setInterval>
    const start = () => {
      clearInterval(timer)
      const interval = context.profile.appearance.reducedMotion || media?.matches ? 1000 : 33
      let last = performance.now()
      timer = setInterval(() => {
        const now = performance.now(),
          elapsed = now - last
        last = now
        if (
          document.hidden ||
          !document.hasFocus() ||
          document.querySelector('[role="dialog"], [data-feed-history-body]')
        )
          return
        deck.tick(elapsed, (row) => !!held.current.get(row)?.size)
      }, interval)
    }
    start()
    media?.addEventListener('change', start)
    return () => {
      clearInterval(timer)
      media?.removeEventListener('change', start)
    }
  }, [deck, context.profile.appearance.reducedMotion])
  const hold = (row: FeedRow, scope: string, value: boolean) => {
    const reasons = held.current.get(row) ?? new Set<string>()
    if (value) reasons.add(scope)
    else reasons.delete(scope)
    if (reasons.size) held.current.set(row, reasons)
    else held.current.delete(row)
  }
  const handlers = (row: FeedRow, scope: string) => ({
    onMouseEnter: () => hold(row, `${scope}:pointer`, true),
    onMouseLeave: () => hold(row, `${scope}:pointer`, false),
    onFocusCapture: () => hold(row, `${scope}:focus`, true),
    onBlurCapture: (event: React.FocusEvent<HTMLElement>) => {
      if (!(event.relatedTarget instanceof Node) || !event.currentTarget.contains(event.relatedTarget))
        hold(row, `${scope}:focus`, false)
    },
  })
  return {
    deck,
    shelves: deck.shelves,
    handlers,
    refresh: () => {
      void deck.backfill()
    },
  }
}

export function feedDate(value?: string | null) {
  return value && Number.isFinite(Date.parse(value))
    ? new Date(value).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })
    : ''
}

export function splitReason(reason: string) {
  const runs: { text: string; data: boolean }[] = []
  const append = (text: string, data: boolean) => {
    if (!text) return
    const last = runs.at(-1)
    if (last?.data === data) last.text += text
    else runs.push({ text, data })
  }
  for (const token of reason.match(/\s+|\S+/g) ?? []) {
    if (!/[0-9]/.test(token)) {
      append(token, false)
      continue
    }
    const edges = '"\'“”‘’(),.;:!?—–-'
    let start = 0,
      end = token.length
    while (start < end && edges.includes(token[start])) start++
    while (end > start && edges.includes(token[end - 1])) end--
    append(token.slice(0, start), false)
    append(token.slice(start, end), true)
    append(token.slice(end), false)
  }
  return runs
}
export function FeedReason({ reason }: { reason: string }) {
  return (
    <>
      {splitReason(reason).map((run, index) => (
        <span key={index} className={run.data ? 'avalon-feed-date' : undefined}>
          {run.text}
        </span>
      ))}
    </>
  )
}

export function FeedLaunch({ context, row }: { context: ThemeContext; row: FeedRow }) {
  const workspace = useWorkspace()
  const [pending, setPending] = useState(false),
    [error, setError] = useState('')
  const busy = useRef(false)
  const entry = [...row.game.entries]
    .sort((a, b) => Number(b.installed) - Number(a.installed))
    .find((entry) => primaryAction(entry, workspace.data))
  if (!entry) return null
  const action = primaryAction(entry, workspace.data)
  async function launch() {
    if (busy.current || !entry) return
    busy.current = true
    setPending(true)
    setError('')
    try {
      await context.actions.launch(entry.ownershipId)
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'The launcher could not accept this action.')
    } finally {
      busy.current = false
      setPending(false)
    }
  }
  return (
    <span className="avalon-feed-launch">
      <button
        data-controller-play={context.mode === 'fullscreen' || undefined}
        aria-label={`${action} ${row.game.title}`}
        disabled={pending}
        onClick={() => void launch()}
      >
        {pending ? 'Opening…' : action}
      </button>
      {error && <span role="alert">{error}</span>}
    </span>
  )
}

export function FeedFeedback({ deck, shelf, row }: { deck: FeedDeck; shelf: FeedDeckShelf; row: FeedRow }) {
  const element = useRef<HTMLDivElement>(null),
    restoreFocus = useRef(false)
  useLayoutEffect(() => {
    if (restoreFocus.current) {
      element.current?.querySelector<HTMLButtonElement>('button')?.focus({ preventScroll: true })
      restoreFocus.current = false
    }
  }, [row.receipt])
  if (!shelf.feedback || row.releaseId === undefined) return null
  const give = (kind: number, undo = false) => {
    restoreFocus.current = !!element.current?.contains(document.activeElement)
    void deck.respond(row, kind, undo)
  }
  const receipt = row.receipt,
    countdown = receipt && deck.canReplace(shelf)
  return (
    <div className="avalon-feedback" ref={element}>
      {receipt ? (
        <>
          <span role="status">
            {receipt.kind === 1 ? (
              <>
                Back on{' '}
                <time className="avalon-feed-date" dateTime={receipt.expiresAt ?? undefined}>
                  {feedDate(receipt.expiresAt)}
                </time>
              </>
            ) : (
              'Off the feed.'
            )}
            {countdown && <span className="sr-only"> Undo before this card is replaced.</span>}
          </span>
          <button disabled={row.pending} onClick={() => give(receipt.kind, true)}>
            Undo
          </button>
          {countdown && (
            <progress
              aria-label="Time before this card is replaced"
              max={receiptDuration}
              value={receipt.elapsed}
            />
          )}
        </>
      ) : (
        <>
          <button disabled={row.pending} onClick={() => give(1)}>
            Not now
          </button>
          <button disabled={row.pending} onClick={() => give(0)}>
            Not interested
          </button>
        </>
      )}
      {row.error && <p role="alert">{row.error}</p>}
    </div>
  )
}

export function verdictStatus(row: FeedVerdict) {
  if (row.status === 1) return { note: 'Undone on', date: row.revokedAt }
  if (row.status === 2) return { note: 'Lapsed on', date: row.expiresAt }
  return row.kind === 1
    ? { note: 'Back on', date: row.expiresAt }
    : { note: 'Off the feed since', date: row.createdAt }
}

const FeedHistoryContext = createContext<{ open(): void; count: number }>({ open() {}, count: 0 })
export function FeedHistoryButton() {
  const history = useContext(FeedHistoryContext)
  return (
    <button onClick={history.open}>
      What you've told the feed
      {history.count > 0 && <span className="avalon-feed-date"> {history.count.toLocaleString()}</span>}
    </button>
  )
}

export function FeedHistory({
  games,
  deck,
  children,
}: {
  games: LibraryGame[]
  deck: FeedDeck
  children: ReactNode
}) {
  const [open, setOpen] = useState(false),
    [pending, setPending] = useState(''),
    [error, setError] = useState('')
  const history = useQuery({
    queryKey: ['api', 'feed.history', undefined],
    queryFn: ({ signal }) => request<FeedVerdict[]>('feedHistory', undefined, undefined, signal),
    retry: false,
    staleTime: 30000,
  })
  const client = useQueryClient()
  const origin = useRef<HTMLElement | null>(null)
  const back = useRef<HTMLButtonElement>(null)
  useLayoutEffect(() => {
    if (open) back.current?.focus({ preventScroll: true })
    else if (origin.current?.isConnected) {
      origin.current.focus({ preventScroll: true })
      origin.current = null
    }
  }, [open])
  const titles = new Map(
    games.flatMap((game) => game.entries.map((entry) => [entry.releaseId, game.title] as const)),
  )
  async function undo(row: FeedVerdict, key: string) {
    if (pending) return
    setPending(key)
    setError('')
    try {
      await request('feedRevoke', undefined, { releaseId: row.releaseId, kind: row.kind })
      deck.restore(row.releaseId, row.kind)
      invalidateFeed(client)
      await client.invalidateQueries({ queryKey: ['api', 'feed.history'] })
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Couldn't undo that just now.")
    } finally {
      setPending('')
    }
  }
  return (
    <FeedHistoryContext.Provider
      value={{
        count: history.data?.length ?? 0,
        open: () => {
          origin.current = document.activeElement instanceof HTMLElement ? document.activeElement : null
          setOpen(true)
        },
      }}
    >
      <div className="avalon-feed-body" hidden={open}>
        {children}
      </div>
      {open && (
        <section
          className="avalon-feed-history"
          data-controller-scope
          role="region"
          aria-label="What you've told the feed"
          data-feed-history-body
          onKeyDown={(event) => {
            if (event.key === 'Escape') {
              event.preventDefault()
              event.stopPropagation()
              setOpen(false)
            }
          }}
        >
          <h1>What you've told the feed</h1>
          <p>Your responses stay here, including choices you have taken back.</p>
          {(error || history.error) && (
            <p role="alert">
              {error || 'Your feed responses could not be loaded.'}{' '}
              <button
                onClick={() => {
                  setError('')
                  void history.refetch()
                }}
              >
                Try again
              </button>
            </p>
          )}
          {history.isPending ? (
            <p role="status">Loading your responses…</p>
          ) : history.data?.length ? (
            <ol>
              {[...history.data]
                .sort((a, b) => b.createdAt.localeCompare(a.createdAt))
                .map((row, index) => {
                  const key = `${row.releaseId}:${row.kind}:${row.createdAt}:${index}`,
                    status = verdictStatus(row)
                  return (
                    <li key={key}>
                      <div>
                        <h3>{titles.get(row.releaseId) ?? 'A game that is no longer in your library'}</h3>
                        <span className="avalon-label">{row.kind === 1 ? 'NOT NOW' : 'NOT INTERESTED'}</span>
                        <p>
                          {status.note}{' '}
                          <time className="avalon-feed-date" dateTime={status.date ?? undefined}>
                            {feedDate(status.date)}
                          </time>
                        </p>
                      </div>
                      {row.status === 0 && (
                        <button disabled={!!pending} onClick={() => void undo(row, key)}>
                          {pending === key ? 'Undoing…' : 'Undo'}
                        </button>
                      )}
                    </li>
                  )
                })}
            </ol>
          ) : (
            !history.error && <p>Nothing yet. Your feed responses will appear here.</p>
          )}
          <button ref={back} onClick={() => setOpen(false)}>
            Back to the feed
          </button>
          <p className="avalon-feed-history-hints">A · Select · B · Back to the feed</p>
        </section>
      )}
    </FeedHistoryContext.Provider>
  )
}
