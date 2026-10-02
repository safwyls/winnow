// Only the original fake-repository chart fixture is substituted. Native summary tests
// clear this value and use captured facts in their isolated production SQLite backend.
globalThis.__accountFixture = null
globalThis.__accountReads = 0
const original = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (
    url.hostname === '127.0.0.1' &&
    url.pathname === '/api/v1/statistics/accounts/steam' &&
    (!init?.method || init.method === 'GET')
  ) {
    globalThis.__accountReads++
    if (globalThis.__accountFixture) return Response.json(globalThis.__accountFixture)
  }
  return original(input, init)
}
await import('../../out/main/index.js')
