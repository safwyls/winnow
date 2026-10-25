// Only response data is substituted. The production main, preload, routes and renderer remain intact.
const state = (globalThis.__lazyPanes = { requests: [], detailWorkIds: [], review: null })
const originalFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname !== '127.0.0.1' || (init?.method && init.method !== 'GET'))
    return originalFetch(input, init)
  state.requests.push(url.pathname)
  if (url.pathname === '/api/v1/identity/review/' && state.review) return Response.json(state.review)
  const details = /^\/api\/v1\/games\/(\d+)\/details$/.exec(url.pathname)
  if (details) {
    state.detailWorkIds.push(Number(details[1]))
    const response = await originalFetch(input, init)
    if (!response.ok) return response
    const value = await response.json()
    return Response.json({
      ...value,
      journalEntries: [
        {
          sessionId: 900001,
          sessionAt: '2026-09-01T12:00:00Z',
          note: `Journal for selected work ${details[1]}.`,
        },
      ],
    })
  }
  return originalFetch(input, init)
}
await import('../../out/main/index.js')
