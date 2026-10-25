import { app, BrowserWindow } from 'electron'
import { resolve, basename } from 'node:path'

const data = process.argv.indexOf('--data-dir')
const root = resolve(process.argv[data + 1] ?? '')
if (data < 0 || !basename(root).startsWith('winnow-electron-theme-contrast-'))
  throw Error('Theme contrast probe requires an isolated data directory')
app.setPath('userData', root)
app.whenReady().then(async () => {
  const window = new BrowserWindow({
    width: 760,
    height: 340,
    useContentSize: true,
    webPreferences: { sandbox: true, contextIsolation: true, nodeIntegration: false },
  })
  await window.loadFile(resolve(process.argv[data + 2]), {
    query: { palette: process.argv[data + 3], kind: process.argv[data + 4] },
  })
  window.focus()
})
app.on('window-all-closed', () => app.quit())
