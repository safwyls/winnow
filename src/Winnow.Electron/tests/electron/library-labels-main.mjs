// Merge review has a fixed source fixture; other requests use the real isolated backend.
globalThis.__libraryLabelsReview = null
const originalFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (
    url.hostname === '127.0.0.1' &&
    url.pathname === '/api/v1/identity/review/' &&
    (!init?.method || init.method === 'GET') &&
    globalThis.__libraryLabelsReview
  )
    return Response.json(globalThis.__libraryLabelsReview)
  return originalFetch(input, init)
}
await import('../../out/main/index.js')
