const state = (globalThis.__listTransactions = {
  holdAppend: false,
  appendHeld: false,
  releaseAppend: null,
  feedFixture: false,
  writes: [],
  libraryReads: 0,
})
const fetchBackend = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname !== '127.0.0.1') return fetchBackend(input, init)
  if (state.feedFixture && url.pathname === '/api/v1/feed')
    return Response.json({
      confidence: 1,
      failed: false,
      candidateCount: 1,
      shelves: [
        {
          id: 'ready',
          title: 'Ready',
          blurb: 'Ready to play',
          supportsFeedback: false,
          reserve: [],
          items: [{ ownershipId: 1, releaseId: 1, title: 'Game 1', reason: 'Ready to play' }],
        },
      ],
    })
  if (state.feedFixture && url.pathname === '/api/v1/feed/supplement')
    return Response.json({ shelves: [], candidateCount: 0 })
  if (url.pathname === '/api/v1/library') state.libraryReads++
  if (state.holdAppend && init?.method === 'POST' && /\/lists\/\d+\/members$/.test(url.pathname)) {
    state.holdAppend = false
    state.appendHeld = true
    await new Promise((resolve) => {
      state.releaseAppend = resolve
    })
  }
  const response = await fetchBackend(input, init)
  if (url.pathname.startsWith('/api/v1/lists') && init?.method !== 'GET')
    state.writes.push({
      path: url.pathname,
      method: init?.method,
      body: init?.body ? JSON.parse(init.body) : null,
      status: response.status,
    })
  return response
}
await import('../../out/main/index.js')
