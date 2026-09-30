import { useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ApiError, request, storeLabel } from '../api/client'
import { useApiQuery } from '../api/hooks'
import { useViewState } from '../viewState'
import { Empty, Notice } from './shared'
import type { Mode } from '../api/types'
import { Artwork } from '../components/Artwork'
import { dormancy } from '../themes/avalon-data'
import { mergeIdle, mergePlaytime, mergeRollup, mergeRowDetail, mergeRowMark } from './parity-merge-facts'
import { MergeBatchConfirmation, MergeMemberSheet, MergeOptionsSheet } from './parity-merge-overlay'
import { refreshIdentityReview } from './parity-merge-query'
import { mergeActionCopy, mergeActionNames } from './parity-merge-copy'
import {
  buildMergeCards,
  exactMergeCards,
  extendMergeUndo,
  includeMergeRow,
  mergeAnswer,
  mergeDock,
  mergeMinutes,
  mergeMemberLabels,
  mergeSections,
  mergeTitle,
  promoteMergeRow,
  stableMergeCards,
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
const sortOptions = [
  { value: 'strength', label: 'Strongest match' },
  { value: 'playtime', label: 'Playtime at stake' },
  { value: 'title', label: 'Title' },
]
const platformOptions = [
  { value: '', label: 'None' },
  { value: 'steam', label: 'Steam' },
  { value: 'epic', label: 'Epic Games' },
  { value: 'gog', label: 'GOG' },
]

export function MergeQueueLoading() {
  return (
    <div className="merge-queue" role="group" aria-label="Possible matches" aria-busy="true">
      <p role="status">Loading possible matches…</p>
      {mergeSections.map((kind) => (
        <section className="merge-section" aria-label={sectionNames[kind]} key={kind}>
          <h3>{sectionNames[kind]}</h3>
        </section>
      ))}
    </div>
  )
}

export function MergeQueue({
  review,
  onReview,
  onOpenGame,
  disabled = false,
  mode = 'desktop',
}: {
  review: MergeReview
  onReview: (parent: number, children: number[], kind: string, label: string) => void
  onOpenGame?: (workId: number) => void
  disabled?: boolean
  mode?: Mode
}) {
  const client = useQueryClient()
  const preferences = useApiQuery<{ preference: string; value: string | null }[]>(
    'preferences.presentation.get',
  )
  const storedPlatform = Array.isArray(preferences.data)
    ? (preferences.data.find((item) => item.preference === 'PreferredMergePlatform')?.value ?? '')
    : ''
  const preferred = platformOptions.some((option) => option.value === storedPlatform) ? storedPlatform : ''
  const dimCovers =
    !Array.isArray(preferences.data) ||
    preferences.data
      .find((item) => item.preference === 'DimDormantCovers')
      ?.value?.trim()
      .toLowerCase() !== 'false'
  const [section, setSection] = useViewState<MergeSection | 'all'>('identity:queue-section', 'all')
  const [sort, setSort] = useViewState<MergeSort>('identity:queue-sort', 'strength')
  const [choices, setChoices] = useViewState<Record<string, Choice>>('identity:queue-choices', {})
  const [answeredKeys, setAnsweredKeys] = useViewState<Record<number, string>>(
    'identity:queue-answered-keys',
    {},
  )
  const [positions, setPositions] = useViewState<{ sort: MergeSort; keys: string[] }>(
    'identity:queue-positions',
    { sort, keys: [] },
  )
  const [undo, setUndo] = useViewState<MergeUndo | null>('identity:review-undo', null)
  const [busy, setBusy] = useViewState('identity:queue-busy', false)
  const [problem, setProblem] = useViewState<unknown>('identity:queue-problem', undefined)
  const [mustRefresh, setMustRefresh] = useViewState('identity:queue-refresh-required', false)
  const [notice, setNotice] = useState('')
  const [detailReturn, setDetailReturn] = useViewState<string | null>(`identity:${mode}:detail-return`, null)
  const restoreDetail = useRef(detailReturn)
  const [hovered, setHovered] = useState<{ card: string; work: number } | null>(null)
  const [openCard, setOpenCard] = useState<string | null>(null)
  const [confirmBatch, setConfirmBatch] = useState<{ cards: MergeCard[]; kind: 'exact' | 'selected' } | null>(
    null,
  )
  const [refusalUntil, setRefusalUntil] = useState<number | null>(null)
  const [optionSheet, setOptionSheet] = useState<'sort' | 'kind' | 'platform' | null>(null)
  const writing = useRef(false),
    mounted = useRef(true)
  const root = useRef<HTMLDivElement>(null)
  const focusedRow = useRef<string | null>(null)
  const snapshot = useRef(review)
  const mutation = useMutation({ mutationFn: (operation: () => Promise<void>) => operation() })
  snapshot.current = review
  const projection = useMemo(() => buildMergeCards(review), [review])
  const [appliedPreferred, setAppliedPreferred] = useViewState<string | null>(
    'identity:queue-applied-platform',
    null,
  )
  const cards = projection.map((source) => {
    const card =
      source.actId && answeredKeys[source.actId] ? { ...source, key: answeredKeys[source.actId]! } : source
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
  const ordered = stableMergeCards(cards, positions.sort === sort ? positions.keys : [], sort)
  const selected = ordered.filter(
    (card) => card.selected && !card.actId && mergeAnswer(card).childWorkIds.length,
  )
  const exact = exactMergeCards(ordered, section)
  const shown = cards.filter((card) => section === 'all' || card.section === section)
  const orderKeys = ordered.map((card) => card.key)
  useEffect(() => {
    const keys = positions.sort === sort ? [...new Set([...positions.keys, ...orderKeys])] : orderKeys
    if (positions.sort !== sort || positions.keys.join('|') !== keys.join('|')) setPositions({ sort, keys })
  }, [sort, positions, orderKeys.join('|'), setPositions])
  const rowLabels = new Map(cards.map((card) => [card.key, mergeMemberLabels(card)]))
  const blocked = busy || disabled || mustRefresh
  useEffect(() => {
    const rows = Array.from(
      root.current?.querySelectorAll<HTMLElement>(
        mode === 'desktop' ? '[data-merge-row]' : '[data-merge-proposal][data-merge-pending="true"]',
      ) ?? [],
    )
    const keys = rows.map((row) => row.dataset.mergeRow ?? row.dataset.mergeProposal)
    if (!keys.includes(focusedRow.current ?? undefined)) focusedRow.current = keys[0] ?? null
  }, [review, section, sort, mode])
  function focusAfterAnswer(answered: MergeCard[]) {
    const selector =
      mode === 'fullscreen' ? '[data-merge-proposal][data-merge-pending="true"]' : '[data-merge-row]'
    const key = (row: HTMLButtonElement) => row.dataset.mergeRow ?? row.dataset.mergeProposal
    const belongs = (value: string | null | undefined, card: MergeCard) =>
      value === card.key || value?.startsWith(`${card.key}:`)
    const rows = Array.from(root.current?.querySelectorAll<HTMLButtonElement>(selector) ?? [])
    const current = rows.findIndex((row) => key(row) === focusedRow.current)
    if (current < 0 || !answered.some((card) => belongs(focusedRow.current, card))) return () => {}
    const remains = (row: HTMLButtonElement) => !answered.some((card) => belongs(key(row), card))
    const remaining = rows.filter(remains),
      preceding = rows.slice(0, current).filter(remains).length
    const target = remaining.length ? key(remaining[Math.min(preceding, remaining.length - 1)]!) : undefined
    return () => {
      if (target)
        setTimeout(() => {
          if (mounted.current) {
            const row = Array.from(root.current?.querySelectorAll<HTMLButtonElement>(selector) ?? []).find(
              (row) => key(row) === target,
            )
            row?.focus()
            row?.scrollIntoView?.({ block: 'nearest' })
          }
        }, 0)
    }
  }
  function choose(card: MergeCard) {
    setChoices((current) => ({
      ...current,
      [card.key]: { parent: card.parent, included: card.included, selected: card.selected },
    }))
  }
  function openGame(card: MergeCard, workId: number) {
    if (blocked) return
    focusedRow.current = mode === 'fullscreen' ? card.key : `${card.key}:${workId}`
    setDetailReturn(focusedRow.current)
    if (mode === 'desktop') {
      const row = [...(root.current?.querySelectorAll<HTMLButtonElement>('[data-merge-row]') ?? [])].find(
        (element) => element.dataset.mergeRow === focusedRow.current,
      )
      row?.focus({ preventScroll: true })
    }
    onOpenGame?.(workId)
  }
  useEffect(() => {
    if (!restoreDetail.current || blocked) return
    let second = 0
    const first = requestAnimationFrame(() => {
      second = requestAnimationFrame(() => {
        const target = [
          ...(root.current?.querySelectorAll<HTMLButtonElement>('[data-merge-row], [data-merge-proposal]') ??
            []),
        ].find(
          (element) => (element.dataset.mergeRow ?? element.dataset.mergeProposal) === restoreDetail.current,
        )
        if (!target) return
        target.focus({ preventScroll: true })
        target.scrollIntoView?.({ block: 'nearest' })
        restoreDetail.current = null
        setDetailReturn(null)
      })
    })
    return () => {
      cancelAnimationFrame(first)
      cancelAnimationFrame(second)
    }
  }, [detailReturn, projection, setDetailReturn, blocked])
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
    if (refusalUntil === null) return
    const timeout = setTimeout(() => setRefusalUntil(null), Math.max(0, refusalUntil - Date.now()))
    return () => clearTimeout(timeout)
  }, [refusalUntil])
  useEffect(() => {
    if (!Array.isArray(preferences.data)) return
    // Initial hydration supplies defaults only. A header already chosen while
    // that read was pending belongs to the user, even if it differs from the default.
    const changed = appliedPreferred !== null && preferred !== appliedPreferred
    if (preferred !== appliedPreferred) setAppliedPreferred(preferred)
    if (!preferred) return
    setChoices((current) => {
      const next = { ...current }
      for (const card of projection) {
        if (card.actId || card.kind !== 'same_game') continue
        if (!changed && current[card.key]) continue
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
  }, [preferred, preferences.data, appliedPreferred, projection, setChoices, setAppliedPreferred])
  async function refresh() {
    const fresh = await refreshIdentityReview(client)
    snapshot.current = fresh
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
  async function action(operation: (revision: string) => Promise<void>, refusedCard?: MergeCard) {
    if (writing.current || blocked) return false
    writing.current = true
    setBusy(true)
    setProblem(undefined)
    setNotice('')
    setRefusalUntil(null)
    let wrote = false
    await client.cancelQueries({ queryKey: ['api', 'identity.get'] })
    try {
      await mutation.mutateAsync(async () => {
        await operation(snapshot.current.revision)
        wrote = true
        await refresh()
      })
      void client.invalidateQueries({ queryKey: ['api', 'library.get'] })
      void client.invalidateQueries({ queryKey: ['api', 'library.workspace'] })
      return true
    } catch (error) {
      // A stale structural proposal can disappear after another client's edit. Show that
      // authoritative state without retrying the rejected write or inventing an Undo act.
      if (!wrote && refusedCard && error instanceof ApiError && error.conflict) {
        try {
          const fresh = await refresh()
          if (!buildMergeCards(fresh).some((card) => !card.actId && card.key === refusedCard.key)) {
            setUndo(null)
            setMustRefresh(false)
            setRefusalUntil(Date.now() + 7000)
            return false
          }
        } catch {
          /* The existing explicit refresh recovery remains available. */
        }
      }
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
  async function link(batch: MergeCard[], kind: 'single' | 'selected' | 'exact' = 'single') {
    const captured = batch.filter((card) => !card.actId && mergeAnswer(card).childWorkIds.length > 0)
    if (!captured.length) return
    const restoreFocus = focusAfterAnswer(captured)
    const succeeded = await action(
      async (revision) => {
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
          setAnsweredKeys((current) => ({ ...current, [result.actId!]: card.key }))
          answers.candidateIds.push(...answer.rejectedCandidateIds)
          answers.refusedPairs.push(...answer.refusedPairs)
          remember({
            kind: 'merge',
            ...answers,
            count: answers.actIds.length,
            revision,
            action: kind,
            headerTitle: mergeTitle(card),
            nested: answer.childWorkIds.length,
            leftOut: card.rows.length - card.included.length,
          })
          choose({ ...card, selected: false })
        }
        if (mounted.current)
          setNotice(`${answers.actIds.length === 1 ? 'Group' : 'Groups'} rolled up. Nothing was deleted.`)
      },
      captured.length === 1 ? captured[0] : undefined,
    )
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
    if (writing.current || blocked) return
    writing.current = true
    setBusy(true)
    setProblem(undefined)
    try {
      await client.cancelQueries({
        queryKey: ['api', 'preferences.presentation.get', undefined],
        exact: true,
      })
      await request('preferences.presentation.put', { preference: 'PreferredMergePlatform' }, { value })
      setAppliedPreferred(preferred)
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
  async function saveHeader(card: MergeCard, store: string) {
    if (!card.header || store === card.header.store) return
    await action(async (revision) => {
      const result = await execute(
        'identity.header',
        { workId: card.header!.root, store: store || null },
        revision,
      )
      setUndo((current) =>
        current && current.revision === revision ? { ...current, revision: result.revision } : current,
      )
    })
  }
  return (
    <div
      className="merge-queue"
      ref={root}
      role="group"
      aria-label="Possible matches"
      tabIndex={mode === 'desktop' ? 0 : undefined}
      onKeyDown={(event) => {
        if (
          mode !== 'desktop' ||
          !(event.target instanceof HTMLElement) ||
          (event.target !== root.current && !event.target.hasAttribute('data-merge-row'))
        )
          return
        const cursor = event.target.dataset.mergeRow ?? focusedRow.current
        const card = cards.find((item) => !item.actId && cursor?.startsWith(`${item.key}:`))
        const row = card?.rows.find((item) => `${card.key}:${item.workId}` === cursor)
        if (card && row && !blocked && [' ', 'Enter', 's', 'S', 'd', 'D'].includes(event.key)) {
          event.preventDefault()
          event.stopPropagation()
          if (event.key === ' ') choose(promoteMergeRow(card, row.workId))
          else if (event.key.toLowerCase() === 'd') void dismiss(card)
          else void link([card])
          return
        }
        if (!['ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) return
        const rows = Array.from(root.current?.querySelectorAll<HTMLButtonElement>('[data-merge-row]') ?? [])
        const current = rows.findIndex((row) => row.dataset.mergeRow === cursor)
        if (current < 0) return
        event.preventDefault()
        const next =
          rows[
            event.key === 'Home'
              ? 0
              : event.key === 'End'
                ? rows.length - 1
                : Math.max(0, Math.min(rows.length - 1, current + (event.key === 'ArrowDown' ? 1 : -1)))
          ]
        next?.focus()
        next?.scrollIntoView?.({ block: 'nearest' })
      }}
    >
      <div className="merge-controls">
        {mode === 'fullscreen' ? (
          <>
            <button onClick={() => setOptionSheet('sort')}>
              Sort · {sortOptions.find((option) => option.value === sort)!.label}
            </button>
            <button onClick={() => setOptionSheet('kind')}>
              Kind · {section === 'all' ? 'All proposals' : sectionNames[section]}
            </button>
            <button disabled={blocked} onClick={() => setOptionSheet('platform')}>
              Preferred platform ·{' '}
              {platformOptions.find((option) => option.value === preferred)?.label ?? storeLabel(preferred)}
            </button>
          </>
        ) : (
          <>
            <label>
              Sort proposals
              <select
                aria-label="Sort proposals"
                value={sort}
                onChange={(event) => {
                  const value = event.target.value as MergeSort
                  setSort(value)
                  setPositions({ sort: value, keys: [] })
                }}
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
                disabled={blocked}
                onChange={(event) => void savePreferred(event.target.value)}
              >
                <option value="">None</option>
                <option value="steam">Steam</option>
                <option value="epic">Epic Games</option>
                <option value="gog">GOG</option>
              </select>
            </label>
          </>
        )}
        <button
          disabled={blocked || !exact.length}
          onClick={() =>
            mode === 'fullscreen'
              ? setConfirmBatch({ cards: exact, kind: 'exact' })
              : void link(exact, 'exact')
          }
        >
          {exact.length
            ? `Accept ${exact.length} exact ${exact.length === 1 ? 'match' : 'matches'}`
            : 'No exact matches left'}
        </button>
        <button
          className="primary"
          disabled={blocked || !selected.length}
          onClick={() =>
            mode === 'fullscreen'
              ? setConfirmBatch({ cards: selected, kind: 'selected' })
              : void link(selected, 'selected')
          }
        >
          {selected.length ? `Merge ${selected.length} selected` : 'Merge selected'}
        </button>
      </div>
      {mode === 'desktop' && (
        <nav className="tabs" aria-label="Proposal kinds">
          {(['all', ...mergeSections] as const).map((kind) => (
            <button key={kind} aria-pressed={section === kind} onClick={() => setSection(kind)}>
              {kind === 'all' ? 'All proposals' : sectionNames[kind]}
            </button>
          ))}
        </nav>
      )}
      <p className="muted" role="status">
        {(() => {
          const count = shown.filter((card) => !card.actId).length
          return count
            ? `${count} ${count === 1 ? 'proposal' : 'proposals'} · non-destructive`
            : 'nothing waiting'
        })()}
        {section !== 'all' && (
          <span className="merge-count-cut" aria-label="Filtered proposals">
            {cards.filter((card) => !card.actId).length} → {shown.filter((card) => !card.actId).length}
          </span>
        )}
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
        const members = ordered.filter((card) => card.section === kind)
        if (section !== 'all' && section !== kind) return null
        return (
          <section className="merge-section" key={kind} aria-label={sectionNames[kind]}>
            <h3>{sectionNames[kind]}</h3>
            {!members.length && (
              <Empty>
                {!review.hasCompletedSweep && (kind === 'stores' || kind === 'editions')
                  ? 'Still scanning your library.'
                  : 'Nothing left to decide here.'}
              </Empty>
            )}
            {members.map((card) => (
              <article
                className={`merge-card${card.actId ? ' resolved' : ''}`}
                key={card.key}
                aria-label={`${mergeTitle(card)} ${card.actId ? 'saved group' : 'proposal'}`}
              >
                {mode === 'fullscreen' ? (
                  <button
                    className="merge-proposal"
                    data-merge-proposal={card.key}
                    data-merge-pending={!card.actId}
                    onFocus={() => {
                      focusedRow.current = card.key
                    }}
                    onClick={() => {
                      focusedRow.current = card.key
                      setOpenCard(card.key)
                    }}
                    aria-label={`${mergeTitle(card)} · ${card.rows.length} entries · ${mergePlaytime({ minutes: mergeMinutes(card), pack: false })}${card.actId ? ' · saved group' : ''}`}
                  >
                    <strong>{mergeTitle(card)}</strong>
                    <span>
                      {card.selected ? 'Selected · ' : ''}
                      {card.actId ? 'Grouped' : card.confidence} · {mergeRollup(card)}
                    </span>
                  </button>
                ) : (
                  <>
                    <header>
                      <h4>{mergeTitle(card)}</h4>
                      {!card.actId && (
                        <>
                          <span className="merge-confidence" data-confidence={card.confidence}>
                            {card.confidence}
                          </span>
                          {card.rows.some((row) => row.unread && card.included.includes(row.workId)) && (
                            <span
                              className="merge-unread"
                              aria-label={mergeActionCopy.unreadTip}
                              title={mergeActionCopy.unreadTip}
                            />
                          )}
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
                          {card.rows.length} entries ·{' '}
                          {mergePlaytime({ minutes: mergeMinutes(card), pack: false })} · nested, nothing
                          deleted
                        </p>
                        {card.header && (
                          <label>
                            Header store
                            <select
                              aria-label={`Header store for ${mergeTitle(card)}`}
                              value={card.header.store}
                              disabled={blocked}
                              onChange={(event) => void saveHeader(card, event.target.value)}
                            >
                              {card.header.options.map((option) => (
                                <option key={option.value} value={option.value}>
                                  {option.label}
                                </option>
                              ))}
                            </select>
                          </label>
                        )}
                        <button
                          disabled={blocked}
                          aria-label={mergeActionNames(card).separate}
                          title={mergeActionCopy.separateTip}
                          onClick={() => void reverse(card.actId)}
                        >
                          {mergeActionCopy.separate}
                        </button>
                      </div>
                    ) : (
                      <>
                        <div className="merge-rows">
                          {card.rows.map((row, index) => (
                            <div
                              className={`merge-row${row.workId === card.parent ? ' main-row' : ''}`}
                              data-included={card.included.includes(row.workId)}
                              key={row.workId}
                              onMouseEnter={() => setHovered({ card: card.key, work: row.workId })}
                              onMouseLeave={() => setHovered(null)}
                              onClick={(event) => {
                                if (!blocked && !(event.target as HTMLElement).closest('button,input'))
                                  choose(promoteMergeRow(card, row.workId))
                              }}
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
                              {(() => {
                                const { saturation, brightness, hue } = dormancy(row.lastPlayedAt)
                                return (
                                  <span
                                    className="merge-cover"
                                    aria-hidden="true"
                                    data-work-id={row.workId}
                                    style={
                                      {
                                        '--merge-dormancy': dimCovers
                                          ? `saturate(${saturation}) hue-rotate(${hue}deg) brightness(${brightness})`
                                          : 'none',
                                      } as CSSProperties
                                    }
                                  >
                                    <span className="merge-cover-fallback">{row.title}</span>
                                    <Artwork workId={row.workId} />
                                  </span>
                                )
                              })()}
                              <button
                                className="merge-row-title"
                                data-merge-row={`${card.key}:${row.workId}`}
                                onFocus={() => {
                                  focusedRow.current = `${card.key}:${row.workId}`
                                }}
                                type="button"
                                disabled={blocked}
                                onClick={() => choose(promoteMergeRow(card, row.workId))}
                                aria-label={`Choose ${rowLabels.get(card.key)![index]}`}
                                aria-description={mergeRowDetail(row)}
                              >
                                <strong>{row.title}</strong>
                                <span className="merge-row-mark">
                                  {mergeRowMark(card.parent, card.included, row)}
                                </span>
                              </button>
                              <span className="merge-row-stores">
                                {row.stores.map((store) => (
                                  <span className="merge-store" key={store}>
                                    {store === 'epic' ? 'EPIC' : storeLabel(store).toLocaleUpperCase()}
                                  </span>
                                ))}
                              </span>
                              <span className="merge-row-facts">
                                <span className="merge-row-hours">{mergePlaytime(row)}</span>
                                <span className="merge-row-idle">{mergeIdle(row)}</span>
                              </span>
                              {row.unread && (
                                <span
                                  className="merge-unread"
                                  aria-label={mergeActionCopy.unreadTip}
                                  title={mergeActionCopy.unreadTip}
                                />
                              )}
                              {onOpenGame && (
                                <button
                                  className="merge-row-details"
                                  disabled={blocked}
                                  aria-label={`Details for ${rowLabels.get(card.key)![index]}`}
                                  onClick={() => openGame(card, row.workId)}
                                >
                                  Details
                                </button>
                              )}
                              {row.workId !== card.parent && (
                                <input
                                  type="checkbox"
                                  aria-label={`Include ${rowLabels.get(card.key)![index]}`}
                                  checked={card.included.includes(row.workId)}
                                  disabled={blocked || row.workId === card.parent}
                                  onChange={(event) =>
                                    choose(includeMergeRow(card, row.workId, event.target.checked))
                                  }
                                />
                              )}
                            </div>
                          ))}
                        </div>
                        <p className="merge-reason" data-row-detail={hovered?.card === card.key}>
                          {hovered?.card === card.key
                            ? (() => {
                                const row = card.rows.find((member) => member.workId === hovered.work)!
                                return mergeRowDetail(row)
                              })()
                            : card.reason}
                        </p>
                        <p className="muted">{mergeRollup(card)}</p>
                        <div className="form-actions">
                          <button
                            className="primary"
                            disabled={blocked || !mergeAnswer(card).childWorkIds.length}
                            aria-label={mergeActionNames(card).same}
                            title={mergeActionCopy.sameTip}
                            onClick={() => void link([card])}
                          >
                            {mergeActionCopy.same}
                          </button>
                          <button
                            disabled={blocked}
                            aria-label={mergeActionNames(card).different}
                            title={mergeActionCopy.differentTip}
                            onClick={() => void dismiss(card)}
                          >
                            {mergeActionCopy.different}
                          </button>
                          <button
                            disabled={blocked}
                            onClick={() =>
                              onReview(
                                card.parent,
                                mergeAnswer(card).childWorkIds,
                                card.kind,
                                card.label ?? '',
                              )
                            }
                          >
                            {card.kind === 'same_game' ? 'Same game…' : 'Review relationship…'}
                          </button>
                        </div>
                      </>
                    )}
                  </>
                )}
              </article>
            ))}
          </section>
        )
      })}
      {mode === 'fullscreen' &&
        openCard &&
        cards.find((card) => card.key === openCard) &&
        (() => {
          const card = cards.find((entry) => entry.key === openCard)!
          return (
            <MergeMemberSheet
              card={card}
              disabled={blocked}
              onClose={() => setOpenCard(null)}
              onPromote={(id) => choose(promoteMergeRow(card, id))}
              onInclude={(id, value) => choose(includeMergeRow(card, id, value))}
              onSelect={() => choose({ ...card, selected: !card.selected })}
              onLink={() => void link([card])}
              onDismiss={() => void dismiss(card)}
              onSeparate={() => void reverse(card.actId)}
              onHeader={(store) => void saveHeader(card, store)}
              onOpenGame={onOpenGame ? (workId) => openGame(card, workId) : undefined}
              onReview={() =>
                onReview(card.parent, mergeAnswer(card).childWorkIds, card.kind, card.label ?? '')
              }
            />
          )
        })()}
      {confirmBatch && (
        <MergeBatchConfirmation
          count={confirmBatch.cards.length}
          onClose={() => setConfirmBatch(null)}
          onConfirm={() => void link(confirmBatch.cards, confirmBatch.kind)}
        />
      )}
      {optionSheet && (
        <MergeOptionsSheet
          disabled={optionSheet === 'platform' && blocked}
          title={
            optionSheet === 'sort'
              ? 'Sort possible matches'
              : optionSheet === 'kind'
                ? 'Match kind'
                : 'Preferred platform for pending headers'
          }
          value={optionSheet === 'sort' ? sort : optionSheet === 'kind' ? section : preferred}
          options={
            optionSheet === 'sort'
              ? sortOptions
              : optionSheet === 'kind'
                ? [
                    { value: 'all', label: 'All proposals' },
                    ...mergeSections.map((kind) => ({ value: kind, label: sectionNames[kind] })),
                  ]
                : platformOptions
          }
          onClose={() => setOptionSheet(null)}
          onChoose={(value) => {
            if (optionSheet === 'sort') {
              setSort(value as MergeSort)
              setPositions({ sort: value as MergeSort, keys: [] })
            } else if (optionSheet === 'kind') setSection(value as MergeSection | 'all')
            else void savePreferred(value)
          }}
        />
      )}
      {undo && undo.expiresAt > Date.now() && (
        <aside className="merge-undo" aria-label="Review undo" role="status">
          <span>
            <strong>{mergeDock(undo).title}</strong>
            <span className="merge-dock-note">{mergeDock(undo).note}</span>
          </span>
          <button disabled={blocked} title={mergeActionCopy.undoTip} onClick={() => void reverse()}>
            Undo review decisions
          </button>
          <button
            aria-label="Dismiss review undo"
            title={mergeActionCopy.dismissTip}
            disabled={busy}
            onClick={() => setUndo(null)}
          >
            Dismiss
          </button>
        </aside>
      )}
      {refusalUntil !== null && (
        <aside className="merge-undo" role="status" aria-label="Review notice">
          <span>
            <strong>Couldn't link those.</strong>
            <span className="merge-dock-note">That proposal was out of date · nothing changed.</span>
          </span>
          <button aria-label="Dismiss review notice" onClick={() => setRefusalUntil(null)}>
            Dismiss
          </button>
        </aside>
      )}
    </div>
  )
}
