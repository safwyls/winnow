import { app, BrowserWindow, shell } from 'electron'
import { mkdirSync } from 'node:fs'
import { isAbsolute, join } from 'node:path'
import { openLinkBrowser } from '../../src/main/link-browser'

const dataDirectory = process.argv[process.argv.indexOf('--data-dir') + 1]
if (!dataDirectory || !isAbsolute(dataDirectory) || !dataDirectory.includes('winnow-electron-link-reader-'))
  throw Error('A throwaway browser-test directory is required.')
mkdirSync(dataDirectory, { recursive: true })
app.setPath('userData', dataDirectory)
app.setPath('sessionData', join(dataDirectory, 'chromium'))
const fullscreen = process.argv.includes('--fullscreen-test')
const state = { pressed: [] as number[], external: [] as string[], forbidden: [] as string[] }
shell.openExternal = async (url) => {
  state.external.push(url)
  if (url.includes('external-failure')) throw Error('fixture refusal')
}
const page = (url: string) =>
  `<!doctype html><html><head><title>Reading fixture</title></head><body style="font:24px sans-serif;margin:30px"><h1>${url.includes('/next') ? 'Next article' : 'Reading article'}</h1><a id="next" href="https://reading.example.test/next">Next article</a><a id="app" href="winnow-app://app/index.html">Application origin</a><a id="toolbar" href="winnow-browser://external">Toolbar command</a><button id="accept" onclick="document.querySelector('output').textContent=String(Number(document.querySelector('output').textContent)+1)">Controller action</button><output>0</output><iframe src="http://frame.example.test/frame" title="Web frame"></iframe><div style="height:3000px">Long reading content</div></body></html>`
app.on('session-created', (partition) => {
  for (const scheme of ['http', 'https'])
    partition.protocol.handle(scheme, (request) => {
      const url = new URL(request.url)
      if (!['reading.example.test', 'frame.example.test'].includes(url.hostname)) {
        state.forbidden.push(request.url)
        return new Response('Refused fixture request', { status: 403 })
      }
      return new Response(
        url.hostname === 'frame.example.test' ? '<p>Embedded web frame</p>' : page(request.url),
        { headers: { 'Content-Type': 'text/html' } },
      )
    })
})
app.whenReady().then(async () => {
  const owner = new BrowserWindow({
    width: 800,
    height: 600,
    show: false,
    fullscreen,
    webPreferences: { sandbox: true, contextIsolation: true, nodeIntegration: false },
  })
  await owner.loadURL('data:text/html,<h1>Isolated browser test owner</h1>')
  owner.webContents.executeJavaScriptInIsolatedWorld = async () => ({
    index: 0,
    buttons: Array.from({ length: 18 }, (_, index) => state.pressed.includes(index)),
    axes: [0, 0],
  })
  Object.assign(globalThis, {
    linkBrowserTest: {
      state,
      open: (url: string) => openLinkBrowser(owner, url),
      reader: () => BrowserWindow.getAllWindows().find((window) => window !== owner),
    },
  })
  await openLinkBrowser(owner, 'http://reading.example.test/article')
})
app.on('window-all-closed', () => app.quit())
