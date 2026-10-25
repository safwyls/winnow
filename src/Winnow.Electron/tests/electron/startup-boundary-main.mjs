import { app, dialog, ipcMain } from 'electron'
import { readFileSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

app.setAppPath(fileURLToPath(new URL('../..', import.meta.url)))
const reportPath = process.env.WINNOW_STARTUP_REPORT
const state = (globalThis.__startupBoundary = {
  processId: process.pid,
  windows: [],
  errors: [],
  requests: [],
  fetches: [],
  uncaught: [],
  exitCode: null,
  backendProcessId: null,
})
const persist = () => {
  if (reportPath) writeFileSync(reportPath, JSON.stringify(state, null, 2))
}
app.on('browser-window-created', (_event, window) => {
  state.windows.push({ id: window.id, at: Date.now(), nativeReady: app.isReady() })
  const directory = process.argv[process.argv.indexOf('--data-dir') + 1]
  try {
    state.backendProcessId = JSON.parse(
      readFileSync(join(directory, 'backend/endpoint.json'), 'utf8'),
    ).processId
  } catch {
    /* Refused data directories have no endpoint. */
  }
  persist()
})
// Preserve actual fatal-startup control flow while making its blocking OS message
// box reviewable in an unattended process test.
dialog.showErrorBox = (title, content) => {
  state.errors.push({ title, content })
  persist()
}
process.on('uncaughtExceptionMonitor', (error) => {
  state.uncaught.push(String(error))
  persist()
})
app.on('quit', (_event, code) => {
  state.exitCode = code
  persist()
})
const register = ipcMain.handle.bind(ipcMain)
ipcMain.handle = (channel, listener) =>
  register(
    channel,
    channel !== 'winnow:request'
      ? listener
      : async (event, request) => {
          const record = { route: request.route, at: Date.now(), completed: false, status: null }
          state.requests.push(record)
          const result = await listener(event, request)
          record.completed = true
          record.status = result.status
          return result
        },
  )
persist()
const nativeFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname === '127.0.0.1')
    state.fetches.push({ path: url.pathname, nativeReady: app.isReady(), at: Date.now() })
  return nativeFetch(input, init)
}
await import('./identity-projections-main.mjs')
