// Exercise rendered batch failures through the production transport without changing real data.
const originalFetch = globalThis.fetch
const at = (days) => new Date(Date.now() + days * 86400000).toISOString()
const fixture = (globalThis.__updateFlagsFixture = {
  workId: null,
  releases: [],
  events: [],
  acknowledgements: {},
  calls: [],
  fail: true,
})
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname !== '127.0.0.1') return originalFetch(input, init)
  const mutation = url.pathname.match(/^\/api\/v1\/releases\/(\d+)\/(acknowledge|restore)-updates$/)
  if (mutation) {
    const releaseId = Number(mutation[1]),
      restore = mutation[2] === 'restore'
    const body = init?.body ? JSON.parse(init.body) : undefined
    fixture.calls.push({ releaseId, restore, body })
    if (fixture.fail && releaseId === fixture.releases[1]) return Response.json({ result: 'NotStored' })
    if (restore) {
      delete fixture.acknowledgements[releaseId]
      return Response.json({ result: 'Stored' })
    }
    const through = fixture.events
      .filter(
        (event) =>
          event.releaseId === releaseId &&
          event.kind === 'build_push' &&
          body.observedEventIds.includes(event.id),
      )
      .map((event) => event.occurredAt)
      .sort()
      .at(-1)
    fixture.acknowledgements[releaseId] = through
    return Response.json({ result: 'Stored', acknowledgedThrough: through })
  }
  const response = await originalFetch(input, init)
  if (!response.ok || (init?.method && init.method !== 'GET')) return response
  if (url.pathname === '/api/v1/library') {
    const data = await response.json()
    if (!fixture.workId) {
      fixture.workId = data.games[0].workId
      fixture.releases = [data.games[0].entries[0].releaseId, 900001]
      fixture.events = fixture.releases.flatMap((releaseId, index) => [
        {
          id: 9000 + index * 2,
          releaseId,
          kind: 'build_push',
          occurredAt: at(-40 + index),
          title: `Edition ${index + 1} build`,
        },
        {
          id: 9001 + index * 2,
          releaseId,
          kind: 'announcement',
          occurredAt: at(-39 + index),
          title: `Edition ${index + 1} patch notes`,
        },
      ])
    }
    return Response.json({
      ...data,
      games: data.games.map((game) =>
        game.workId !== fixture.workId
          ? game
          : {
              ...game,
              title: 'Update Flag Fixture',
              bucket: 'stale_but_patched',
              playtimeMinutes: 600,
              lastPlayedAt: at(-90),
              entries: game.entries.map((entry) => ({
                ...entry,
                playtimeMinutes: 600,
                lastPlayedAt: at(-90),
              })),
            },
      ),
    })
  }
  if (url.pathname === `/api/v1/games/${fixture.workId}/details`) {
    const data = await response.json()
    return Response.json({ ...data, events: fixture.events, acknowledgements: fixture.acknowledgements })
  }
  return response
}
await import('../../out/main/index.js')
