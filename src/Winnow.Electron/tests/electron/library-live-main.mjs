import { app } from 'electron'

// Independent renderer profiles share only the explicitly supplied fixture backend.
const setPath = app.setPath.bind(app)
app.setPath = (name, path) =>
  setPath(
    name,
    ['userData', 'sessionData'].includes(name)
      ? `${path}-${process.env.WINNOW_FIXTURE_CLIENT ?? 'one'}`
      : path,
  )

const state = (globalThis.__libraryFixture = {
  hold: false,
  captured: false,
  released: false,
  release: null,
  libraries: [],
  writes: [],
})
const originalFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  const response = await originalFetch(input, init)
  if (url.hostname !== '127.0.0.1') return response
  if (url.pathname === '/api/v1/library' && response.ok) {
    const snapshot = await response.clone().json()
    state.libraries.push(snapshot)
    if (state.hold) {
      state.hold = false
      state.captured = true
      // Keep the already captured body even when its caller is cancelled.
      const body = JSON.stringify(snapshot)
      await new Promise((resolve) => (state.release = resolve))
      state.released = true
      return new Response(body, { headers: { 'Content-Type': 'application/json' } })
    }
  }
  if (init?.method === 'PUT' && /\/metadata$/.test(url.pathname))
    state.writes.push({ body: JSON.parse(init.body), status: response.status })
  return response
}
await import('../../out/main/index.js')
