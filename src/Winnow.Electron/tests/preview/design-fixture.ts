import type { ApiRequest, ApiResult, WinnowBridge } from '../../src/shared/bridge'
import { DEFAULT_PROFILE, parseThemeProfile } from '../../src/shared/theme'
import type { FeedSnapshot, GameDetails, LibraryGame, Mode, Workspace } from '../../src/renderer/api/types'

// PreviewLibrary/PreviewServices records at cf45d9f, expressed at the frontend API
// boundary. Dates stay relative; no database, provider, host or filesystem is used.
const works = [
  [
    1,
    'Hollow Knight',
    2017,
    'Team Cherry',
    'A hand-drawn action adventure through a vast ruined kingdom of insects beneath a fading town.',
  ],
  [
    2,
    'Disco Elysium',
    2019,
    'ZA/UM',
    'A role-playing game about a detective with a unique skill system at his disposal.',
  ],
  [
    3,
    'The Witcher 3: Wild Hunt',
    2015,
    'CD Projekt',
    'A story-driven open world set in a visually stunning fantasy universe.',
  ],
  [30, 'The Witcher 3: Wild Hunt', 2015, 'CD Projekt', ''],
  [
    4,
    'Stardew Valley',
    2016,
    'ConcernedApe',
    "An open-ended country-life RPG. Restore your grandfather's farm, grow crops, raise animals and befriend the valley.",
  ],
  [
    5,
    'Celeste',
    2018,
    'Maddy Makes Games',
    'Help Madeline survive her inner demons on her journey to the top of Celeste Mountain.',
  ],
  [6, "Baldur's Gate 3", 2023, 'Larian Studios', 'A party-based RPG set in the Dungeons & Dragons universe.'],
  [
    7,
    'Slay the Spire',
    2019,
    'Mega Crit',
    'A deck-building roguelike. Craft a unique deck, encounter bizarre creatures, discover relics.',
  ],
  [
    8,
    'Portal 2',
    2011,
    'Valve',
    'The sequel to the acclaimed puzzle game, with an extended single-player story and a co-operative campaign.',
  ],
] as const
// ownership, release, raw work, resolved work, store, minutes, last played days,
// update days, unread count, installed, acquired days, licence, price, store id.
const rows = [
  [201, 101, 1, 1, 'steam', 1540, 40, 10, 1, true, 700, 'purchase', 1499, '367520'],
  [202, 102, 2, 2, 'steam', 0, null, null, 0, false, 400, 'purchase', 3999, '632470'],
  [203, 103, 3, 3, 'steam', 7200, 60, null, 0, true, 1200, 'purchase', 999, '292030'],
  [230, 130, 30, 3, 'gog', 1000, 365, null, 0, false, 1100, 'purchase', null, '1207664643'],
  [204, 104, 4, 4, 'gog', 340, 270, 60, 1, true, 900, 'purchase', 1399, '1453375253'],
  [205, 105, 5, 5, 'epic', 45, 20, null, 0, false, 300, 'giveaway', null, 'celeste-preview'],
  [206, 106, 6, 6, 'steam', 90, 5, null, 0, true, 60, 'purchase', 5999, '1086940'],
  [207, 107, 7, 7, 'steam', 0, null, null, 0, false, 1500, 'bundle', null, '646570'],
  [208, 108, 8, 8, 'steam', 6500, 730, null, 0, true, 2000, 'purchase', null, '620'],
] as const
const buckets: Record<number, string> = {
  1: 'bounced',
  2: 'never_played',
  3: 'retired',
  4: 'stale_but_patched',
  5: 'active',
  6: 'active',
  7: 'never_played',
  8: 'retired',
}
export const DESIGN_PATCH_NOTES =
  'Patch 1.6.15 — Fixed several multiplayer desyncs, corrected item pricing at the festival stalls and added the meadowlands farm layout.'

