import { readFile } from 'node:fs/promises'

// A test-owned entry delegates every service except these deterministic presentation snapshots.
// Production main, preload, renderer, security and styling are loaded without test hooks.
globalThis.__winnowLayoutFixture = JSON.parse(await readFile(process.env.WINNOW_LAYOUT_FIXTURE, 'utf8'))
const originalFetch = globalThis.fetch
globalThis.fetch = (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname === '127.0.0.1' && (!init?.method || init.method === 'GET')) {
    const fixture = globalThis.__winnowLayoutFixture
    if (url.pathname === '/api/v1/library') return Promise.resolve(Response.json(fixture.library))
    if (url.pathname === '/api/v1/feed') return Promise.resolve(Response.json(fixture.feed))
    if (url.pathname === '/api/v1/feed/supplement')
      return Promise.resolve(Response.json({ shelves: [], candidateCount: 0 }))
  }
  return originalFetch(input, init)
}
await import('../../out/main/index.js')
