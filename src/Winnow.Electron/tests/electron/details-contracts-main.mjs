const state = {
  revision: 'Before',
  longTitle: false,
  child: false,
  games: [],
  writes: [],
  originalWorkId: null,
}
globalThis.detailsContractsFixture = state
const originalFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname !== '127.0.0.1') return originalFetch(input, init)
  if (url.pathname === '/api/v1/journal/preferences') return Response.json({ promptAfterPlay: true })
  const journal = /^\/api\/v1\/sessions\/(\d+)\/journal$/.exec(url.pathname)
  if (journal) {
    if (init?.method === 'PUT') state.writes.push({ path: url.pathname, body: JSON.parse(init.body) })
    return Response.json({
      sessionId: Number(journal[1]),
      note: journal[1] === '781' ? 'Remember this' : null,
      rating: null,
      revision: 'one',
    })
  }
  if (/^\/api\/v1\/sessions\/\d+\/prompt$/.test(url.pathname))
    return Response.json({
      sessionId: 888,
      ownershipId: state.games[0].entries[0].ownershipId,
      durationSeconds: 600,
    })
  if (init?.method === 'DELETE' && /^\/api\/v1\/identity\/links\/\d+$/.test(url.pathname)) {
    state.writes.push({ path: url.pathname, body: JSON.parse(init.body) })
    state.child = false
    return Response.json({})
  }
  // The synthetic child identity retains the seed game's real artwork/read endpoints.
  const target = new URL(url)
  if (state.child && state.originalWorkId && (!init?.method || init.method === 'GET'))
    target.pathname = target.pathname.replace(/\/(games|works)\/99(?=\/)/, `/$1/${state.originalWorkId}`)
  const response = await originalFetch(target, init)
  if (init?.method && init.method !== 'GET') return response
  if (url.pathname === '/api/v1/library') {
    const body = await response.json()
    state.originalWorkId = body.games[0]?.workId
    state.games = (state.child ? body.games.slice(0, 1) : body.games).map((game, index) => {
      const workId = state.child && index === 0 ? 99 : game.workId
      const title = state.longTitle
        ? 'A very long game title '.repeat(10).trim()
        : state.child && index === 0
          ? 'Expansion'
          : `${state.revision} ${game.title}`
      return {
        ...game,
        workId,
        title,
        playtimeMinutes: 60,
        lastPlayedAt: '2026-08-27T19:00:00Z',
        entries: [
          {
            ...game.entries[0],
            workId,
            title,
            store: 'steam',
            installed: true,
            playtimeMinutes: 60,
            lastPlayedAt: '2026-08-27T19:00:00Z',
          },
        ],
      }
    })
    return Response.json({ ...body, games: state.games })
  }
  if (url.pathname === '/api/v1/library/workspace') {
    const body = await response.json()
    return Response.json({
      ...body,
      works: [
        ...body.works.filter((work) => ![10, 99].includes(work.id)),
        { id: 10, name: 'Base game' },
        { id: 99, name: 'Expansion' },
      ],
      identityLinks: state.child
        ? [{ id: 271, childWorkId: 99, parentWorkId: 10, kind: 'expansion_of', retractedAt: null }]
        : [],
      externalIds: [
        ...body.externalIds.filter((id) => id.provider !== 'steam'),
        ...body.releases.map((release) => ({ releaseId: release.id, provider: 'steam', providerId: '10' })),
      ],
    })
  }
  const detail = /^\/api\/v1\/games\/(\d+)\/details$/.exec(url.pathname)
  if (detail) {
    const game = state.games.find((game) => game.workId === Number(detail[1]))
    if (!game) return response
    const entry = game.entries[0],
      body = response.ok ? await response.json() : {}
    return Response.json({
      ...body,
      workId: game.workId,
      readAtUtc: '2026-08-27T20:00:00Z',
      events: [],
      ratings: [],
      history: {},
      sessions: {
        [entry.ownershipId]: [
          {
            id: 781,
            ownershipId: entry.ownershipId,
            startedAt: '2026-08-27T18:00:00Z',
            endedAt: '2026-08-27T19:00:00Z',
            durationSeconds: 3600,
            detectionMethod: 'process',
          },
        ],
      },
      journalEntries: [
        {
          sessionId: 781,
          ownershipId: entry.ownershipId,
          sessionAt: '2026-08-27T19:00:00Z',
          note: 'Remember this',
          rating: 4,
        },
      ],
      achievements: [
        { releaseId: entry.releaseId, total: 20, unlocked: 5, hasKnownProgress: true, isStale: false },
      ],
    })
  }
  return response
}
await import('./gallery-main.mjs')
