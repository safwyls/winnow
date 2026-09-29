import { describe, expect, it } from 'vitest'
import {
  buildMergeCards,
  exactMergeCards,
  extendMergeUndo,
  includeMergeRow,
  mergeAnswer,
  mergeMinutes,
  mergeMemberLabels,
  mergePayload,
  mergeTitle,
  promoteMergeRow,
  sortMergeCards,
  type MergeReview,
} from '../src/renderer/features/parity-merge-model'

import { mergeFixture } from './parity-merge-fixtures'
describe('original grouped review projection', () => {
  it('projects cross-store and same-store pairs into separate sections with one card per connected triple', () => {
    const review = mergeFixture()
    expect(
      buildMergeCards(review).map((card) => [card.section, card.rows.length, card.edges.length]),
    ).toEqual([
      ['stores', 2, 1],
      ['editions', 2, 1],
    ])
    review.candidates = [
      review.candidates[0]!,
      { id: 12, leftReleaseId: 101, rightReleaseId: 103, score: 0.98, status: 'pending' },
      { id: 13, leftReleaseId: 102, rightReleaseId: 103, score: 0.98, status: 'pending' },
    ]
    review.workspace.works[2]!.name = 'Bastion'
    ;(review.workspace.ownerships as { store: string }[])[2]!.store = 'epic'
    const [card] = buildMergeCards(review)
    expect(buildMergeCards(review)).toHaveLength(1)
    expect(card!.rows.map((row) => row.workId)).toEqual([1, 2, 3])
    expect(card!.edges.map((edge) => edge.candidateId)).toEqual([10, 12, 13])
    expect(new Set(mergeMemberLabels(card!)).size).toBe(3)
  })
  it('renders exact, edition-normalized and forced-review evidence without recomputing the stored band', () => {
    const review = mergeFixture()
    review.candidates = [review.candidates[0]!]
    review.candidates[0]!.signalsJson = JSON.stringify({
      band: 'Priority',
      title_similarity: 1,
      publisher_match: true,
      year_delta: 0,
    })
    let card = buildMergeCards(review)[0]!
    expect(card.confidence).toBe('Exact match')
    expect(card.reason).toBe('Same title on Steam and GOG. Same publisher, same year.')
    review.workspace.works[1]!.name = 'Bastion: Game of the Year'
    review.candidates[0]!.signalsJson = JSON.stringify({
      band: 'Priority',
      title_similarity: 1,
      publisher_match: true,
      year_delta: 1,
    })
    card = buildMergeCards(review)[0]!
    expect(card.confidence).toBe('Likely')
    expect(card.reason).toContain('Same title apart from the edition. Same publisher, a year apart.')
    review.workspace.works[1]!.name = 'Bastion'
    for (const score of [0.65, 1]) {
      review.candidates[0]!.score = score
      review.candidates[0]!.signalsJson = JSON.stringify({ band: 'Review', title_similarity: 1 })
      expect(buildMergeCards(review)[0]!.confidence).toBe('Worth a look')
    }
  })
  it.each([
    ['same_game', 'stores', null],
    ['same_game', 'editions', null],
    ['expansion_of', 'expansions', 'expansion'],
    ['variant_of', 'tests', 'demo'],
  ] as const)('loads standing %s acts in %s without duplicating their proposal', (kind, section, label) => {
    const review = mergeFixture()
    review.candidates = [review.candidates[0]!]
    if (section === 'editions') (review.workspace.ownerships as { store: string }[])[1]!.store = 'steam'
    review.history = [{ id: 8, actId: 9, parentWorkId: 1, childWorkId: 2, kind, relationLabel: label }]
    const cards = buildMergeCards(review)
    expect(cards).toHaveLength(1)
    expect(cards[0]).toMatchObject({ actId: 9, section, parent: 1, included: [1, 2], kind, label })
    expect(cards[0]!.selected).toBe(false)
  })
  it('offers saved-group automatic, available and unavailable header stores without changing the identity parent', () => {
    const review = mergeFixture()
    review.history = [{ id: 8, actId: 9, parentWorkId: 1, childWorkId: 2, kind: 'same_game' }]
    review.workspace.works[1]!.name = 'Bastion GOG edition'
    review.workspace.preferredHeaderStores = { '1': 'gog' }
    let card = buildMergeCards(review).find((card) => card.actId)!
    expect(card.parent).toBe(1)
    expect(mergeTitle(card)).toBe('Bastion GOG edition')
    expect(card.header?.options.map((option) => option.value)).toEqual(['', 'gog', 'steam'])
    review.workspace.preferredHeaderStores = { '1': 'epic' }
    card = buildMergeCards(review).find((card) => card.actId)!
    expect(mergeTitle(card)).toBe('Bastion')
    expect(card.header?.store).toBe('epic')
    expect(card.header?.options.at(-1)).toEqual({ value: 'epic', label: 'Epic Games (unavailable)' })
  })
  it('builds one connected work card and drops already resolved or missing release pairs', () => {
    const review = mergeFixture()
    review.candidates.push(
      { id: 12, leftReleaseId: 102, rightReleaseId: 103, score: 0.7, status: 'pending' },
      { id: 13, leftReleaseId: 999, rightReleaseId: 104, score: 1, status: 'pending' },
    )
    expect(buildMergeCards(review)).toHaveLength(1)
    expect(buildMergeCards(review)[0]!.rows.map((row) => row.workId)).toEqual([1, 2, 3, 4])
    review.history = [{ id: 1, actId: 9, parentWorkId: 1, childWorkId: 2, kind: 'same_game' }]
    expect(
      buildMergeCards(review)
        .find((card) => !card.actId)!
        .edges.map((edge) => edge.candidateId),
    ).toEqual([11, 12])
    expect(
      buildMergeCards(review)
        .find((card) => !card.actId)!
        .rows.map((row) => row.workId),
    ).toEqual([1, 3, 4])
  })
  it('does not offer an expansion or variant child as a same-game parent', () => {
    for (const kind of ['expansion_of', 'variant_of']) {
      const review = mergeFixture()
      review.history = [{ id: 1, actId: 9, parentWorkId: 1, childWorkId: 3, kind }]
      expect(
        buildMergeCards(review)
          .filter((card) => !card.actId)
          .flatMap((card) => card.rows.map((row) => row.workId)),
      ).toEqual([1, 2])
    }
  })
  it('uses IGDB, named title, release count and added-first ladder for the default header', () => {
    const review = mergeFixture()
    const works = review.workspace.works as (MergeReview['workspace']['works'][number] & {
      nameIsProvisional?: boolean
    })[]
    expect(buildMergeCards(review)[0]!.parent).toBe(1)
    review.workspace.releases.push({ id: 222, workId: 2 })
    expect(buildMergeCards(review)[0]!.parent).toBe(2)
    works[1]!.nameIsProvisional = true
    expect(buildMergeCards(review)[0]!.parent).toBe(1)
    works[1]!.igdbId = 42
    expect(buildMergeCards(review)[0]!.parent).toBe(2)
  })
  it('requires unchanged storefront spelling and priority evidence for exact cross-store acceptance', () => {
    const review = mergeFixture()
    expect(exactMergeCards(buildMergeCards(review), 'all')).toHaveLength(1)
    review.workspace.works[1]!.name = 'Bastion Remastered'
    expect(buildMergeCards(review)[0]!.confidence).toBe('Likely')
    expect(exactMergeCards(buildMergeCards(review), 'all')).toEqual([])
    review.candidates[0]!.signalsJson = '{broken'
    expect(buildMergeCards(review)[0]!.confidence).toBe('Worth a look')
    expect(mergePayload('null')).toEqual({ priority: false, titleSimilarity: 0 })
  })
  it('promotes a group member, reincludes it, rejects foreign rows and protects expansion parents', () => {
    const card = buildMergeCards(mergeFixture())[0]!
    const excluded = includeMergeRow(card, 2, false)
    expect(mergeAnswer(excluded).childWorkIds).toEqual([])
    const promoted = promoteMergeRow(excluded, 2)
    expect(promoted).toMatchObject({ parent: 2, included: [1, 2] })
    expect(mergeAnswer(promoted).childWorkIds).toEqual([1])
    expect(includeMergeRow(promoted, 2, false)).toBe(promoted)
    expect(() => promoteMergeRow(card, 4)).toThrow('not a member')
    expect(card.parent).toBe(1)
    expect(promoteMergeRow({ ...card, kind: 'expansion_of' }, 2).parent).toBe(1)
    expect(includeMergeRow(card, 1, false)).toBe(card)
  })
  it('rejects only edges crossing into excluded rows and leaves outside-to-outside proposals unanswered', () => {
    const review = mergeFixture()
    review.candidates.push({ id: 12, leftReleaseId: 102, rightReleaseId: 103, score: 0.7, status: 'pending' })
    let card = buildMergeCards(review)[0]!
    card = includeMergeRow(includeMergeRow(card, 3, false), 4, false)
    expect(mergeAnswer(card)).toMatchObject({
      parentWorkId: 1,
      childWorkIds: [2],
      rejectedCandidateIds: [12],
      refusedPairs: [],
    })
  })
  it('splits expansions, episodes and demos with fixed base parents and directional refusals', () => {
    const review = mergeFixture()
    review.expansions = [
      {
        base: { workId: 1, title: 'Bastion' },
        members: [
          {
            work: { workId: 2, title: 'Pack' },
            kind: 'expansion_of',
            relationLabel: 'expansion',
            fromMetadata: true,
          },
          {
            work: { workId: 3, title: 'Episode' },
            kind: 'expansion_of',
            relationLabel: 'episode',
            fromMetadata: false,
          },
          {
            work: { workId: 4, title: 'Demo' },
            kind: 'variant_of',
            relationLabel: 'demo',
            fromMetadata: true,
          },
        ],
      },
    ]
    const cards = buildMergeCards(review).filter((card) => card.kind !== 'same_game')
    expect(cards.map((card) => [card.section, card.parent, card.kind, card.confidence])).toEqual([
      ['expansions', 1, 'expansion_of', 'Exact match'],
      ['parts', 1, 'expansion_of', 'Likely'],
      ['tests', 1, 'variant_of', 'Exact match'],
    ])
    expect(mergeAnswer(includeMergeRow(cards[0]!, 2, false)).refusedPairs).toEqual([
      { baseWorkId: 1, childWorkId: 2 },
    ])
    expect(exactMergeCards(cards, 'all')).toEqual([])
  })
  it('groups both scanned packs under one fixed base with pack marks, likely evidence and two refusal directions', () => {
    const review = mergeFixture()
    review.candidates = []
    review.expansions = [
      {
        base: { workId: 1, title: 'Bastion' },
        members: [2, 3].map((id) => ({
          work: { workId: id, title: `Bastion pack ${id}` },
          kind: 'expansion_of',
          relationLabel: 'expansion',
          fromMetadata: false,
        })),
      },
    ]
    const cards = buildMergeCards(review)
    expect(cards).toHaveLength(1)
    expect(cards[0]).toMatchObject({
      parent: 1,
      section: 'expansions',
      kind: 'expansion_of',
      confidence: 'Likely',
      edges: [],
      included: [1, 2, 3],
    })
    expect(cards[0]!.rows.map((row) => [row.workId, row.pack])).toEqual([
      [1, false],
      [2, true],
      [3, true],
    ])
    expect(cards[0]!.reason).toContain('Bastion')
    expect(cards[0]!.pairs).toEqual([
      { baseWorkId: 1, childWorkId: 2 },
      { baseWorkId: 1, childWorkId: 3 },
    ])
  })
  it('rolls up entry hours and ownership evidence from the same included rows', () => {
    const cards = buildMergeCards(mergeFixture())
    expect(mergeMinutes(cards[0]!)).toBe(180)
    expect(mergeMinutes(includeMergeRow(cards[0]!, 2, false))).toBe(60)
    expect(cards[0]!.rows[0]).toMatchObject({ acquiredAt: '2012-01-01T00:00:00Z', stores: ['steam'] })
    expect(cards[1]!.rows[0]).toMatchObject({
      unread: true,
      installed: true,
      lastPlayedAt: '2026-01-01T00:00:00Z',
    })
  })
  it('sorts confidence before score, supports summed hours and title, and leaves saved acts last', () => {
    const cards = buildMergeCards(mergeFixture())
    const resolved = { ...cards[0]!, key: 'act-1', actId: 1 }
    expect(sortMergeCards([...cards, resolved], 'strength').map((card) => card.key)).toEqual([
      'merge-card-1',
      'merge-card-3',
      'act-1',
    ])
    expect(sortMergeCards([...cards, resolved], 'playtime').map((card) => card.key)).toEqual([
      'merge-card-3',
      'merge-card-1',
      'act-1',
    ])
    expect(sortMergeCards([...cards, resolved], 'title').map((card) => card.key)).toEqual([
      'merge-card-1',
      'merge-card-3',
      'act-1',
    ])
    expect(exactMergeCards(cards, 'editions')).toEqual([])
    expect(exactMergeCards(cards, 'stores')).toHaveLength(1)
  })
  it('groups standing relationships by their exact act and omits retracted history', () => {
    const review = mergeFixture()
    review.history = [
      { id: 1, actId: 5, parentWorkId: 1, childWorkId: 2, kind: 'same_game' },
      { id: 2, actId: 5, parentWorkId: 1, childWorkId: 3, kind: 'same_game' },
      { id: 3, actId: 6, parentWorkId: 1, childWorkId: 4, kind: 'variant_of', retractedAt: '2026-01-01' },
    ]
    const standing = buildMergeCards(review).filter((card) => card.actId)
    expect(standing).toHaveLength(1)
    expect(standing[0]).toMatchObject({ actId: 5, parent: 1, section: 'stores' })
    expect(standing[0]!.rows.map((row) => row.workId)).toEqual([1, 2, 3])
  })
  it('explains frozen title, publisher and year evidence and names indirect group members', () => {
    const review = mergeFixture()
    review.candidates[0]!.signalsJson = JSON.stringify({
      band: 'Priority',
      title_similarity: 1,
      publisher_match: true,
      year_delta: 0,
    })
    review.candidates.push({ id: 12, leftReleaseId: 102, rightReleaseId: 103, score: 0.7, status: 'pending' })
    const card = buildMergeCards(review)[0]!
    expect(card.reason).toContain('Same title apart from the edition.')
    expect(card.reason).toContain('Same publisher, same year.')
    expect(card.reason).toContain('Prey 2006 reached this group through')
  })
  it('distinguishes identical row names using shown stores, years, publishers and position as the final fallback', () => {
    const card = buildMergeCards(mergeFixture())[0]!
    expect(mergeMemberLabels(card)).toEqual(['Bastion (Steam)', 'Bastion (GOG)'])
    const rows = card.rows.map((row) => ({ ...row, stores: ['steam'], year: 2011, publisher: 'Publisher' }))
    expect(mergeMemberLabels({ ...card, rows })).toEqual([
      'Bastion (Steam, 2011, Publisher, 1 of 2)',
      'Bastion (Steam, 2011, Publisher, 2 of 2)',
    ])
    rows[1]!.year = 2017
    expect(mergeMemberLabels({ ...card, rows })).toEqual(['Bastion (Steam, 2011)', 'Bastion (Steam, 2017)'])
  })
  it('accumulates consecutive dismissals in a seven-second undo run but starts fresh for merges or expired runs', () => {
    const first = extendMergeUndo(
      null,
      { kind: 'dismiss', actIds: [], candidateIds: [10], refusedPairs: [], revision: 'r2', count: 1 },
      100,
    )
    const second = extendMergeUndo(
      first,
      { kind: 'dismiss', actIds: [], candidateIds: [11], refusedPairs: [], revision: 'r3', count: 1 },
      200,
    )
    expect(second).toMatchObject({ candidateIds: [10, 11], revision: 'r3', count: 2, expiresAt: 7200 })
    expect(
      extendMergeUndo(
        second,
        { kind: 'merge', actIds: [7], candidateIds: [], refusedPairs: [], revision: 'r4', count: 1 },
        300,
      ),
    ).toMatchObject({ actIds: [7], candidateIds: [], count: 1 })
    expect(
      extendMergeUndo(
        first,
        { kind: 'dismiss', actIds: [], candidateIds: [11], refusedPairs: [], revision: 'r4', count: 1 },
        7200,
      ),
    ).toMatchObject({ candidateIds: [11], count: 1 })
  })
})
