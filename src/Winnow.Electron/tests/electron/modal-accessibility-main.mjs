const originalFetch = globalThis.fetch
const fixture = (globalThis.__modalAccessibility = { game: null, releaseList: null })
const session = () => ({
  id: 99001,
  ownershipId: fixture.game.entries[0].ownershipId,
  startedAt: new Date().toISOString(),
  endedAt: new Date().toISOString(),
  durationSeconds: 3600,
  detectionMethod: 'manual',
})
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname !== '127.0.0.1') return originalFetch(input, init)
  if (url.pathname === '/api/v1/lists' && init?.method === 'POST') {
    await new Promise((resolve) => {
      fixture.releaseList = resolve
    })
    fixture.releaseList = null
    return Response.json({ detail: 'Could not save this list.' }, { status: 400 })
  }
  if (url.pathname === '/api/v1/activity/query')
    return Response.json({
      rows: [
        {
          ownershipId: fixture.game.entries[0].ownershipId,
          store: fixture.game.entries[0].store,
          atUtc: new Date().toISOString(),
          session: session(),
          note: null,
        },
      ],
      next: null,
    })
  if (/^\/api\/v1\/sessions\/\d+\/journal$/.test(url.pathname))
    return Response.json({ sessionId: 99001, note: null, rating: null, revision: 'empty' })
  const response = await originalFetch(input, init)
  if (!response.ok || (init?.method && init.method !== 'GET')) return response
  if (url.pathname === '/api/v1/library') {
    const body = await response.json()
    fixture.game = body.games[0]
    return Response.json({
      ...body,
      lists: [
        ...body.lists,
        { id: 99001, name: 'Existing list', revision: 'existing', releaseIds: [], isLive: false },
      ],
    })
  }
  if (/^\/api\/v1\/games\/\d+\/details$/.test(url.pathname)) {
    const body = await response.json()
    return Response.json({
      ...body,
      sessions: { [fixture.game.entries[0].ownershipId]: [session()] },
      journalEntries: [],
    })
  }
  return response
}
await import('../../out/main/index.js')
