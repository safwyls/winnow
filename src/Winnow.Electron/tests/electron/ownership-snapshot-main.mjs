import { app, shell } from 'electron'
import { basename, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const index = process.argv.indexOf('--data-dir')
const root = resolve(process.argv[index + 1] ?? '')
if (index < 0 || !basename(root).startsWith('winnow-electron-ownership-snapshot-'))
  throw Error('Ownership verification requires its own isolated data directory.')
app.setAppPath(fileURLToPath(new URL('../..', import.meta.url)))
const state = (globalThis.__ownershipSnapshot = { dispatches: [], writes: [] })
shell.openExternal = async (url) => {
  state.dispatches.push(url)
}
shell.openPath = async () => {
  throw Error('Unexpected executable or folder dispatch')
}
shell.showItemInFolder = () => {
  throw Error('Unexpected folder reveal')
}
const originalFetch = globalThis.fetch
globalThis.fetch = (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (
    url.hostname === '127.0.0.1' &&
    url.pathname === '/api/v1/connections/account-visibility' &&
    init?.method === 'PUT'
  )
    state.writes.push(JSON.parse(init.body))
  return originalFetch(input, init)
}
await import('../../out/main/index.js')
