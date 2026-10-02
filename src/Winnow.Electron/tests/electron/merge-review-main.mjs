// Only the source test's asynchronous refresh boundary is controlled. Successful requests,
// review snapshots, mutations and SSE still traverse the production main/preload/API path.
const state = (globalThis.__mergeReviewNative = { holdRefresh: false, refreshCalls: 0, release: null })
const realFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname === '127.0.0.1' && url.pathname === '/api/v1/identity/review/refresh') {
    state.refreshCalls++
    if (state.holdRefresh) {
      const outcome = await new Promise((resolve, reject) => {
        state.release = resolve
        init?.signal?.addEventListener('abort', () => reject(init.signal.reason), { once: true })
      })
      state.release = null
      if (outcome === 'fail') return Response.json({ message: 'Fixture refresh failure' }, { status: 503 })
    }
  }
  return realFetch(input, init)
}
// The shared guard intercepts and records every OS dispatch; it never substitutes library data.
await import('./identity-projections-main.mjs')
