// Install before production constructs ApiTransport and captures fetch.
const originalFetch = globalThis.fetch
globalThis.overlayLaunches = 0
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname !== '127.0.0.1') return originalFetch(input, init)
  // Always stop launcher dispatch at the HTTP boundary, including a leaking shortcut.
  if (/^\/api\/v1\/entries\/\d+\/actions$/.test(url.pathname)) {
    globalThis.overlayLaunches++
    return Response.json(0)
  }
  const response = await originalFetch(input, init)
  if (!response.ok || (init?.method && init.method !== 'GET')) return response
  if (url.pathname === '/api/v1/library') {
    const body = await response.json()
    return Response.json({
      ...body,
      games: body.games.map((game) => ({
        ...game,
        entries: game.entries.map((entry) => ({ ...entry, store: 'steam', installed: true })),
      })),
    })
  }
  if (url.pathname === '/api/v1/library/workspace') {
    const body = await response.json()
    // Give every fixture release a reachable primary action. Dispatch remains intercepted.
    return Response.json({
      ...body,
      externalIds: [
        ...body.externalIds.filter((id) => id.provider !== 'steam'),
        ...body.releases.map((release) => ({ releaseId: release.id, provider: 'steam', providerId: '1' })),
      ],
    })
  }
  return response
}
await import('./details-main.mjs')
