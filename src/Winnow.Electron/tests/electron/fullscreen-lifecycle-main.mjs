const originalFetch = globalThis.fetch
const waiting = []
const state = {
  hold: process.env.WINNOW_LIFECYCLE_HOLD === '1',
  activeFeeds: 0,
  release() {
    state.hold = false
    waiting.splice(0).forEach((done) => done())
  },
}
globalThis.__fullscreenLifecycle = state
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  const primary =
    url.hostname === '127.0.0.1' &&
    (!init?.method || init.method === 'GET') &&
    ['/api/v1/feed', '/api/v1/library', '/api/v1/library/workspace'].includes(url.pathname)
  if (!primary) return originalFetch(input, init)
  if (url.pathname === '/api/v1/feed') state.activeFeeds++
  try {
    if (state.hold) await new Promise((done) => waiting.push(done))
    return await originalFetch(input, init)
  } finally {
    if (url.pathname === '/api/v1/feed') state.activeFeeds--
  }
}
globalThis.__detailsHero = true
await import('./details-main.mjs')
