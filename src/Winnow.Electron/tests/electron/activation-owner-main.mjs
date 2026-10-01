import { app, BrowserWindow } from 'electron'
import { mkdirSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const directory = process.argv[process.argv.indexOf('--data-dir') + 1]
const userData = join(directory, 'electron-userdata')
mkdirSync(join(userData, 'chromium'), { recursive: true })
app.setPath('userData', userData)
app.setPath('sessionData', join(userData, 'chromium'))
globalThis.__received = []
void app.whenReady().then(async () => {
  const { startActivationHost } = await import(pathToFileURL(process.env.WINNOW_ACTIVATION_MODULE).href)
  const host = await startActivationHost({
    appPath: fileURLToPath(new URL('../..', import.meta.url)),
    resourcesPath: process.resourcesPath,
    dataDirectory: directory,
    activation: { kind: 'show' },
    signal: new AbortController().signal,
    onActivation: (activation) => globalThis.__received.push(activation),
    onLost: (error) => {
      throw error
    },
  })
  if (!host.primary) throw Error('Fixture could not own its isolated profile')
  app.on('before-quit', () => host.dispose())
  const window = new BrowserWindow({
    show: false,
    webPreferences: { sandbox: true, contextIsolation: true, nodeIntegration: false },
  })
  await window.loadURL('about:blank')
})
