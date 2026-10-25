import { app, BrowserWindow, session } from 'electron'
import { basename, resolve } from 'node:path'

const index = process.argv.indexOf('--data-dir')
const root = resolve(process.argv[index + 1] ?? '')
if (index < 0 || !basename(root).startsWith('winnow-electron-design-preview-'))
  throw Error('Design preview requires an isolated data directory')
app.setPath('userData', root)
const webPreferences = { sandbox: true, contextIsolation: true, nodeIntegration: false }
const ledger = { network: [], navigation: [], windows: [] }
globalThis.designPreviewHostLedger = ledger
globalThis.designPreviewHostPreferences = webPreferences

// This host deliberately has no production main, preload, data host or IPC ports.
// Production renderer components receive the same isolated fixture as component tests.
app.whenReady().then(async () => {
  session.defaultSession.webRequest.onBeforeRequest(
    { urls: ['http://*/*', 'https://*/*', 'ws://*/*', 'wss://*/*'] },
    (details, respond) => {
      ledger.network.push(details.url)
      respond({ cancel: true })
    },
  )
  const window = new BrowserWindow({
    width: 1280,
    height: 820,
    useContentSize: true,
    webPreferences,
  })
  window.webContents.setWindowOpenHandler(({ url }) => {
    ledger.windows.push(url)
    return { action: 'deny' }
  })
  window.webContents.on('will-navigate', (event, url) => {
    ledger.navigation.push(url)
    event.preventDefault()
  })
  await window.loadFile(resolve(process.argv[index + 2]), {
    query: { mode: process.argv[index + 3] ?? 'desktop', surface: process.argv[index + 4] ?? 'shell' },
  })
  window.focus()
})
app.on('window-all-closed', () => app.quit())
