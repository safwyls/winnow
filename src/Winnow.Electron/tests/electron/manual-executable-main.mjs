import { app, dialog, shell } from 'electron'
import { join } from 'node:path'

const dataDirectory = process.argv[process.argv.indexOf('--data-dir') + 1]
app.setPath('documents', join(dataDirectory, 'files'))
const state = (globalThis.__manualExecutable = {
  selections: [],
  dialogs: [],
  searches: [],
  mutations: [],
  shellAttempts: [],
  results: {
    Celeste: [{ igdbId: 1, name: 'Celeste', firstReleaseYear: 2018, platforms: [] }],
    Cyberpunk: [{ igdbId: 1877, name: 'Cyberpunk 2077', firstReleaseYear: 2020, platforms: [] }],
    Prey: [{ igdbId: 1, name: 'Prey', firstReleaseYear: 2006, platforms: [] }],
    'Prey 2017': [{ igdbId: 1, name: 'Prey', firstReleaseYear: 2006, platforms: [] }],
    Iconoclasts: [{ igdbId: 9, name: 'Something Else', firstReleaseYear: 1999, platforms: [] }],
  },
})
dialog.showOpenDialog = async (_window, options) => {
  state.dialogs.push(options)
  if (!state.selections.length) throw Error('No manual executable selection was queued')
  const path = state.selections.shift()
  return path === null ? { canceled: true, filePaths: [] } : { canceled: false, filePaths: [path] }
}
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
  // Only the read-only metadata adapter response is canned, matching the source test's fake IGDB service.
  // Manual game reads/writes and all other requests still reach the real authenticated backend and SQLite.
  if (url.hostname === '127.0.0.1' && url.pathname === '/api/v1/metadata/igdb/search') {
    const title = url.searchParams.get('title')
    state.searches.push(title)
    return Response.json(state.results[title] ?? [])
  }
  if (
    url.hostname === '127.0.0.1' &&
    url.pathname.startsWith('/api/v1/manual-games') &&
    init?.method &&
    init.method !== 'GET'
  )
    state.mutations.push({
      method: init.method,
      path: url.pathname,
      body: init.body ? JSON.parse(init.body) : null,
    })
  return backendFetch(input, init)
}
await import('../../out/main/index.js')
