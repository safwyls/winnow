import { app, BrowserWindow, session, WebContentsView } from 'electron'
import { mkdirSync } from 'node:fs'
import { basename, isAbsolute, join } from 'node:path'
import { createAccountBrowser } from '../../src/main/account-browser'
import { openLinkBrowser } from '../../src/main/link-browser'
import { setPopoutTypography } from '../../src/main/popout-typography'

const directory = process.argv[process.argv.indexOf('--data-dir') + 1]
if (
  !directory ||
  !isAbsolute(directory) ||
  !basename(directory).startsWith('winnow-electron-typography-popout-')
)
  throw Error('An isolated typography popout directory is required.')
mkdirSync(directory, { recursive: true })
app.setPath('userData', directory)
app.setPath('sessionData', join(directory, 'chromium'))
const state = { pressed: [] as number[], providerScriptCalls: 0, forbidden: [] as string[] }
app.on('session-created', (profile) => {
  for (const scheme of ['http', 'https'])
    profile.protocol.handle(scheme, (request) => {
      if (new URL(request.url).hostname !== 'account.example.test') {
        state.forbidden.push(request.url)
        return new Response('No external fixture request', { status: 403 })
      }
      return new Response(
        '<!doctype html><html><body style="font:24px system-ui;padding:40px"><h1>Account typography fixture</h1><label>Password<input aria-label="Password" type="password"></label><p>Provider styles remain independent.</p></body></html>',
        { headers: { 'Content-Type': 'text/html' } },
      )
    })
})
app.whenReady().then(async () => {
  const owner = new BrowserWindow({
    show: false,
    webPreferences: { sandbox: true, contextIsolation: true, nodeIntegration: false },
  })
  owner.isFullScreen = () => true
  await owner.loadURL('data:text/html,<h1>Trusted typography owner</h1>')
  owner.webContents.executeJavaScriptInIsolatedWorld = async () => ({
    index: 0,
    buttons: Array.from({ length: 18 }, (_, i) => state.pressed.includes(i)),
    axes: [0, 0],
  })
  const profile = session.fromPartition('typography-popout-provider')
  const browser = createAccountBrowser(owner, profile, 'Typography account fixture')
  const execute = browser.webContents.executeJavaScript.bind(browser.webContents)
  browser.webContents.executeJavaScript = (...args) => {
    state.providerScriptCalls++
    return execute(...args)
  }
  const current = () => BrowserWindow.getAllWindows().find((window) => window !== owner)
  Object.assign(globalThis, {
    typographyPopout: {
      state,
      browser,
      current,
      size(percent: number) {
        setPopoutTypography(owner.webContents, {
          headingFont: 'Bricolage Grotesque',
          interfaceFont: 'IBM Plex Mono',
          dataFont: 'IBM Plex Mono',
          sizePercent: percent,
        })
      },
      geometry() {
        const window = current()!
        const provider = window.contentView.children.find((view) => view instanceof WebContentsView)!
        return { window: window.getContentBounds(), provider: provider.getBounds() }
      },
      async reference() {
        await openLinkBrowser(owner, 'https://account.example.test/reference')
      },
    },
  })
  await browser.loadURL('https://account.example.test/login')
  current()!.setFullScreen(false)
  current()!.setContentSize(1920, 1080)
  current()!.focus()
})
app.on('window-all-closed', () => app.quit())
