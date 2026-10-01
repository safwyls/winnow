import { app, shell } from 'electron'
import { basename, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const position = process.argv.indexOf('--data-dir')
const root = resolve(process.argv[position + 1] ?? '')
if (position < 0 || !basename(root).startsWith('winnow-electron-plugin-provenance-'))
  throw Error('Plugin presentation verification requires its own isolated data directory.')
app.setAppPath(fileURLToPath(new URL('../..', import.meta.url)))
const state = (globalThis.__pluginPresentation = { links: [], shellAttempts: [], requests: [], settings: [] })
shell.openExternal = async (url) => {
  if (url !== 'https://ca.account.sony.com/api/v1/ssocookie') throw Error('Unexpected external dispatch')
  state.links.push(url)
}
shell.openPath = async (path) => {
  state.shellAttempts.push({ kind: 'path', path })
  throw Error('Unexpected path dispatch')
}
shell.showItemInFolder = (path) => {
  state.shellAttempts.push({ kind: 'reveal', path })
  throw Error('Unexpected folder dispatch')
}

const originalFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  const method = init?.method ?? 'GET'
  if (url.hostname === '127.0.0.1') state.requests.push({ method, path: url.pathname })
  // Record the actual settings boundary; the real application/SDK fixture owns
  // all responses and secret storage. No library or settings response is replaced.
  if (
    url.hostname === '127.0.0.1' &&
    method !== 'GET' &&
    url.pathname.startsWith('/api/v1/connections/plugins')
  )
    state.settings.push({
      method,
      path: url.pathname.slice('/api/v1/connections/plugins'.length),
      body: init?.body ? JSON.parse(init.body) : undefined,
    })
  return originalFetch(input, init)
}
await import('../../out/main/index.js')
