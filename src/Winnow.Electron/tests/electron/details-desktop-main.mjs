const state = (globalThis.desktopDetailsFixture = { longTitle: false, game: null })
const originalFetch = globalThis.fetch
const summary =
  'Set out from a quiet observatory to chart a sky that changes every night. Follow old signals, rebuild your ship, and find the people who left their stories among the stars.\n\n' +
  'Each expedition offers a new route home. You can trade discoveries with other travellers, restore abandoned stations, or spend an evening exploring a single unfamiliar world. The choices you make change which stories you find.'
const played = '2023-09-08T12:00:00Z'
const lists = () =>
  Array.from({ length: 6 }, (_, index) => ({
    id: index + 1,
    name: index === 0 ? 'Return to these worlds' : `Weekend expeditions ${index + 1}`,
    description: null,
    isLive: false,
    releaseIds: index === 0 && state.game ? [state.game.entries[0].releaseId] : [],
    revision: 'one',
  }))
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname !== '127.0.0.1') return originalFetch(input, init)
  if (init?.method === 'POST' && url.pathname.includes('/actions'))
    throw Error('This geometry fixture cannot launch games')
  const response = await originalFetch(input, init)
  if (!response.ok || (init?.method && init.method !== 'GET')) return response
  if (url.pathname === '/api/v1/library') {
    const body = await response.json(),
      game = body.games[0]
    const title = state.longTitle
      ? 'The Astral Cartographers: Echoes Beyond the Forgotten Constellations — Complete Collection'
      : 'The Astral Cartographers'
    const publisher = state.longTitle
      ? 'The Independent Cartographic Society and the Interstellar Exploration Cooperative'
      : 'Northstar Studio'
    state.game = {
      ...game,
      title,
      publisher,
      summary,
      firstReleaseYear: 2019,
      bucket: 'stale_but_patched',
      playtimeMinutes: 2220,
      lastPlayedAt: played,
      entries: ['steam', 'gog', 'epic'].map((store, index) => ({
        ...body.games[index].entries[0],
        workId: game.workId,
        title,
        store,
        installed: true,
        playtimeMinutes: 740,
        lastPlayedAt: played,
      })),
    }
    return Response.json({ ...body, games: [state.game], lists: lists() })
  }
  if (url.pathname === '/api/v1/library/workspace') {
    const body = await response.json(),
      game = state.game
    if (!game) return Response.json(body)
    return Response.json({
      ...body,
      works: [
        ...body.works.filter((work) => ![game.workId, 120011, 120012, 120013].includes(work.id)),
        {
          ...body.works.find((work) => work.id === game.workId),
          name: game.title,
          summary,
          publisher: game.publisher,
          firstReleaseYear: 2019,
        },
        ...Array.from({ length: 3 }, (_, index) => ({
          id: 120011 + index,
          name: `The Astral Cartographers: Distant Shores ${index + 1}`,
        })),
      ],
      identityLinks: Array.from({ length: 3 }, (_, index) => ({
        id: 5001 + index,
        parentWorkId: game.workId,
        childWorkId: 120011 + index,
        kind: 'expansion_of',
        retractedAt: null,
      })),
      buckets: body.buckets.map((row) =>
        row.resolvedWorkId === game.workId ? { ...row, game: { ...row.game, unreadUpdateCount: 12 } } : row,
      ),
      externalIds: [
        ...body.externalIds.filter((id) => !game.entries.some((entry) => id.releaseId === entry.releaseId)),
        { releaseId: game.entries[0].releaseId, provider: 'steam', providerId: '12345' },
      ],
    })
  }
  if (/^\/api\/v1\/games\/\d+\/details$/.test(url.pathname)) {
    const body = await response.json(),
      game = state.game,
      entry = game.entries[0]
    return Response.json({
      ...body,
      ownerships: game.entries.map((entry) => ({
        id: entry.ownershipId,
        releaseId: entry.releaseId,
        store: entry.store,
        installed: true,
        installPath: 'C:\\Games\\The Astral Cartographers\\Library\\Installed games\\Complete edition',
        acquiredAt: '2021-09-08T12:00:00Z',
        licenseType: 'gift',
      })),
      events: Array.from({ length: 12 }, (_, index) => ({
        id: index + 1,
        releaseId: entry.releaseId,
        kind: 'announcement',
        occurredAt: new Date(Date.UTC(2026, 8, 8, 12) - (index + 1) * 10 * 86400000).toISOString(),
        title: `Expedition ${index + 1}: new places to discover and improvements`,
        url: `https://store.steampowered.com/news/app/12345/view/${index + 1}`,
      })),
      ratings: [
        ['igdb_users', 84, 1240],
        ['igdb_critics', 87, 43],
        ['steam', 92, 18240],
      ].map(([source, score, ratingCount]) => ({ source, score, ratingCount, hasFigure: true })),
      journalEntries: Array.from({ length: 3 }, (_, index) => ({
        sessionId: index + 1,
        ownershipId: entry.ownershipId,
        sessionAt: new Date(Date.parse(played) - (index + 1) * 86400000).toISOString(),
        rating: 4,
        note: 'Reached the old observatory. Next time, follow the signal beyond the southern ridge and bring supplies for the return journey.',
      })),
    })
  }
  return response
}
await import('./gallery-main.mjs')
globalThis.__galleryCount = 8
