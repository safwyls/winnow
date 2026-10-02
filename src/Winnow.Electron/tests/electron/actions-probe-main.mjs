import { app, BrowserWindow } from 'electron'
import { resolve } from 'node:path'

const data = process.argv.indexOf('--data-dir')
if (data < 0 || !process.argv[data + 1]) throw Error('Action probe requires an isolated data directory')
app.setPath('userData', resolve(process.argv[data + 1]))
app.whenReady().then(async () => {
  const window = new BrowserWindow({
    width: 1920,
    height: 1080,
    useContentSize: true,
    webPreferences: { sandbox: true, contextIsolation: true, nodeIntegration: false },
  })
  await window.loadFile(resolve(process.argv[data + 2]))
  window.focus()
})
app.on('window-all-closed', () => app.quit())
