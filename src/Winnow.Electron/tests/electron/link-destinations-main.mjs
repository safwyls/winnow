import { app, shell } from 'electron'

// Test-owned network and OS boundaries surround the production frontend and backend.
const state = { external: [], reading: [], launches: 0, failReader: false, forbidden: [] }
globalThis.linkDestinationsFixture = state
shell.openExternal = async (address) => {
  state.external.push(address)
}
app.getApplicationNameForProtocol = (address) => (address === 'steam://' ? 'Steam fixture' : '')
const hosts = ['www.igdb.com', 'steamdb.info', 'www.steamgriddb.com', 'store.steampowered.com', 'example.com']
app.on('session-created', (profile) => {
  for (const scheme of ['http', 'https'])
    profile.protocol.handle(scheme, (request) => {
      const address = new URL(request.url)
      if (!hosts.includes(address.hostname)) {
        state.forbidden.push(request.url)
        return new Response('Refused fixture request', { status: 403 })
      }
      state.reading.push(request.url)
      if (state.failReader) return Response.error()
      return new Response(
        '<!doctype html><html><head><title>Reference fixture</title></head><body><h1>Reference fixture</h1><p>Local test content</p></body></html>',
        { headers: { 'Content-Type': 'text/html' } },
      )
    })
})
const originalFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const address = new URL(input instanceof Request ? input.url : input)
  if (address.hostname !== '127.0.0.1') return originalFetch(input, init)
  if (/^\/api\/v1\/entries\/\d+\/actions$/.test(address.pathname)) {
    state.launches++
    return Response.json(0)
  }
  const response = await originalFetch(input, init)
  if (!response.ok || (init?.method && init.method !== 'GET')) return response
  if (address.pathname === '/api/v1/library') {
    const body = await response.json()
    return Response.json({
      ...body,
      games: body.games.map((game) => ({
        ...game,
        entries: game.entries.map((entry) => ({ ...entry, store: 'steam', installed: true })),
      })),
    })
  }
  if (address.pathname === '/api/v1/library/workspace') {
    const body = await response.json()
    return Response.json({
      ...body,
      works: body.works.map((work) => ({ ...work, igdbId: 1942 })),
      externalIds: [
        ...body.externalIds.filter((id) => id.provider !== 'steam'),
        ...body.releases.map((release) => ({ releaseId: release.id, provider: 'steam', providerId: '440' })),
      ],
    })
  }
  return response
}
await import('../../out/main/index.js')
