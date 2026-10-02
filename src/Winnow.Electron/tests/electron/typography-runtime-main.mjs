import { app, BrowserWindow } from 'electron'
import { resolve, basename } from 'node:path'

const index = process.argv.indexOf('--data-dir')
const root = resolve(process.argv[index + 1] ?? '')
if (index < 0 || !basename(root).startsWith('winnow-electron-typography-runtime-'))
  throw Error('Typography probe requires its isolated data directory')
app.setPath('userData', root)
app.whenReady().then(async () => {
  const window = new BrowserWindow({
    width: 640,
    height: 480,
    useContentSize: true,
    webPreferences: { sandbox: true, contextIsolation: true, nodeIntegration: false },
  })
  await window.loadFile(resolve(process.argv[index + 2]), {
    query: { kind: process.argv[index + 3], palette: process.argv[index + 4] ?? 'winnow' },
  })
  window.focus()
})
app.on('window-all-closed', () => app.quit())
