import { shell } from 'electron'

// The real bridge and renderer run against recorded action responses; no fixture can launch a game.
const state = { actions: [], outcome: 0, forbidden: [], copies: [], firstInstalled: true }
globalThis.launchFeedbackFixture = state
shell.openExternal = async (address) => {
  state.forbidden.push(address)
  throw Error('Unexpected OS handoff')
}
const originalFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const address = new URL(input instanceof Request ? input.url : input)
  if (address.hostname !== '127.0.0.1') return originalFetch(input, init)
  const action = /^\/api\/v1\/entries\/(\d+)\/actions$/.exec(address.pathname)
  if (action) {
    state.actions.push({ ownershipId: Number(action[1]), ...JSON.parse(init.body) })
    return Response.json(state.outcome)
  }
  const response = await originalFetch(input, init)
  if (!response.ok || (init?.method && init.method !== 'GET')) return response
  if (address.pathname === '/api/v1/library') {
    const body = await response.json()
    const games = body.games.map((game) => ({
      ...game,
      summary:
        'Explore a changing world, meet its people, and find your own way through a forgotten kingdom. '.repeat(
          30,
        ),
      entries: [0, 1].map((index) => ({
        ...game.entries[0],
        ownershipId: game.entries[0].ownershipId + index * 1000000,
        releaseId: game.entries[0].releaseId + index * 1000000,
        title: `Copy ${index + 1}`,
        store: 'steam',
        installed: index === 1 || state.firstInstalled,
      })),
    }))
    state.copies = games.map((game) => ({
      title: game.title,
      ids: game.entries.map((entry) => entry.ownershipId),
    }))
    return Response.json({ ...body, games })
  }
  if (address.pathname === '/api/v1/library/workspace') {
    const body = await response.json()
    return Response.json({
      ...body,
      externalIds: [
        ...body.externalIds.filter((id) => id.provider !== 'steam'),
        ...body.releases.flatMap((release) =>
          [0, 1].map((index) => ({
            releaseId: release.id + index * 1000000,
            provider: 'steam',
            providerId: String((index + 1) * 10),
          })),
        ),
      ],
    })
  }
  return response
}
await import('./gallery-main.mjs')
