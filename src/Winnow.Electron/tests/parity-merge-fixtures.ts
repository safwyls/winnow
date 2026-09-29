import type { MergeReview } from '../src/renderer/features/parity-merge-model'
export function mergeFixture(): MergeReview {
  return {
    revision: 'r1',
    hasCompletedSweep: true,
    candidates: [
      {
        id: 10,
        leftReleaseId: 101,
        rightReleaseId: 102,
        score: 0.98,
        status: 'pending',
        signalsJson: JSON.stringify({ band: 'Priority', title_similarity: 1 }),
      },
      {
        id: 11,
        leftReleaseId: 103,
        rightReleaseId: 104,
        score: 0.8,
        status: 'pending',
        signalsJson: JSON.stringify({ band: 'Priority', title_similarity: 0.92 }),
      },
    ],
    history: [],
    expansions: [],
    workspace: {
      works: [
        { id: 1, name: 'Bastion' },
        { id: 2, name: 'Bastion' },
        { id: 3, name: 'Prey 2006' },
        { id: 4, name: 'Prey 2017' },
      ],
      releases: [1, 2, 3, 4].map((id) => ({ id: id + 100, workId: id })),
      ownerships: [1, 2, 3, 4].map((id) => ({
        id,
        releaseId: id + 100,
        store: id === 2 ? 'gog' : 'steam',
        installed: id === 3,
        acquiredAt: id === 1 ? '2012-01-01T00:00:00Z' : null,
      })),
      buckets: [1, 2, 3, 4].map((id) => ({
        ownershipId: id,
        releaseId: id + 100,
        workId: id,
        resolvedWorkId: id,
        playtimeMinutes: id * 60,
        lastPlayedAt: id === 3 ? '2026-01-01T00:00:00Z' : null,
        bucket: id === 3 ? 'stale_but_patched' : 'active',
      })),
      externalIds: [],
      epicLaunchKeys: {},
      pluginActions: {},
      preferences: {} as MergeReview['workspace']['preferences'],
    },
  }
}

export function sourceSortFixture(review: MergeReview) {
  review.workspace.works = [
    { id: 1, name: 'The Witcher 3: Wild Hunt' },
    { id: 2, name: 'The Witcher 3: Wild Hunt GOTY' },
    { id: 3, name: 'Prey' },
    { id: 4, name: 'Prey' },
    { id: 5, name: 'The Stanley Parable' },
    { id: 6, name: 'The Stanley Parable' },
  ]
  review.workspace.releases = review.workspace.works.map((work) => ({ id: 100 + work.id, workId: work.id }))
  review.workspace.ownerships = review.workspace.works.map((work) => ({
    id: work.id,
    releaseId: 100 + work.id,
    store: 'steam',
  }))
  review.workspace.buckets = review.workspace.works.map((work) => ({
    ownershipId: work.id,
    releaseId: 100 + work.id,
    workId: work.id,
    playtimeMinutes: work.id === 3 ? 600 : work.id <= 2 ? 30 : 0,
    bucket: 'active',
  }))
  review.candidates = [
    {
      id: 11,
      leftReleaseId: 103,
      rightReleaseId: 104,
      score: 0.65,
      status: 'pending',
      signalsJson: JSON.stringify({ band: 'Review', title_similarity: 1 }),
    },
    {
      id: 10,
      leftReleaseId: 101,
      rightReleaseId: 102,
      score: 0.99,
      status: 'pending',
      signalsJson: JSON.stringify({ band: 'Priority', title_similarity: 1 }),
    },
    {
      id: 12,
      leftReleaseId: 105,
      rightReleaseId: 106,
      score: 0.98,
      status: 'pending',
      signalsJson: JSON.stringify({ band: 'Priority', title_similarity: 1 }),
    },
  ]
}
