import { app, BrowserWindow, session } from 'electron'
import { mkdirSync } from 'node:fs'
import { isAbsolute, join } from 'node:path'
import { createAccountBrowser } from '../../src/main/account-browser'

const directory = process.argv[process.argv.indexOf('--data-dir') + 1]
if (!directory || !isAbsolute(directory) || !directory.includes('winnow-electron-account-input-'))
  throw Error('An isolated account-input test directory is required.')
mkdirSync(directory, { recursive: true })
app.setPath('userData', directory)
app.setPath('sessionData', join(directory, 'chromium'))
const state = { pressed: [] as number[], providerScriptCalls: 0, forbidden: [] as string[] }
app.whenReady().then(async () => {
  const owner = new BrowserWindow({
    show: false,
    fullscreen: true,
    webPreferences: { sandbox: true, contextIsolation: true, nodeIntegration: false },
  })
  await owner.loadURL('data:text/html,<h1>Account input fixture owner</h1>')
  owner.webContents.executeJavaScriptInIsolatedWorld = async () => ({
    index: 0,
    buttons: Array.from({ length: 18 }, (_, i) => state.pressed.includes(i)),
    axes: [0, 0],
  })
  const profile = session.fromPartition('account-input-fixture')
  for (const scheme of ['http', 'https'])
    profile.protocol.handle(scheme, (request) => {
      if (new URL(request.url).hostname !== 'account.example.test') {
        state.forbidden.push(request.url)
        return new Response('No external fixture request', { status: 403 })
      }
      return new Response(
        '<!doctype html><html><body style="font:24px system-ui;padding:40px"><h1>Account input fixture</h1><label>User name<input aria-label="User name"></label><label>Password<input aria-label="Password" type="password" autocomplete="off"></label><label><input type="checkbox">Remember this session</label><button onclick="document.querySelector(\'output\').textContent=\'Submitted\'">Sign in</button><output>Ready</output></body></html>',
        { headers: { 'Content-Type': 'text/html' } },
      )
    })
  profile.setPermissionRequestHandler((_c, _p, callback) => callback(false))
  profile.setPermissionCheckHandler(() => false)
  const browser = createAccountBrowser(owner, profile, 'Account fixture')
  const execute = browser.webContents.executeJavaScript.bind(browser.webContents)
  browser.webContents.executeJavaScript = (...args) => {
    state.providerScriptCalls++
    return execute(...args)
  }
  browser.webContents.setWindowOpenHandler(() => ({ action: 'deny' }))
  Object.assign(globalThis, {
    accountInputTest: {
      state,
      browser,
      window: () => BrowserWindow.getAllWindows().find((window) => window !== owner),
    },
  })
  await browser.loadURL('https://account.example.test/login')
})
app.on('window-all-closed', () => app.quit())
