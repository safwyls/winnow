import { shell } from 'electron'

// Only OS dispatch is intercepted. Identity, library, artwork and details use the real backend.
const state = (globalThis.__identityProjections = { dispatches: [], mutations: [] })
shell.openExternal = async (target) => {
  state.dispatches.push({ kind: 'external', target })
}
shell.openPath = async (target) => {
  state.dispatches.push({ kind: 'path', target })
  return ''
}
shell.showItemInFolder = (target) => {
  state.dispatches.push({ kind: 'reveal', target })
}
const backendFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname === '127.0.0.1') {
    if (/^\/api\/v1\/entries\/\d+\/actions$/.test(url.pathname)) {
      state.dispatches.push({ kind: 'entry', target: url.pathname })
      return Response.json(0)
    }
    if (url.pathname.startsWith('/api/v1/identity/') && init?.method && init.method !== 'GET')
      state.mutations.push({ path: url.pathname, method: init.method, body: JSON.parse(init.body ?? '{}') })
  }
  return backendFetch(input, init)
}
await import('../../out/main/index.js')
