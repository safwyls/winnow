const originalFetch = globalThis.fetch
const fixture = (globalThis.__journalPromptFixture = {
  ownershipId: null,
  title: '',
  enabled: false,
  preferenceReads: [],
  calls: [],
  fail: true,
  hold: false,
  release: null,
  notes: {},
})
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname !== '127.0.0.1') return originalFetch(input, init)
  if (url.pathname === '/api/v1/journal/preferences') {
    fixture.preferenceReads.push(fixture.enabled)
    return Response.json({ promptAfterPlay: fixture.enabled })
  }
  const prompt = url.pathname.match(/^\/api\/v1\/sessions\/(\d+)\/prompt$/)
  if (prompt)
    return Response.json({
      sessionId: Number(prompt[1]),
      ownershipId: fixture.ownershipId,
      durationSeconds: 2820,
    })
  const journal = url.pathname.match(/^\/api\/v1\/sessions\/(\d+)\/journal$/)
  if (journal) {
    const id = Number(journal[1])
    if (init?.method === 'PUT') {
      const body = JSON.parse(init.body)
      fixture.calls.push({ id, body })
      if (fixture.fail) return Response.json({ detail: 'Could not save your note.' }, { status: 503 })
      if (fixture.hold)
        await new Promise((resolve) => {
          fixture.release = resolve
        })
      fixture.notes[id] = {
        sessionId: id,
        note: body.note,
        rating: body.rating,
        revision: `saved-${fixture.calls.length}`,
      }
    }
    return Response.json(
      fixture.notes[id] ?? { sessionId: id, note: null, rating: null, revision: 'original' },
    )
  }
  const response = await originalFetch(input, init)
  if (url.pathname === '/api/v1/library' && response.ok) {
    const data = await response.clone().json()
    fixture.ownershipId = data.games[0]?.entries[0]?.ownershipId
    fixture.title = data.games[0]?.title
  }
  return response
}
await import('../../out/main/index.js')
