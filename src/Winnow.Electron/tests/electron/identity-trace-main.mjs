// Capture real identity requests before ApiTransport stores its fetch implementation.
const originalFetch = globalThis.fetch
globalThis.__identityTrace = []
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname !== '127.0.0.1' || !url.pathname.startsWith('/api/v1/identity'))
    return originalFetch(input, init)
  const entry = { path: url.pathname, method: init?.method ?? 'GET' }
  if (typeof init?.body === 'string') entry.expected = JSON.parse(init.body).expectedRevision
  globalThis.__identityTrace.push(entry)
  const response = await originalFetch(input, init)
  entry.status = response.status
  void response
    .clone()
    .json()
    .then((body) => {
      entry.revision = body.revision
      entry.message = body.detail ?? body.message
    })
    .catch(() => {})
  return response
}
await import('../../out/main/index.js')
