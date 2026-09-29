import type { IdentityReview } from '../api/types'
import { storeLabel } from '../api/client'

export const mergeSections = ['stores', 'editions', 'expansions', 'parts', 'tests'] as const
export type MergeSection = (typeof mergeSections)[number]
export type MergeSort = 'strength' | 'playtime' | 'title'
export type RefusedPair = { baseWorkId: number; childWorkId: number }
export interface MergeLink {
  id: number
  actId: number
  parentWorkId: number
  childWorkId: number
  kind: string
  relationLabel?: string | null
  retractedAt?: string | null
  appliedAt?: string
}
export interface MergeReview extends IdentityReview {
  candidates: (IdentityReview['candidates'][number] & { signalsJson?: string | null })[]
  history: MergeLink[]
  expansions: {
    base: { workId: number; title: string; releaseIds?: number[] }
    members: {
      work: { workId: number; title: string; releaseIds?: number[] }
      kind: string
      relationLabel?: string | null
      fromMetadata: boolean
    }[]
  }[]
}
type Work = {
  id: number
  name: string
  igdbId?: number | null
  nameIsProvisional?: boolean
  firstReleaseYear?: number | null
  publisher?: string | null
}
type Bucket = {
  ownershipId: number
  releaseId: number
  playtimeMinutes: number
  lastPlayedAt?: string | null
  bucket: string
}
type Ownership = {
  id: number
  releaseId: number
  store: string
  acquiredAt?: string | null
  installed?: boolean
}
export interface MergeRow {
  workId: number
  title: string
  releaseIds: number[]
  stores: string[]
  minutes: number
  lastPlayedAt: string | null
  acquiredAt: string | null
  unread: boolean
  installed: boolean
  year: number | null
  publisher: string | null
  pack: boolean
}
export interface MergeEdge {
  candidateId: number
  left: number
  right: number
  score: number
  priority: boolean
  titleSimilarity: number
  hasBreakdown?: boolean
  publisherMatch?: boolean
  yearDelta?: number
}
export interface MergeCard {
  key: string
  section: MergeSection
  confidence: 'Exact match' | 'Likely' | 'Worth a look'
  score: number
  rows: MergeRow[]
  parent: number
  included: number[]
  kind: string
  label: string | null
  edges: MergeEdge[]
  pairs: RefusedPair[]
  actId?: number
  selected: boolean
  reason: string
}
export interface MergeAnswer {
  parentWorkId: number
  childWorkIds: number[]
  kind: string
  relationLabel: string | null
  rejectedCandidateIds: number[]
  refusedPairs: RefusedPair[]
}
export interface MergeUndo {
  kind: 'merge' | 'dismiss'
  actIds: number[]
  candidateIds: number[]
  refusedPairs: RefusedPair[]
  revision: string
  expiresAt: number
  count: number
}

