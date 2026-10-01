// Only OS dispatch is intercepted. The tracker and large-history flows use the real backend.
await import('./identity-projections-main.mjs')

const measurements = (globalThis.__activityNative = { phase: '', requests: [] })
const fetchBackend = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  const phase = measurements.phase
  const requests = measurements.requests
  const start = performance.now()
  const response = await fetchBackend(input, init)
  if (phase && url.hostname === '127.0.0.1' && url.pathname.startsWith('/api/'))
    requests.push({
      phase,
      path: url.pathname,
      method: init?.method ?? 'GET',
      // This measures response headers, without cloning or delaying the response body.
      responseHeadersMs: performance.now() - start,
      status: response.status,
      ...(url.pathname.endsWith('/activity/query') ? { query: JSON.parse(init?.body ?? '{}') } : {}),
    })
  return response
}
