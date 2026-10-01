import { app, shell } from 'electron'
import { basename, resolve, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const position = process.argv.indexOf('--data-dir')
const directory = resolve(process.argv[position + 1] ?? '')
if (position < 0 || !basename(directory).startsWith('winnow-electron-diagnostics-'))
  throw Error('Diagnostics verification requires its own isolated data directory.')
app.setAppPath(fileURLToPath(new URL('../..', import.meta.url)))
Object.defineProperty(app, 'isPackaged', { get: () => true })
Object.defineProperty(process, 'resourcesPath', {
  value: fileURLToPath(new URL('../../resources', import.meta.url)),
})

const state = (globalThis.__diagnosticsNative = {
  loginEnabled: false,
  loginReads: [],
  loginWrites: [],
  folderFailure: false,
  folders: [],
  external: [],
  jumpLists: [],
  requests: [],
})
// Capture the actual main-process boundaries without changing Windows registration or opening Explorer.
app.getLoginItemSettings = (options) => {
  state.loginReads.push(structuredClone(options))
  return {
    openAtLogin: state.loginEnabled,
    openAsHidden: false,
    wasOpenedAtLogin: false,
    wasOpenedAsHidden: false,
    restoreState: false,
    executableWillLaunchAtLogin: state.loginEnabled,
    launchItems: [],
  }
}
app.setLoginItemSettings = (options) => {
  state.loginWrites.push(structuredClone(options))
  state.loginEnabled = options.openAtLogin
}
app.getJumpListSettings = () => ({ minItems: 10, removedItems: [] })
app.setJumpList = (items) => {
  state.jumpLists.push(structuredClone(items))
  return 'ok'
}
shell.openPath = async (path) => {
  if (resolve(path) !== join(directory, 'logs')) throw Error('Unexpected diagnostics folder dispatch')
  state.folders.push(path)
  return state.folderFailure ? 'Fixture folder open failure' : ''
}
shell.openExternal = async (url) => {
  state.external.push(url)
}
shell.showItemInFolder = () => {
  throw Error('Unexpected shell reveal')
}
const originalFetch = globalThis.fetch
globalThis.fetch = (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname === '127.0.0.1') state.requests.push({ path: url.pathname, method: init?.method ?? 'GET' })
  return originalFetch(input, init)
}
await import('../../out/main/index.js')