export function mergePayload(
  json?: string | null,
): Pick<MergeEdge, 'priority' | 'titleSimilarity' | 'hasBreakdown' | 'publisherMatch' | 'yearDelta'> {
  try {
    const value: unknown = JSON.parse(json ?? 'null')
    if (!value || typeof value !== 'object') return { priority: false, titleSimilarity: 0 }
    const payload = value as Record<string, unknown>
    return {
      priority: payload.band === 'Priority',
      titleSimilarity: typeof payload.title_similarity === 'number' ? payload.title_similarity : 0,
      hasBreakdown: true,
      publisherMatch: typeof payload.publisher_match === 'boolean' ? payload.publisher_match : undefined,
      yearDelta: typeof payload.year_delta === 'number' ? payload.year_delta : undefined,
    }
  } catch {
    return { priority: false, titleSimilarity: 0 }
  }
}
export function relationSection(kind: string, label?: string | null): MergeSection {
  return kind === 'variant_of'
    ? 'tests'
    : ['episode', 'season'].includes(label?.toLowerCase() ?? '')
      ? 'parts'
      : 'expansions'
}
export function mergeTitle(card: MergeCard) {
  return card.rows.find((row) => row.workId === card.parent)!.title
}
export function mergeMemberLabels(card: MergeCard): string[] {
  const labels = (depth: number) =>
    card.rows.map((row, index) => {
      const qualifiers = [
        depth >= 1 ? row.stores.map(storeLabel).join(', ') : '',
        depth >= 2 ? row.year : '',
        depth >= 3 ? row.publisher : '',
        depth >= 4 ? `${index + 1} of ${card.rows.length}` : '',
      ].filter(Boolean)
      return qualifiers.length ? `${row.title} (${qualifiers.join(', ')})` : row.title
    })
  for (let depth = 0; depth < 4; depth++) {
    const result = labels(depth)
    if (new Set(result).size === result.length) return result
  }
  return labels(4)
}
function sameGameReason(strongest: MergeEdge, rows: MergeRow[], edges: MergeEdge[], parent: number): string {
  const sameTitles = rows.every(
    (row) => row.title.trim().toLocaleLowerCase() === rows[0]!.title.trim().toLocaleLowerCase(),
  )
  const stores = [...new Set(rows.flatMap((row) => row.stores))].map(storeLabel)
  const storeNames = stores.length > 1 ? `${stores.slice(0, -1).join(', ')} and ${stores.at(-1)}` : stores[0]
  const sentences = [
    !strongest.hasBreakdown
      ? 'The matcher recorded no breakdown for this pair.'
      : strongest.titleSimilarity >= 0.999
        ? !sameTitles
          ? 'Same title apart from the edition.'
          : stores.length > 1
            ? `Same title on ${storeNames}.`
            : 'Same title.'
        : `${strongest.titleSimilarity.toFixed(2)} name match.`,
  ]
  const clauses = [
    strongest.publisherMatch === true
      ? 'Same publisher'
      : strongest.publisherMatch === false
        ? 'Different publishers'
        : '',
  ]
  if (strongest.yearDelta !== undefined) {
    const gap = Math.abs(strongest.yearDelta)
    clauses.push(gap === 0 ? 'same year' : gap === 1 ? 'a year apart' : `${gap} years apart`)
  }
  const clause = clauses.filter(Boolean).join(', ')
  if (clause) sentences.push(`${clause[0]!.toUpperCase()}${clause.slice(1)}.`)
  for (const row of rows) {
    if (
      row.workId === parent ||
      edges.some(
        (edge) =>
          (edge.left === row.workId && edge.right === parent) ||
          (edge.right === row.workId && edge.left === parent),
      )
    )
      continue
    const edge = [...edges]
      .filter((edge) => edge.left === row.workId || edge.right === row.workId)
      .sort((a, b) => b.score - a.score)[0]
    const through = rows.find(
      (entry) => entry.workId === (edge?.left === row.workId ? edge.right : edge?.left),
    )
    if (through) sentences.push(`${row.title} reached this group through ${through.title}.`)
  }
  return sentences.join(' ')
}
export function mergeMinutes(card: MergeCard) {
  return card.rows
    .filter((row) => card.included.includes(row.workId))
    .reduce((sum, row) => sum + row.minutes, 0)
}
export function promoteMergeRow(card: MergeCard, workId: number): MergeCard {
  if (!card.rows.some((row) => row.workId === workId))
    throw new Error('That game is not a member of this group.')
  if (card.actId || card.kind !== 'same_game') return card
  return { ...card, parent: workId, included: [...new Set([...card.included, workId])] }
}
export function includeMergeRow(card: MergeCard, workId: number, include: boolean): MergeCard {
  if (card.actId || workId === card.parent || !card.rows.some((row) => row.workId === workId)) return card
  return {
    ...card,
    included: include
      ? [...new Set([...card.included, workId])]
      : card.included.filter((id) => id !== workId),
  }
}
export function mergeAnswer(card: MergeCard): MergeAnswer {
  const inside = new Set([...card.included, card.parent])
  return {
    parentWorkId: card.parent,
    childWorkIds: card.rows
      .map((row) => row.workId)
      .filter((id) => id !== card.parent && inside.has(id))
      .sort((a, b) => a - b),
    kind: card.kind,
    relationLabel: card.label,
    rejectedCandidateIds: card.edges
      .filter((edge) => inside.has(edge.left) !== inside.has(edge.right))
      .map((edge) => edge.candidateId),
    refusedPairs: card.pairs.filter((pair) => !inside.has(pair.childWorkId)),
  }
}
export function sortMergeCards(cards: MergeCard[], sort: MergeSort): MergeCard[] {
  const tiers = { 'Exact match': 0, Likely: 1, 'Worth a look': 2 }
  return [...cards].sort((a, b) => {
    if (Boolean(a.actId) !== Boolean(b.actId)) return a.actId ? 1 : -1
    if (a.actId && b.actId) return b.actId - a.actId
    const order =
      sort === 'title'
        ? mergeTitle(a).localeCompare(mergeTitle(b), undefined, { sensitivity: 'base' })
        : sort === 'playtime'
          ? mergeMinutes(b) - mergeMinutes(a)
          : 0
    return (
      order || tiers[a.confidence] - tiers[b.confidence] || b.score - a.score || a.key.localeCompare(b.key)
    )
  })
}
export function exactMergeCards(cards: MergeCard[], section: MergeSection | 'all') {
  return section !== 'all' && section !== 'stores'
    ? []
    : cards.filter(
        (card) =>
          !card.actId &&
          card.section === 'stores' &&
          card.confidence === 'Exact match' &&
          mergeAnswer(card).childWorkIds.length > 0,
      )
}
export function extendMergeUndo(
  previous: MergeUndo | null,
  next: Omit<MergeUndo, 'expiresAt'>,
  now = Date.now(),
): MergeUndo {
  const accumulate = next.kind === 'dismiss' && previous?.kind === 'dismiss' && previous.expiresAt > now
  return {
    ...next,
    expiresAt: now + 7000,
    actIds: [...new Set([...(accumulate ? previous.actIds : []), ...next.actIds])],
    candidateIds: [...new Set([...(accumulate ? previous.candidateIds : []), ...next.candidateIds])],
    refusedPairs: [
      ...new Map(
        [...(accumulate ? previous.refusedPairs : []), ...next.refusedPairs].map((pair) => [
          `${pair.baseWorkId}:${pair.childWorkId}`,
          pair,
        ]),
      ).values(),
    ],
    count: next.count + (accumulate ? previous.count : 0),
  }
}

