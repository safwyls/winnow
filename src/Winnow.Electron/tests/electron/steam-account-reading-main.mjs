import { app, dialog, ipcMain, shell } from 'electron'
import fs from 'node:fs/promises'
import { syncBuiltinESMExports } from 'node:module'
import { basename, dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const index = process.argv.indexOf('--data-dir')
const root = resolve(process.argv[index + 1] ?? '')
if (index < 0 || !basename(root).startsWith('winnow-electron-steam-account-reading-'))
  throw Error('Steam account verification requires its isolated data directory.')
const documents = join(root, 'docs')
app.setAppPath(fileURLToPath(new URL('../..', import.meta.url)))
app.setPath('documents', documents)
const state = (globalThis.__steamAccountReading = {
  requests: [],
  saveDialogs: [],
  openDialogs: [],
  dispatches: [],
  exports: [],
  writes: [],
  nextSave: null,
  holdNextWrite: false,
  failNextWrite: false,
  releaseWrite: null,
})
// The source substitutes only the export destination. Keep production export/API
// bytes and real filesystem writes, with a bounded gate inside this test's folder.
const originalWrite = fs.writeFile.bind(fs)
fs.writeFile = async (path, data, ...options) => {
  if (typeof path !== 'string' || dirname(resolve(path)) !== documents || !path.endsWith('.csv'))
    return originalWrite(path, data, ...options)
  const call = { path, bytes: Array.from(Buffer.from(data)), completed: false, failed: false }
  state.writes.push(call)
  if (state.holdNextWrite) {
    state.holdNextWrite = false
    await new Promise((done) => {
      state.releaseWrite = done
    })
    state.releaseWrite = null
  }
  if (state.failNextWrite) {
    state.failNextWrite = false
    call.failed = true
    throw Error('Disk full')
  }
  await originalWrite(path, data, ...options)
  call.completed = true
}
syncBuiltinESMExports()
const originalHandle = ipcMain.handle.bind(ipcMain)
ipcMain.handle = (channel, listener) =>
  originalHandle(channel, async (event, ...args) => {
    if (channel !== 'winnow:acquisitions:export') return listener(event, ...args)
    const call = { completed: false }
    state.exports.push(call)
    try {
      const result = await listener(event, ...args)
      call.result = result
      call.completed = true
      return result
    } catch (error) {
      call.error = String(error)
      throw error
    }
  })
dialog.showSaveDialog = async (_owner, options) => {
  state.saveDialogs.push(options)
  const filePath = state.nextSave
  state.nextSave = null
  return { canceled: !filePath, ...(filePath ? { filePath } : {}) }
}
dialog.showOpenDialog = async (_owner, options) => {
  state.openDialogs.push(options)
  return { canceled: true, filePaths: [] }
}
shell.openExternal = async (url) => {
  state.dispatches.push(url)
}
shell.openPath = async () => {
  throw Error('Unexpected folder or executable dispatch')
}
shell.showItemInFolder = () => {
  throw Error('Unexpected folder reveal')
}
const originalFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  const call =
    url.hostname === '127.0.0.1' && url.pathname.startsWith('/api/v1/')
      ? { path: url.pathname, method: init?.method ?? 'GET', completed: false }
      : null
  if (call) state.requests.push(call)
  const response = await originalFetch(input, init)
  if (call) {
    call.status = response.status
    call.completed = true
    if (url.pathname.startsWith('/api/v1/imports/steam/') && response.ok)
      call.result = await response.clone().json()
  }
  return response
}
await import('../../out/main/index.js')
