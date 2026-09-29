import { useEffect, useMemo, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { ApiError, hours, request, storeLabel } from '../api/client'
import { useApiQuery } from '../api/hooks'
import { useViewState } from '../viewState'
import { Empty, Notice } from './shared'
import {
  buildMergeCards,
  exactMergeCards,
  extendMergeUndo,
  includeMergeRow,
  mergeAnswer,
  mergeMinutes,
  mergeMemberLabels,
  mergeSections,
  mergeTitle,
  promoteMergeRow,
  sortMergeCards,
  type MergeCard,
  type MergeReview,
  type MergeSection,
  type MergeSort,
  type MergeUndo,
} from './parity-merge-model'
import './parity-merge.css'

const sectionNames: Record<MergeSection, string> = {
  stores: 'Across stores',
  editions: 'Editions',
  expansions: 'Expansions',
  parts: 'Parts',
  tests: 'Test builds',
}
type Choice = Pick<MergeCard, 'parent' | 'included' | 'selected'>
type Mutation = { revision: string; actId?: number }

export function MergeQueue({
  review,
  onReview,
  onOpenGame,
  disabled = false,
}: {
  review: MergeReview
  onReview: (parent: number, children: number[], kind: string, label: string) => void
  onOpenGame?: (workId: number) => void
  disabled?: boolean
}) {
  const client = useQueryClient()
  const preferences = useApiQuery<{ preference: string; value: string | null }[]>(
    'preferences.presentation.get',
  )
  const preferred = Array.isArray(preferences.data)
    ? (preferences.data.find((item) => item.preference === 'PreferredMergePlatform')?.value ?? '')
    : ''
  const [section, setSection] = useViewState<MergeSection | 'all'>('identity:queue-section', 'all')
  const [sort, setSort] = useViewState<MergeSort>('identity:queue-sort', 'strength')
  const [choices, setChoices] = useViewState<Record<string, Choice>>('identity:queue-choices', {})
  const [undo, setUndo] = useViewState<MergeUndo | null>('identity:review-undo', null)
  const [busy, setBusy] = useViewState('identity:queue-busy', false)
  const [problem, setProblem] = useViewState<unknown>('identity:queue-problem', undefined)
  const [mustRefresh, setMustRefresh] = useViewState('identity:queue-refresh-required', false)
  const [notice, setNotice] = useState('')
  const [hovered, setHovered] = useState<{ card: string; work: number } | null>(null)
  const writing = useRef(false),
    mounted = useRef(true)
  const root = useRef<HTMLDivElement>(null)
  const focusedRow = useRef<string | null>(null)
  const snapshot = useRef(review)
  snapshot.current = review
  const projection = useMemo(() => buildMergeCards(review), [review])
  const cards = projection.map((card) => {
    const choice = choices[card.key]
    if (!choice || card.actId) return card
    const parent = card.rows.some((row) => row.workId === choice.parent) ? choice.parent : card.parent
    return {
      ...card,
      parent,
      included: [
        ...new Set([parent, ...choice.included.filter((id) => card.rows.some((row) => row.workId === id))]),
      ],
      selected: choice.selected,
    }
  })
  const selected = cards.filter(
    (card) => card.selected && !card.actId && mergeAnswer(card).childWorkIds.length,
  )
  const exact = exactMergeCards(cards, section)
  const shown = cards.filter((card) => section === 'all' || card.section === section)
  const rowLabels = new Map(cards.map((card) => [card.key, mergeMemberLabels(card)]))
  const blocked = busy || disabled || mustRefresh
  function focusAfterAnswer(answered: MergeCard[]) {
    const rows = Array.from(root.current?.querySelectorAll<HTMLButtonElement>('[data-merge-row]') ?? [])
    const current = rows.findIndex((row) => row.dataset.mergeRow === focusedRow.current)
    if (current < 0 || !answered.some((card) => focusedRow.current?.startsWith(`${card.key}:`)))
      return () => {}
    const remains = (row: HTMLButtonElement) =>
      !answered.some((card) => row.dataset.mergeRow?.startsWith(`${card.key}:`))
    const remaining = rows.filter(remains),
      preceding = rows.slice(0, current).filter(remains).length
    const target = remaining[Math.min(preceding, remaining.length - 1)]?.dataset.mergeRow
    return () => {
      if (target)
        setTimeout(() => {
          if (mounted.current)
            Array.from(root.current?.querySelectorAll<HTMLButtonElement>('[data-merge-row]') ?? [])
              .find((row) => row.dataset.mergeRow === target)
              ?.focus()
        }, 0)
    }
  }
  function choose(card: MergeCard) {
    setChoices((current) => ({
      ...current,
      [card.key]: { parent: card.parent, included: card.included, selected: card.selected },
    }))
  }
  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])
  useEffect(() => {
    if (!undo) return
    const delay = undo.expiresAt - Date.now()
    if (delay <= 0) {
      setUndo(null)
      return
    }
    const timeout = setTimeout(
      () => setUndo((current) => (current?.expiresAt === undo.expiresAt ? null : current)),
      delay,
    )
    return () => clearTimeout(timeout)
  }, [undo, setUndo])
  useEffect(() => {
    if (!preferred) return
    setChoices((current) => {
      const next = { ...current }
      for (const card of projection) {
        if (card.actId || card.kind !== 'same_game') continue
        const prior = current[card.key] ?? card
        const matching =
          card.rows.find((row) => row.workId === prior.parent && row.stores.includes(preferred)) ??
          card.rows.find((row) => row.stores.includes(preferred))
        if (matching)
          next[card.key] = {
            ...prior,
            parent: matching.workId,
            included: [...new Set([...prior.included, matching.workId])],
          }
      }
      return next
    })
    // A new snapshot reconciles saved platform preference when a surface is reopened.
  }, [preferred, projection, setChoices])
  async function refresh() {
    const fresh = await request<MergeReview>('identity.get')
    if (!fresh || typeof fresh.revision !== 'string' || !Array.isArray(fresh.candidates))
      throw new Error('The refreshed review was incomplete. Try again.')
    snapshot.current = fresh
    client.setQueryData(['api', 'identity.get', undefined], fresh)
    return fresh
  }
  async function execute(route: string, body: object, revision: string): Promise<Mutation> {
    const result = await request<Mutation>(route, undefined, { expectedRevision: revision, ...body })
    if (
      !result ||
      typeof result.revision !== 'string' ||
      (route === 'identity.link' && !Number.isSafeInteger(result.actId))
    )
      throw new ApiError(0, 'The saved decision could not be confirmed.')
    return result
  }
  async function action(operation: (revision: string) => Promise<void>) {
    if (writing.current || blocked) return false
    writing.current = true
    setBusy(true)
    setProblem(undefined)
    setNotice('')
    await client.cancelQueries({ queryKey: ['api', 'identity.get'] })
    try {
      await operation(snapshot.current.revision)
      await refresh()
      void client.invalidateQueries({ queryKey: ['api', 'library.get'] })
      void client.invalidateQueries({ queryKey: ['api', 'library.workspace'] })
      return true
    } catch (error) {
      setProblem(error)
      setMustRefresh(true)
      return false
    } finally {
      writing.current = false
      setBusy(false)
    }
  }
  function remember(next: Omit<MergeUndo, 'expiresAt'>) {
    setUndo((previous) => extendMergeUndo(previous, next))
  }
  async function link(batch: MergeCard[]) {
    const captured = batch.filter((card) => !card.actId && mergeAnswer(card).childWorkIds.length > 0)
    if (!captured.length) return
    const restoreFocus = focusAfterAnswer(captured)
    const succeeded = await action(async (revision) => {
      const answers = {
        actIds: [] as number[],
        candidateIds: [] as number[],
        refusedPairs: [] as MergeUndo['refusedPairs'],
      }
      for (const card of captured) {
        const answer = mergeAnswer(card),
          result = await execute('identity.link', answer, revision)
        revision = result.revision
        answers.actIds.push(result.actId!)
        answers.candidateIds.push(...answer.rejectedCandidateIds)
        answers.refusedPairs.push(...answer.refusedPairs)
        remember({ kind: 'merge', ...answers, count: answers.actIds.length, revision })
        choose({ ...card, selected: false })
      }
      if (mounted.current)
        setNotice(`${answers.actIds.length === 1 ? 'Group' : 'Groups'} rolled up. Nothing was deleted.`)
    })
    if (succeeded) restoreFocus()
  }
  async function dismiss(card: MergeCard) {
    const restoreFocus = focusAfterAnswer([card])
    const succeeded = await action(async (revision) => {
      const candidateIds = card.edges.map((edge) => edge.candidateId),
        refusedPairs = card.pairs
      const result = await execute('identity.dismiss', { candidateIds, refusedPairs }, revision)
      remember({
        kind: 'dismiss',
        actIds: [],
        candidateIds,
        refusedPairs,
        count: 1,
        revision: result.revision,
      })
      choose({ ...card, selected: false })
      if (mounted.current) setNotice('These games will stay separate. Nothing was deleted.')
    })
    if (succeeded) restoreFocus()
  }
  async function reverse(actId?: number) {
    const run = undo
    if (!actId && (!run || run.expiresAt <= Date.now())) {
      setUndo(null)
      return
    }
    await action(async (revision) => {
      const result = await execute(
        'identity.undo',
        {
          actIds: actId ? [actId] : run!.actIds,
          candidateIds: actId ? [] : run!.candidateIds,
          refusedPairs: actId ? [] : run!.refusedPairs,
        },
        actId ? revision : run!.revision,
      )
      if (!actId) setUndo(null)
      else setUndo((current) => current && { ...current, revision: result.revision })
      if (mounted.current) setNotice('Decision undone. Your games and play history are kept.')
    })
  }
  async function savePreferred(value: string) {
    if (writing.current || busy || disabled) return
    writing.current = true
    setBusy(true)
    setProblem(undefined)
    try {
      await request('preferences.presentation.put', { preference: 'PreferredMergePlatform' }, { value })
      client.setQueryData(
        ['api', 'preferences.presentation.get', undefined],
        (old: { preference: string; value: string | null }[] | undefined) => [
          ...(Array.isArray(old) ? old.filter((item) => item.preference !== 'PreferredMergePlatform') : []),
          { preference: 'PreferredMergePlatform', value },
        ],
      )
    } catch (error) {
      setProblem(error)
    } finally {
      writing.current = false
      setBusy(false)
    }
  }
  return (
    <div
      className="merge-queue"
      ref={root}
      onKeyDown={(event) => {
        if (
          !['ArrowUp', 'ArrowDown'].includes(event.key) ||
          !(event.target instanceof HTMLElement) ||
          !event.target.hasAttribute('data-merge-row')
        )
          return
        const rows = Array.from(root.current?.querySelectorAll<HTMLButtonElement>('[data-merge-row]') ?? [])
        const current = rows.indexOf(event.target as HTMLButtonElement)
        if (current < 0) return
        event.preventDefault()
        rows[Math.max(0, Math.min(rows.length - 1, current + (event.key === 'ArrowDown' ? 1 : -1)))]?.focus()
      }}
    >
      <div className="merge-controls">
        <label>
          Sort proposals
          <select
            aria-label="Sort proposals"
            value={sort}
            onChange={(event) => setSort(event.target.value as MergeSort)}
          >
            <option value="strength">Strongest match</option>
            <option value="playtime">Playtime at stake</option>
            <option value="title">Title</option>
          </select>
        </label>
        <label>
          Preferred main platform
          <select
            aria-label="Preferred main platform"
            value={preferred}
            disabled={busy}
            onChange={(event) => void savePreferred(event.target.value)}
          >
            <option value="">None</option>
            <option value="steam">Steam</option>
            <option value="epic">Epic Games</option>
            <option value="gog">GOG</option>
          </select>
        </label>
        <button disabled={blocked || !exact.length} onClick={() => void link(exact)}>
          {exact.length
            ? `Accept ${exact.length} exact ${exact.length === 1 ? 'match' : 'matches'}`
            : 'No exact matches left'}
        </button>
        <button
          className="primary"
          disabled={blocked || !selected.length}
          onClick={() => void link(selected)}
        >
          {selected.length ? `Merge ${selected.length} selected` : 'Merge selected'}
        </button>
      </div>
      <nav className="tabs" aria-label="Proposal kinds">
        {(['all', ...mergeSections] as const).map((kind) => (
          <button key={kind} aria-pressed={section === kind} onClick={() => setSection(kind)}>
            {kind === 'all' ? 'All proposals' : sectionNames[kind]}
          </button>
        ))}
      </nav>
      <p className="muted" role="status">
        {cards.filter((card) => !card.actId).length} proposals · {shown.filter((card) => !card.actId).length}{' '}
        shown
      </p>
      <Notice error={problem} message={busy ? 'Saving your review…' : notice} />
      {mustRefresh && (
        <button
          disabled={busy}
          onClick={async () => {
            setBusy(true)
            try {
              const fresh = await refresh()
              setUndo((current) => current && { ...current, revision: fresh.revision })
              setMustRefresh(false)
              setProblem(undefined)
            } catch (error) {
              setProblem(error)
            } finally {
              if (mounted.current) setBusy(false)
            }
          }}
        >
          Check saved review
        </button>
      )}
      {mergeSections.map((kind) => {
        const members = sortMergeCards(
          shown.filter((card) => card.section === kind),
          sort,
        )
        if (!members.length) return null
        return (
          <section className="merge-section" key={kind} aria-label={sectionNames[kind]}>
            <h3>{sectionNames[kind]}</h3>
            {members.map((card) => (
              <article
                className={`merge-card${card.actId ? ' resolved' : ''}`}
                key={card.key}
                aria-label={`${mergeTitle(card)} ${card.actId ? 'saved group' : 'proposal'}`}
              >
                <header>
                  <h4>{mergeTitle(card)}</h4>
                  {!card.actId && (
                    <>
                      <span className="merge-confidence">{card.confidence}</span>
                      <label className="checkbox">
                        <input
                          type="checkbox"
                          aria-label={`Select ${mergeTitle(card)} group`}
                          checked={card.selected}
                          disabled={blocked}
                          onChange={(event) => choose({ ...card, selected: event.target.checked })}
                        />
                        Select group
                      </label>
                    </>
                  )}
                </header>
                {card.actId ? (
                  <div className="form-actions">
                    <p>
                      {card.rows.length} entries · {hours(mergeMinutes(card))} · Nothing deleted
                    </p>
                    <button disabled={blocked} onClick={() => void reverse(card.actId)}>
                      Separate again
                    </button>
                  </div>
                ) : (
                  <>
                    <div className="merge-rows">
                      {card.rows.map((row, index) => (
                        <div
                          className={`merge-row${row.workId === card.parent ? ' main-row' : ''}`}
                          key={row.workId}
                          onMouseEnter={() => setHovered({ card: card.key, work: row.workId })}
                          onMouseLeave={() => setHovered(null)}
                        >
                          {card.kind === 'same_game' && (
                            <input
                              type="radio"
                              name={`main-${card.key}`}
                              aria-label={`Make ${rowLabels.get(card.key)![index]} the main game`}
                              checked={row.workId === card.parent}
                              disabled={blocked}
                              onChange={() => choose(promoteMergeRow(card, row.workId))}
                            />
                          )}
                          <button
                            className="merge-row-title"
                            data-merge-row={`${card.key}:${row.workId}`}
                            onFocus={() => {
                              focusedRow.current = `${card.key}:${row.workId}`
                            }}
                            type="button"
                            onClick={() =>
                              onOpenGame
                                ? onOpenGame(row.workId)
                                : card.kind === 'same_game' && choose(promoteMergeRow(card, row.workId))
                            }
                            aria-label={`${onOpenGame ? 'View' : 'Choose'} ${rowLabels.get(card.key)![index]}`}
                          >
                            <strong>{row.title}</strong>
                            <small>
                              {[row.year, row.publisher, row.stores.map(storeLabel).join(' / ')]
                                .filter(Boolean)
                                .join(' · ')}
                            </small>
                          </button>
                          <span className="merge-row-hours">
                            {row.pack && !row.minutes ? '—' : hours(row.minutes)}
                            {row.unread && <span aria-label="Patched since playing"> · ●</span>}
                          </span>
                          <input
                            type="checkbox"
                            aria-label={`Include ${rowLabels.get(card.key)![index]}`}
                            checked={card.included.includes(row.workId)}
                            disabled={blocked || row.workId === card.parent}
                            onChange={(event) =>
                              choose(includeMergeRow(card, row.workId, event.target.checked))
                            }
                          />
                        </div>
                      ))}
                    </div>
                    <p className="merge-reason">
                      {hovered?.card === card.key
                        ? (() => {
                            const row = card.rows.find((member) => member.workId === hovered.work)!
                            return [
                              row.stores.map(storeLabel).join(' / '),
                              row.pack && !row.minutes
                                ? 'No separate playtime recorded'
                                : row.minutes
                                  ? hours(row.minutes)
                                  : 'Never opened',
                              row.lastPlayedAt
                                ? `Last played ${new Date(row.lastPlayedAt).toLocaleDateString()}`
                                : row.acquiredAt
                                  ? `Added ${new Date(row.acquiredAt).toLocaleDateString()}`
                                  : '',
                              row.installed ? 'Installed' : 'Not installed',
                              row.unread ? 'Patched since playing' : '',
                            ]
                              .filter(Boolean)
                              .join(' · ')
                          })()
                        : card.reason}
                    </p>
                    <p className="muted">
                      {hours(mergeMinutes(card))} · {card.rows.length} entries
                      {card.rows.length !== card.included.length
                        ? ` · ${card.rows.length - card.included.length} left out`
                        : ''}
                      {card.rows.some((row) => row.acquiredAt)
                        ? ` · Owned since ${card.rows
                            .flatMap((row) => (row.acquiredAt ? [row.acquiredAt] : []))
                            .sort()[0]!
                            .slice(0, 4)}`
                        : ''}
                    </p>
                    <div className="form-actions">
                      <button
                        className="primary"
                        disabled={blocked || !mergeAnswer(card).childWorkIds.length}
                        onClick={() => void link([card])}
                      >
                        Same game
                      </button>
                      <button disabled={blocked} onClick={() => void dismiss(card)}>
                        Different games
                      </button>
                      <button
                        disabled={blocked}
                        onClick={() =>
                          onReview(card.parent, mergeAnswer(card).childWorkIds, card.kind, card.label ?? '')
                        }
                      >
                        {card.kind === 'same_game' ? 'Same game…' : 'Review relationship…'}
                      </button>
                    </div>
                  </>
                )}
              </article>
            ))}
          </section>
        )
      })}
      {!shown.length && <Empty>No proposals in this view.</Empty>}
      {undo && undo.expiresAt > Date.now() && (
        <aside className="merge-undo" aria-label="Review undo" role="status">
          <span>
            {undo.count} {undo.kind === 'dismiss' ? 'left separate' : 'rolled up'} · Nothing deleted
          </span>
          <button disabled={blocked} onClick={() => void reverse()}>
            Undo review decisions
          </button>
          <button aria-label="Dismiss review undo" disabled={busy} onClick={() => setUndo(null)}>
            Dismiss
          </button>
        </aside>
      )}
    </div>
  )
}
