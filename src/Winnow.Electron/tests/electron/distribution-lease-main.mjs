import { app, BrowserWindow } from 'electron'
import { spawn } from 'node:child_process'
import { writeFileSync } from 'node:fs'
import { pathToFileURL } from 'node:url'

const state = { processId: process.pid, children: [], events: [], errors: [] }
const persist = () => writeFileSync(process.env.WINNOW_LEASE_REPORT, JSON.stringify(state, null, 2))
let lease
let startDistributionLease
const start = async () => {
  const lifetime = new AbortController()
  lease = await startDistributionLease({
    helper: process.env.WINNOW_UPDATE_HELPER_PATH,
    installation: process.env.WINNOW_LEASE_INSTALLATION,
    dataDirectory: process.env.WINNOW_LEASE_DATA,
    signal: lifetime.signal,
    onLost: (error) => {
      state.errors.push(error.message)
      persist()
    },
    launch: (...args) => {
      const child = spawn(...args)
      const record = { pid: child.pid, args: args[1], stdout: '', stderr: '', exit: null, signal: null }
      state.children.push(record)
      child.stdout.on('data', (chunk) => {
        record.stdout += chunk.toString()
        persist()
      })
      child.stderr.on('data', (chunk) => {
        record.stderr += chunk.toString()
        persist()
      })
      child.on('exit', (code, signal) => {
        record.exit = code
        record.signal = signal
        persist()
      })
      persist()
      return child
    },
  })
  state.events.push({ kind: 'leased', canUpdate: lease.canUpdate, recoveryStatus: lease.recoveryStatus })
  persist()
}
globalThis.__lease = {
  state,
  start,
  ready: async () => {
    await lease.ready()
    state.events.push({ kind: 'ready' })
    persist()
  },
  dispose: () => {
    lease?.dispose()
    state.events.push({ kind: 'disposed' })
    persist()
  },
}
app.on('will-quit', () => {
  lease?.dispose()
  persist()
})
app.on('window-all-closed', () => app.quit())
// Do not await native readiness at module scope: Electron must finish loading its entry point first.
app
  .whenReady()
  .then(async () => {
    ;({ startDistributionLease } = await import(pathToFileURL(process.env.WINNOW_LEASE_MODULE).href))
    await start()
    const window = new BrowserWindow({
      width: 600,
      height: 240,
      webPreferences: { sandbox: true, contextIsolation: true, nodeIntegration: false },
    })
    await window.loadURL(
      'data:text/html,' +
        encodeURIComponent(
          '<!doctype html><title>Winnow helper lease fixture</title><h1>Portable update guard</h1><p>Isolated native protocol verification. No installer is run.</p>',
        ),
    )
  })
  .catch((error) => {
    state.errors.push(error.message)
    persist()
    app.exit(1)
  })
