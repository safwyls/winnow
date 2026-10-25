import { app, shell } from 'electron'
import { basename, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const index = process.argv.indexOf('--data-dir')
const root = resolve(process.argv[index + 1] ?? '')
if (index < 0 || !basename(root).startsWith('winnow-electron-update-acknowledgement-'))
  throw Error('Update acknowledgement verification requires its isolated data directory.')
app.setAppPath(fileURLToPath(new URL('../..', import.meta.url)))
const state = (globalThis.__updateAcknowledgement = { requests: [], dispatches: [] })
shell.openExternal = async (url) => {
  state.dispatches.push(url)
}
shell.openPath = async () => {
  throw Error('Unexpected executable or folder dispatch')
}
const originalFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  const call =
    url.hostname === '127.0.0.1' && url.pathname.startsWith('/api/v1/')
      ? { path: url.pathname, method: init?.method ?? 'GET', completed: false }
      : null
  if (call) {
    if (/\/(acknowledge|restore)-updates$/.test(url.pathname) && init?.body) call.body = JSON.parse(init.body)
    state.requests.push(call)
  }
  const response = await originalFetch(input, init)
  if (call) {
    call.status = response.status
    if (/\/(acknowledge|restore)-updates$/.test(url.pathname) || url.pathname === '/api/v1/feed')
      call.result = await response.clone().json()
    call.completed = true
  }
  return response
}
await import('../../out/main/index.js')