export function createDesignFixture({
  now = new Date(),
  mode = 'desktop',
}: { now?: Date; mode?: Mode } = {}) {
  const ago = (days: number) => new Date(now.getTime() - days * 86_400_000).toISOString()
  const names = new Map(works.map((work) => [Number(work[0]), work[1]]))
  const ownerships = rows.map(
    ([id, releaseId, workId, , store, , , , , installed, days, licenseType, pricePaidCents]) => ({
      id,
      releaseId,
      store,
      installed,
      acquiredAt: ago(days),
      licenseType,
      pricePaidCents,
      installPath: installed
        ? `C:\\Games\\${store === 'gog' ? 'GOG' : 'Steam\\steamapps\\common'}\\${id === 203 ? 'The Witcher 3' : id === 206 ? 'Baldurs Gate 3' : names.get(workId)}`
        : null,
    }),
  )
  const releases = rows.map(([, id, workId]) => ({
    id,
    workId,
    name: names.get(workId)!,
    platform: 'windows',
  }))
  const games: LibraryGame[] = works
    .filter((work) => work[0] !== 30)
    .map(([workId, title, firstReleaseYear, publisher, summary]) => {
      const members = rows.filter((row) => row[3] === workId)
      const played = members.flatMap((row) => (row[6] === null ? [] : [row[6]]))
      return {
        workId,
        title,
        firstReleaseYear,
        publisher,
        summary,
        bucket: buckets[workId],
        playtimeMinutes: members.reduce((total, row) => total + row[5], 0),
        lastPlayedAt: played.length ? ago(Math.min(...played)) : null,
        entries: members.map(
          ([ownershipId, releaseId, rawWorkId, , store, playtimeMinutes, days, , , ,]) => ({
            ownershipId,
            releaseId,
            workId: rawWorkId,
            title: names.get(rawWorkId)!,
            store,
            installed: ownerships.find((row) => row.id === ownershipId)!.installed,
            playtimeMinutes,
            lastPlayedAt: days === null ? null : ago(days),
          }),
        ),
      }
    })
  const events = [
    {
      id: 1,
      releaseId: 101,
      kind: 'announcement',
      occurredAt: ago(10),
      title: 'Silksong is out now',
      url: 'https://store.steampowered.com/news/app/367520',
    },
    { id: 2, releaseId: 101, kind: 'build_push', occurredAt: ago(12), buildId: '15284321' },
    {
      id: 3,
      releaseId: 104,
      kind: 'announcement',
      occurredAt: ago(60),
      title: 'Patch 1.6.15 — patch notes',
      url: 'https://www.gog.com/game/stardew_valley',
    },
    { id: 4, releaseId: 104, kind: 'build_push', occurredAt: ago(62), buildId: 'gog-2.4.0.15' },
  ]
  const workspace = {
    preferences: { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'all' },
    works: works.map(([id, name, firstReleaseYear, publisher, summary]) => ({
      id,
      name,
      firstReleaseYear,
      publisher,
      summary,
    })),
    releases,
    ownerships,
    externalIds: rows.map(([, releaseId, , , provider, , , , , , , , , providerId]) => ({
      releaseId,
      provider,
      providerId,
    })),
    pluginActions: {},
    epicLaunchKeys: {},
    facets: [],
    releaseFacets: [],
    identityLinks: [{ parentWorkId: 3, childWorkId: 30, kind: 'same_game' }],
    storefronts: {
      'gog:1453375253': {
        storeUrl: 'https://www.gog.com/game/stardew_valley',
        patchNotes: DESIGN_PATCH_NOTES,
      },
    },
    buckets: rows.map(
      ([
        ownershipId,
        releaseId,
        workId,
        resolvedWorkId,
        ,
        playtimeMinutes,
        days,
        updated,
        unreadUpdateCount,
      ]) => ({
        ownershipId,
        releaseId,
        workId,
        resolvedWorkId,
        playtimeMinutes,
        lastPlayedAt: days === null ? null : ago(days),
        majorUpdateAt: updated === null ? null : ago(updated),
        bucket: ownershipId === 230 ? 'bounced' : buckets[resolvedWorkId],
        game: { ...games.find((game) => game.workId === resolvedWorkId), unreadUpdateCount },
      }),
    ),
  } satisfies Workspace
  const details = (workId: number): GameDetails => {
    const game = games.find((game) => game.workId === workId)!
    const ids = new Set(game.entries.map((entry) => entry.releaseId))
    return {
      workId,
      readAtUtc: now.toISOString(),
      events: events.filter((row) => ids.has(row.releaseId)),
      sessions: {},
      journalEntries: [],
      ratings: [],
      achievements: [],
      ownerships: ownerships.filter((row) => ids.has(row.releaseId)),
      acknowledgements: {},
      images: [],
      history:
        workId === 4
          ? {
              204: [
                { id: 1, ownershipId: 204, playtimeMinutes: 0, observedAt: ago(400) },
                { id: 2, ownershipId: 204, playtimeMinutes: 120, observedAt: ago(330) },
                { id: 3, ownershipId: 204, playtimeMinutes: 340, observedAt: ago(270) },
              ],
            }
          : {},
    }
  }
  const feed: FeedSnapshot = {
    candidateCount: 8,
    confidence: 1,
    failed: false,
    shelves: [
      {
        id: 'patched',
        title: 'Patched while you were away',
        blurb: 'Something changed since the last time either of you looked.',
        supportsFeedback: false,
        reserve: [],
        items: [
          {
            ownershipId: 204,
            releaseId: 104,
            title: 'Stardew Valley',
            reason: 'Patched 2 months ago, nine months after you last played',
          },
          { ownershipId: 201, releaseId: 101, title: 'Hollow Knight', reason: 'Silksong is out now' },
        ],
      },
      {
        id: 'untouched',
        title: 'Never opened',
        blurb: 'Owned, installed or otherwise, and never once launched.',
        supportsFeedback: false,
        reserve: [],
        items: [
          {
            ownershipId: 202,
            releaseId: 102,
            title: 'Disco Elysium',
            reason: 'In the library for over a year, never played',
          },
          {
            ownershipId: 207,
            releaseId: 107,
            title: 'Slay the Spire',
            reason: 'Came with a bundle, never opened',
          },
        ],
      },
    ],
  }
  const emptySlice = { count: 0, cents: 0 }
  const account = {
    source: 'steam',
    hasAnything: true,
    isSingleCurrency: true,
    currencySymbol: '$',
    currencyGroups: [],
    knownAccountCount: 0,
    unknownAccountFactCount: 0,
    transactionCount: 214,
    licenseCount: 312,
    transactionsWithoutCurrency: 0,
    transactionsWithoutDate: 0,
    licensesWithoutDate: 0,
    grossProductSpendCents: 412300,
    refundedProductSpendCents: 6499,
    netProductSpendCents: 405801,
    grossProductTransactionCount: 208,
    refundedProductTransactionCount: 2,
    netProductTransactionCount: 206,
    spendByYear: [
      { year: 2023, transactionCount: 61, cents: 118400 },
      { year: 2024, transactionCount: 72, cents: 142900 },
      { year: 2025, transactionCount: 73, cents: 144501 },
    ],
    purchases: { count: 184, cents: 371200 },
    giftPurchases: { count: 6, cents: 14995 },
    inGamePurchases: { count: 18, cents: 9606 },
    bundlePurchases: emptySlice,
    refundTransactions: emptySlice,
    walletCreditPurchases: emptySlice,
    walletCreditRedemptions: emptySlice,
    discountedPurchases: emptySlice,
    discountedPurchaseListCents: 0,
    undatedNetSpendCents: 0,
    undatedNetTransactionCount: 0,
    firstTransactionAt: ago(2000),
    lastTransactionAt: ago(12),
    firstLicenseAt: ago(2000),
    lastLicenseAt: ago(12),
    currencies: [{ symbol: '$', transactionCount: 214 }],
    licenseAcquisitions: [
      { kind: 'Steam Store', count: 288 },
      { kind: 'Gift/Guest Pass', count: 14 },
      { kind: 'Complimentary', count: 10 },
    ],
    biggestPurchase: {
      cents: 9999,
      occurredAt: ago(500),
      itemNames: ["Baldur's Gate 3", 'Larian gift bundle'],
      itemCount: 2,
      currencySymbol: '$',
      isBundle: false,
    },
  }
  let profile = structuredClone(DEFAULT_PROFILE)
  // Static design previews do not start the independent loading-animation worker.
  profile.appearance.reducedMotion = true
  let fullscreen = mode === 'fullscreen'
  const fullscreenListeners = new Set<(value: boolean) => void>()
  const preferences = new Map<string, string>([
    ['DefaultSort', 'NameAscending'],
    ['GroupExpansions', 'false'],
    ['Theme', 'winnow'],
    ['FullscreenReducedMotion', 'true'],
  ])
  const requests: string[] = [],
    blocked: string[] = [],
    writes: ApiRequest[] = []
  const refuse = (action: string) => {
    blocked.push(action)
    return {
      ok: false,
      status: 403,
      message: 'This isolated preview does not perform system or library writes.',
    }
  }
  const bridge: WinnowBridge = {
    request: async <T>(input: ApiRequest): Promise<ApiResult<T>> => {
      const { route, params } = input
      requests.push(route)
      let data: unknown
      switch (route) {
        case 'library.get':
          data = { games, lists: [] }
          break
        case 'library.workspace':
          data = workspace
          break
        case 'feed.get':
          data = feed
          break
        case 'feed.supplement':
          data = { shelves: [], candidateCount: 0 }
          break
        case 'feedImpression':
          data = {}
          break
        case 'game.details':
          data = details(Number(params?.workId))
          break
        case 'preferences.library.get':
          data = workspace.preferences
          break
        case 'preferences.presentation.get':
          data = [...preferences].map(([preference, value]) => ({ preference, value }))
          break
        case 'preferences.presentation.put': {
          const body = input.body as { value: string }
          preferences.set(String(params?.preference), body.value)
          writes.push(input)
          data = {}
          break
        }
        case 'connections.get':
          data = { steam: { hasUsableCredential: false, hasSession: false, hasApiKey: false }, epic: null }
          break
        case 'connections.visibility.get':
          data = { accountConfirmed: false, ownAccountOnly: true, hiddenCount: 0 }
          break
        case 'library.visibility':
          data = { explicitHidden: 0, ratingCapHidden: 0 }
          break
        case 'diagnostics.get':
          data = { sessionFailures: [] }
          break
        case 'progress.get':
          data = { total: 0, remaining: 0 }
          break
        case 'journal.preferences.get':
          data = { promptAfterPlay: false }
          break
        case 'connections.igdb.get':
          data = { clientId: '', hasSavedCredentials: false }
          break
        case 'setup.get':
          data = { step: null }
          break
        case 'activity.query':
          data = { rows: [], next: null }
          break
        case 'statistics.account':
          data = account
          break
        case 'statistics.gameplay':
          data = {
            recordedSeconds: 113400,
            gamesPlayedCount: 6,
            overlappingSessionCount: 24,
            startedSessionCount: 23,
            medianSessionSeconds: 2700,
            excludedSessionCount: 1,
            periods: [],
            topGames: games
              .slice(0, 6)
              .map((game, index) => ({ resolvedWorkId: game.workId, recordedSeconds: (6 - index) * 5400 })),
            sessionLengths: [
              { fromSeconds: 0, untilSeconds: 1800, count: 5 },
              { fromSeconds: 1800, untilSeconds: 3600, count: 9 },
              { fromSeconds: 3600, untilSeconds: 7200, count: 6 },
              { fromSeconds: 7200, untilSeconds: null, count: 3 },
            ],
          }
          break
        case 'identity.get':
          data = {
            revision: 'preview-1',
            hasCompletedSweep: true,
            workspace,
            candidates: [],
            history: [],
            expansions: [],
            refused: [],
            acts: [],
          }
          break
        case 'metadata.get': {
          const game = games.find((row) => row.workId === Number(params?.workId))!
          data = {
            workId: game.workId,
            title: game.title,
            isPinned: false,
            revision: 'preview-1',
            fields: [
              { field: 'name', value: game.title, source: 'preview' },
              { field: 'summary', value: game.summary, source: 'preview' },
            ],
          }
          break
        }
        case 'metadata.igdb':
          data = { workId: params?.workId, revision: 'preview-1', mappingRevision: 0, pin: null }
          break
        case 'artworkState':
          data = { current: null, revision: 'preview-1' }
          break
        case 'artwork.backdrop':
          data = { candidates: [] }
          break
        case 'plugins.directory':
          data = { directory: 'Preview only' }
          break
        case 'plugins.get':
        case 'feedHistory':
        case 'operations.get':
        case 'feedFeedback.get':
        case 'hidden.get':
        case 'identity.candidates':
        case 'manual.get':
        case 'accounts.get':
          data = []
          break
        default:
          return refuse(route)
      }
      return { ok: true, status: 200, data: structuredClone(data) as T }
    },
    connection: async () => ({ connected: true, message: 'Isolated design preview' }),
    onEvent: () => () => {},
    onConnection: () => () => {},
    artwork: async () => null,
    loadPreferences: async () => structuredClone(profile),
    savePreferences: async (value) => {
      profile = parseThemeProfile(value)
    },
    listThemes: async () => [],
    listFonts: async () => ['Bricolage Grotesque', 'Plus Jakarta Sans', 'IBM Plex Mono'],
    installTheme: async () => null,
    importProfile: async () => null,
    exportProfile: async () => false,
    isFullscreen: async () => fullscreen,
    setFullscreen: async (value) => {
      fullscreen = value
      fullscreenListeners.forEach((listener) => listener(value))
    },
    onFullscreen: (callback) => {
      fullscreenListeners.add(callback)
      return () => {
        fullscreenListeners.delete(callback)
      }
    },
    openExternal: async () => {
      refuse('openExternal')
      return { opened: false, message: 'Links are disabled in this isolated preview.' }
    },
    applicationInfo: async () => ({
      version: 'preview',
      commit: 'cf45d9f',
      platform: 'win32',
      packaged: false,
      autostartSupported: false,
      openAtLogin: false,
    }),
  }
  return {
    mode,
    now,
    games,
    ownerships,
    releases,
    workspace,
    feed,
    details,
    bridge,
    requests,
    blocked,
    writes,
  }
}
export type DesignFixture = ReturnType<typeof createDesignFixture>
export function installDesignFixture(fixture: DesignFixture) {
  const previous = window.winnow
  window.winnow = fixture.bridge
  return () => {
    window.winnow = previous
  }
}
