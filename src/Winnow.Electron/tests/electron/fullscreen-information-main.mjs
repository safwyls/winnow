const originalFetch = globalThis.fetch
let game
const note =
  process.env.WINNOW_LONG_NOTE === '1'
    ? Array.from(
        { length: 24 },
        () =>
          'A journal entry about returning to a familiar game. The quieter moments made this session memorable, and there is still another path to explore.',
      ).join('\n\n')
    : 'Found a new path through the old ruins. Next time, return to the gate above the river.'
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname === '127.0.0.1' && url.pathname === '/api/v1/activity/query') {
    const query = JSON.parse(init.body)
    const now = new Date().toISOString()
    return Response.json({
      rows:
        query.section === 1
          ? []
          : [
              {
                ownershipId: game.entries[0].ownershipId,
                store: game.entries[0].store,
                atUtc: now,
                session: {
                  id: 99001,
                  ownershipId: game.entries[0].ownershipId,
                  startedAt: now,
                  durationSeconds: 5400,
                  detectionMethod: 'manual',
                },
                note: { sessionId: 99001, note, rating: 4 },
              },
            ],
      next: null,
    })
  }
  const response = await originalFetch(input, init)
  if (url.hostname === '127.0.0.1' && url.pathname === '/api/v1/library' && response.ok) {
    const body = await response.json()
    game = body.games[0]
    return Response.json(body)
  }
  return response
}
await import('../../out/main/index.js')
