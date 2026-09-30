// Test-owned long text travels through the same API transport as real library records.
const originalFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (
    url.hostname === '127.0.0.1' &&
    init?.method === 'POST' &&
    /^\/api\/v1\/games\/\d+\/refetch$/.test(url.pathname) &&
    globalThis.__detailsHoldRefetch
  ) {
    return new Promise((resolve) => {
      globalThis.__detailsFinishRefetch = (outcome) => {
        globalThis.__detailsFinishRefetch = undefined
        resolve(Response.json({ outcome }))
      }
    })
  }
  if (
    url.hostname === '127.0.0.1' &&
    (!init?.method || init.method === 'GET') &&
    globalThis.__detailsHero &&
    /^\/api\/v1\/works\/\d+\/backdrop$/.test(url.pathname)
  )
    return Response.json({
      candidates: [
        { key: { provider: 'igdb-shot', id: 'fixture_hero' }, aspectRatio: 16 / 9, fitWholeHero: false },
      ],
      coverKey: null,
    })
  const response = await originalFetch(input, init)
  if (url.hostname !== '127.0.0.1' || (init?.method && init.method !== 'GET') || !response.ok) return response
  if (globalThis.__detailsHero && /^\/api\/v1\/works\/\d+\/artwork\/Hero$/.test(url.pathname))
    return Response.json({
      revision: 'details-fixture-hero',
      current: { previewKey: { provider: 'igdb-shot', id: 'fixture_hero' } },
    })
  if (url.pathname === '/api/v1/library') {
    const body = await response.json()
    return Response.json({
      ...body,
      games: body.games.map((game) => ({
        ...game,
        title: `${game.title}: The Definitive Collector's Edition — A Journey Beyond the Forgotten Kingdom`,
        summary: globalThis.__detailsPlaceholder
          ? null
          : 'Explore a changing world, meet its people, and find your own way through a forgotten kingdom. '.repeat(
              60,
            ),
        publisher: 'A Studio with a Long but Readable Name',
      })),
    })
  }
  return response
}
// Reuse deterministic source-sized images; this harness then loads the production main.
await import('./gallery-main.mjs')
