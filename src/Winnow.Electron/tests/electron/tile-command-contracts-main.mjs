// Intercept every launcher dispatch before production transport can reach the backend.
const state = (globalThis.__tileCommands = { records: [], hold: false, held: false, release: null })
const backendFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname === '127.0.0.1' && /^\/api\/v1\/entries\/\d+\/actions$/.test(url.pathname)) {
    const body = JSON.parse(init?.body ?? (input instanceof Request ? await input.clone().text() : '{}'))
    state.records.push({ ownershipId: Number(url.pathname.split('/')[4]), ...body })
    if (state.hold) {
      state.hold = false
      state.held = true
      await new Promise((resolve) => {
        state.release = resolve
      })
      state.held = false
    }
    return Response.json(0)
  }
  return backendFetch(input, init)
}
await import('../../out/main/index.js')
