import { readFile } from 'node:fs/promises'

// Only library presentation inputs are deterministic; production preferences,
// security, input routing and the renderer run against the isolated backend.
const fixture = JSON.parse(await readFile(process.env.WINNOW_FILTER_FIXTURE, 'utf8'))
const original = globalThis.fetch
globalThis.fetch = (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname === '127.0.0.1' && (!init?.method || init.method === 'GET')) {
    const key = {
      '/api/v1/library': 'library',
      '/api/v1/library/workspace': 'workspace',
      '/api/v1/feed': 'feed',
    }[url.pathname]
    if (key) return Promise.resolve(Response.json(fixture[key]))
    if (url.pathname === '/api/v1/feed/supplement')
      return Promise.resolve(Response.json({ shelves: [], candidateCount: 0 }))
  }
  return original(input, init)
}
await import('../../out/main/index.js')