/** Project the same five review sections from the authoritative API snapshot. */
export function buildMergeCards(review: MergeReview): MergeCard[] {
  const works = new Map((review.workspace.works as Work[]).map((work) => [work.id, work]))
  const releases = new Map(review.workspace.releases.map((release) => [release.id, release.workId]))
  const ownerships = (review.workspace.ownerships ?? []) as Ownership[]
  const buckets = (review.workspace.buckets ?? []) as Bucket[]
  const links = (review.history ?? []).filter((link) => !link.retractedAt)
  const parentOf = new Map(
    links.filter((link) => link.kind === 'same_game').map((link) => [link.childWorkId, link.parentWorkId]),
  )
  const nonSameChildren = new Set(
    links.filter((link) => link.kind !== 'same_game').map((link) => link.childWorkId),
  )
  const resolve = (id: number) => {
    const seen = new Set<number>()
    let root = id
    while (parentOf.has(root)) {
      if (seen.has(root)) return id
      seen.add(root)
      root = parentOf.get(root)!
    }
    return root
  }
  const releasesOf = (id: number) =>
    [...releases].filter(([, work]) => work === id).map(([release]) => release)
  function row(id: number, releaseIds = releasesOf(id), pack = false): MergeRow {
    const work = works.get(id),
      owned = ownerships.filter((entry) => releaseIds.includes(entry.releaseId)),
      played = buckets.filter((entry) => releaseIds.includes(entry.releaseId))
    return {
      workId: id,
      title: work?.name ?? `Game ${id}`,
      releaseIds,
      stores: [...new Set(owned.map((entry) => entry.store))],
      minutes: played.reduce((sum, entry) => sum + entry.playtimeMinutes, 0),
      lastPlayedAt:
        played
          .map((entry) => entry.lastPlayedAt)
          .filter((date): date is string => Boolean(date))
          .sort()
          .at(-1) ?? null,
      acquiredAt:
        owned
          .map((entry) => entry.acquiredAt)
          .filter((date): date is string => Boolean(date))
          .sort()[0] ?? null,
      unread: played.some((entry) => entry.bucket === 'stale_but_patched'),
      installed: owned.some((entry) => entry.installed),
      year: work?.firstReleaseYear ?? null,
      publisher: work?.publisher ?? null,
      pack,
    }
  }
  const edges: MergeEdge[] = [],
    namedReleases = new Map<number, Set<number>>()
  for (const candidate of review.candidates) {
    if (candidate.status !== 'pending') continue
    const leftWork = releases.get(candidate.leftReleaseId),
      rightWork = releases.get(candidate.rightReleaseId)
    if (
      leftWork === undefined ||
      rightWork === undefined ||
      nonSameChildren.has(leftWork) ||
      nonSameChildren.has(rightWork)
    )
      continue
    const left = resolve(leftWork),
      right = resolve(rightWork)
    if (left === right) continue
    for (const [work, release] of [
      [left, candidate.leftReleaseId],
      [right, candidate.rightReleaseId],
    ]) {
      if (!namedReleases.has(work!)) namedReleases.set(work!, new Set())
      namedReleases.get(work!)!.add(release!)
    }
    edges.push({
      candidateId: candidate.id,
      left,
      right,
      score: candidate.score,
      ...mergePayload(candidate.signalsJson),
    })
  }
  const adjacent = new Map<number, Set<number>>()
  for (const edge of edges)
    for (const [a, b] of [
      [edge.left, edge.right],
      [edge.right, edge.left],
    ]) {
      if (!adjacent.has(a!)) adjacent.set(a!, new Set())
      adjacent.get(a!)!.add(b!)
    }
  const seen = new Set<number>(),
    cards: MergeCard[] = []
  const comparePrimary = (a: number, b: number) =>
    Number(Boolean(works.get(b)?.igdbId)) - Number(Boolean(works.get(a)?.igdbId)) ||
    Number(Boolean(works.get(a)?.nameIsProvisional)) - Number(Boolean(works.get(b)?.nameIsProvisional)) ||
    releasesOf(b).length - releasesOf(a).length ||
    a - b
  for (const start of [...adjacent.keys()].sort((a, b) => a - b)) {
    if (seen.has(start)) continue
    const members = [start]
    seen.add(start)
    for (let i = 0; i < members.length; i++)
      for (const neighbour of adjacent.get(members[i]!) ?? [])
        if (!seen.has(neighbour)) {
          seen.add(neighbour)
          members.push(neighbour)
        }
    const groupEdges = edges.filter((edge) => members.includes(edge.left)),
      strongest = [...groupEdges].sort((a, b) => b.score - a.score)[0]!
    const parent = [...members].sort(comparePrimary)[0]!
    const best = (id: number) =>
      Math.max(
        ...groupEdges.filter((edge) => edge.left === id || edge.right === id).map((edge) => edge.score),
      )
    const ordered = members.sort(
      (a, b) => Number(b === parent) - Number(a === parent) || best(b) - best(a) || a - b,
    )
    const rows = ordered.map((id) =>
      row(
        id,
        [...namedReleases.get(id)!].sort((a, b) => a - b),
      ),
    )
    const sameTitles = rows.every(
      (entry) => entry.title.trim().toLocaleLowerCase() === rows[0]!.title.trim().toLocaleLowerCase(),
    )
    cards.push({
      key: `merge-card-${Math.min(...members)}`,
      section: new Set(rows.flatMap((entry) => entry.stores)).size >= 2 ? 'stores' : 'editions',
      confidence:
        strongest.priority && sameTitles && strongest.titleSimilarity >= 0.999
          ? 'Exact match'
          : strongest.priority
            ? 'Likely'
            : 'Worth a look',
      score: strongest.score,
      rows,
      parent,
      included: [...members],
      kind: 'same_game',
      label: null,
      edges: groupEdges,
      pairs: [],
      selected: false,
      reason: sameGameReason(strongest, rows, groupEdges, parent),
    })
  }
  for (const expansion of review.expansions ?? [])
    for (const section of ['expansions', 'parts', 'tests'] as const) {
      const members = expansion.members.filter(
        (member) => relationSection(member.kind, member.relationLabel) === section,
      )
      if (!members.length) continue
      const declared = members.filter((member) => member.fromMetadata).length
      const rows = [
        row(expansion.base.workId, expansion.base.releaseIds),
        ...members.map((member) => row(member.work.workId, member.work.releaseIds, true)),
      ]
      cards.push({
        key: `expansion-card-${section}-${expansion.base.workId}`,
        section,
        confidence: declared === members.length ? 'Exact match' : 'Likely',
        score: 0.5 + (0.5 * declared) / members.length,
        rows,
        parent: expansion.base.workId,
        included: rows.map((entry) => entry.workId),
        kind: section === 'tests' ? 'variant_of' : 'expansion_of',
        label: members.every((member) => member.relationLabel === members[0]!.relationLabel)
          ? (members[0]!.relationLabel ?? null)
          : null,
        edges: [],
        pairs: members.map((member) => ({
          baseWorkId: expansion.base.workId,
          childWorkId: member.work.workId,
        })),
        selected: false,
        reason:
          declared === members.length
            ? members.length === 1
              ? `“${members[0]!.work.title}” is listed under ${expansion.base.title} on the store.`
              : `${members.length} entries declare ${expansion.base.title} as their parent on the store.`
            : `${members.length} titles extend ${expansion.base.title} by name.`,
      })
    }
  for (const actId of [...new Set(links.map((link) => link.actId))]) {
    const actLinks = links.filter((link) => link.actId === actId),
      first = actLinks[0]!
    const rows = [...new Set([first.parentWorkId, ...actLinks.map((link) => link.childWorkId)])].map((id) =>
      row(id),
    )
    cards.push({
      key: `act-${actId}`,
      section:
        first.kind === 'same_game'
          ? new Set(rows.flatMap((entry) => entry.stores)).size >= 2
            ? 'stores'
            : 'editions'
          : relationSection(first.kind, first.relationLabel),
      confidence: 'Exact match',
      score: 0,
      rows,
      parent: first.parentWorkId,
      included: rows.map((entry) => entry.workId),
      kind: first.kind,
      label: first.relationLabel ?? null,
      edges: [],
      pairs: [],
      actId,
      selected: false,
      reason: '',
    })
  }
  return cards
}
