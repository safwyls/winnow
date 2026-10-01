import { shell } from 'electron'

const state = (globalThis.__pluginGameActions = { shellAttempts: [], actions: [] })
shell.openExternal = async (url) => {
  state.shellAttempts.push({ kind: 'external', target: url })
}
shell.openPath = async (path) => {
  state.shellAttempts.push({ kind: 'path', target: path })
  return ''
}
shell.showItemInFolder = (path) => {
  state.shellAttempts.push({ kind: 'reveal', target: path })
}

const backendFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  const response = await backendFetch(input, init)
  if (url.hostname === '127.0.0.1' && /^\/api\/v1\/entries\/\d+\/actions$/.test(url.pathname))
    state.actions.push({
      path: url.pathname,
      body: init?.body ? JSON.parse(init.body) : null,
      status: response.status,
      result: await response.clone().json(),
    })
  return response
}

await import('../../out/main/index.js')
