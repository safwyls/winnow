import { app, BrowserWindow } from 'electron'
import { mkdirSync } from 'node:fs'
import { join } from 'node:path'

const directory = process.argv[process.argv.indexOf('--data-dir') + 1]
const userData = join(directory, 'electron-userdata')
mkdirSync(join(userData, 'chromium'), { recursive: true })
app.setPath('userData', userData)
app.setPath('sessionData', join(userData, 'chromium'))
globalThis.__received = []
app.on('second-instance', (_event, _argv, _cwd, data) => globalThis.__received.push(data.activation))
if (!app.requestSingleInstanceLock()) throw Error('Fixture could not own its isolated profile')
void app.whenReady().then(() => {
  const window = new BrowserWindow({
    show: false,
    webPreferences: { sandbox: true, contextIsolation: true, nodeIntegration: false },
  })
  void window.loadURL('about:blank')
})
