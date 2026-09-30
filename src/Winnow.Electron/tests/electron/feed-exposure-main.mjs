const originalFetch = globalThis.fetch
globalThis.__feedExposures = []
globalThis.fetch = (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname === '127.0.0.1' && url.pathname === '/api/v1/feed/impressions') {
    globalThis.__feedExposures.push(JSON.parse(init.body))
    return Promise.resolve(Response.json(null))
  }
  return originalFetch(input, init)
}
await import('./fullscreen-browse-main.mjs')
