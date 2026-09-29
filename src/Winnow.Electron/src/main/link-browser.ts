import { BrowserWindow, Menu, session, shell, WebContentsView } from 'electron'
import { readableWebUrl } from './security'
import {
  BrowserController,
  browserControllerKey,
  browserPadScript,
  readBrowserPad,
} from './browser-controller'
import { browserToolbar, browserToolbarAction } from './browser-toolbar'

interface Reader {
  window: BrowserWindow
  open(address: string): Promise<void>
}
const readers = new WeakMap<BrowserWindow, Reader>()

/** A page never shares the trusted toolbar's document or the application's preload/IPC. */
export async function openLinkBrowser(parent: BrowserWindow, url: string): Promise<void> {
  const address = readableWebUrl(url)
  if (!address) throw new Error('This page cannot open in Winnow.')
  let reader = readers.get(parent)
  if (!reader || reader.window.isDestroyed()) {
    reader = createReader(parent)
    readers.set(parent, reader)
  }
  await reader.open(address)
}

function createReader(parent: BrowserWindow): Reader {
  const fullscreen = parent.isFullScreen()
  const toolbarUrl = 'about:blank'
  const browsing = session.fromPartition(`winnow-reference-browser-${parent.id}-${Date.now()}`)
  browsing.setPermissionRequestHandler((_contents, _permission, callback) => callback(false))
  browsing.setPermissionCheckHandler(() => false)
  browsing.on('will-download', (event) => event.preventDefault())
  const preferences = {
    session: browsing,
    sandbox: true,
    contextIsolation: true,
    nodeIntegration: false,
    nodeIntegrationInWorker: false,
    webSecurity: true,
    webviewTag: false,
    spellcheck: false,
    disableDialogs: true,
    safeDialogs: true,
    navigateOnDragDrop: false,
  }
  const browser = new BrowserWindow({
    parent,
    width: 1100,
    height: 850,
    minWidth: 760,
    minHeight: 480,
    fullscreen,
    title: 'Winnow browser',
    backgroundColor: '#18191b',
    show: false,
    webPreferences: preferences,
  })
  const page = new WebContentsView({ webPreferences: preferences })
  browser.contentView.addChildView(page)
  const content = page.webContents,
    controls = browser.webContents,
    controller = new BrowserController()
  let disposed = false,
    documentGeneration = 0,
    openRequest = 0,
    poll: ReturnType<typeof setTimeout> | undefined,
    currentAddress = '',
    problem = ''
  const toolbarReady = browser
    .loadURL(toolbarUrl)
    .then(() =>
      controls.executeJavaScript(
        `document.open();document.write(${JSON.stringify(browserToolbar(fullscreen))});document.close()`,
      ),
    )
  browsing.webRequest.onBeforeRequest((details, callback) => {
    const toolbar = details.webContentsId === controls.id && details.url === toolbarUrl
    const navigation = details.resourceType === 'mainFrame' || details.resourceType === 'subFrame'
    callback({
      cancel:
        (navigation && !toolbar && !readableWebUrl(details.url)) ||
        (/^https?:/.test(details.url) && !readableWebUrl(details.url)),
    })
  })
  function layout() {
    const bounds = browser.getContentBounds(),
      height = fullscreen ? 126 : 92
    page.setBounds({ x: 0, y: height, width: bounds.width, height: Math.max(0, bounds.height - height) })
  }
  async function updateToolbar() {
    await toolbarReady
    if (disposed) return
    const state = {
      address: currentAddress,
      problem,
      back: content.navigationHistory.canGoBack(),
      forward: content.navigationHistory.canGoForward(),
    }
    await controls.executeJavaScript(
      `(()=>{const s=${JSON.stringify(state)};document.getElementById('address').textContent=s.address;document.getElementById('address').title=s.address;document.getElementById('problem').textContent=s.problem;for(const name of ['back','forward']){const a=document.getElementById(name);a.setAttribute('aria-disabled',String(!s[name]));a.tabIndex=s[name]?0:-1;if(s[name])a.setAttribute('href','#'+name);else a.removeAttribute('href')}})()`,
    )
  }
  function refreshToolbar() {
    void updateToolbar().catch(() => {})
  }
  function history(direction: 'back' | 'forward') {
    if (direction === 'back' && content.navigationHistory.canGoBack()) content.navigationHistory.goBack()
    if (direction === 'forward' && content.navigationHistory.canGoForward())
      content.navigationHistory.goForward()
  }
  async function external() {
    const address = readableWebUrl(currentAddress)
    if (!address) return
    try {
      await shell.openExternal(address)
      problem = ''
    } catch {
      problem = 'Could not open this page in your browser. Try again.'
    }
    refreshToolbar()
  }
  function toolbarAction(value: string) {
    const action = browserToolbarAction(value)
    if (action === 'close') browser.close()
    else if (action === 'external') void external()
    else if (action) history(action)
  }
  controls.on('will-navigate', (event) => event.preventDefault())
  controls.on('will-redirect', (event) => event.preventDefault())
  controls.on('will-frame-navigate', (event) => {
    if (event.url !== toolbarUrl) event.preventDefault()
  })
  controls.on('did-navigate-in-page', (_event, url, mainFrame) => {
    if (!mainFrame || !url.startsWith(`${toolbarUrl}#`)) return
    toolbarAction(`winnow-browser://${url.slice(toolbarUrl.length + 1)}`)
    if (!disposed)
      void controls
        .executeJavaScript("history.replaceState(null,'',location.href.split('#')[0])")
        .catch(() => {})
  })
  controls.setWindowOpenHandler(() => ({ action: 'deny' }))
  controls.on('will-attach-webview', (event) => event.preventDefault())
  content.on('will-navigate', (event, next) => {
    if (!readableWebUrl(next)) event.preventDefault()
  })
  content.on('will-redirect', (event, next) => {
    if (!readableWebUrl(next)) event.preventDefault()
  })
  content.on('will-frame-navigate', (event) => {
    if (!readableWebUrl(event.url)) event.preventDefault()
  })
  content.on('will-attach-webview', (event) => event.preventDefault())
  content.setWindowOpenHandler(({ url }) => {
    const next = readableWebUrl(url)
    if (next)
      void content.loadURL(next).catch(() => {
        problem = 'This page could not load. Try opening it in your browser.'
        refreshToolbar()
      })
    return { action: 'deny' }
  })
  content.on('did-start-navigation', (_event, url, _inPlace, mainFrame) => {
    if (!mainFrame) return
    documentGeneration++
    controller.reset()
    if (readableWebUrl(url)) currentAddress = url
    problem = ''
    refreshToolbar()
  })
  const navigated = () => {
    currentAddress = readableWebUrl(content.getURL()) ?? currentAddress
    refreshToolbar()
  }
  content.on('did-navigate', navigated)
  content.on('did-navigate-in-page', navigated)
  content.on('did-fail-load', (_event, code, _description, _url, mainFrame) => {
    if (mainFrame && code !== -3) {
      problem = 'This page could not load. Try opening it in your browser.'
      refreshToolbar()
    }
  })
  content.on('will-prevent-unload', (event) => event.preventDefault())
  content.on('page-title-updated', (event) => event.preventDefault())
  for (const target of [controls, content])
    target.on('before-input-event', (event, input) => {
      if (input.type !== 'keyDown') return
      if (input.key === 'Escape') {
        event.preventDefault()
        browser.close()
      } else if (input.alt && (input.key === 'ArrowLeft' || input.key === 'ArrowRight')) {
        event.preventDefault()
        history(input.key === 'ArrowLeft' ? 'back' : 'forward')
      }
    })
  browser.setMenu(
    Menu.buildFromTemplate([
      {
        label: 'Browse',
        submenu: [
          { label: 'Reload', accelerator: 'CmdOrCtrl+R', click: () => content.reload() },
          { label: 'Open in default browser', click: () => void external() },
          { label: 'Close browser', accelerator: 'Escape', click: () => browser.close() },
        ],
      },
      { label: 'Edit', submenu: [{ role: 'copy' }, { role: 'selectAll' }] },
    ]),
  )
  browser.on('resize', layout)
  browser.on('blur', () => controller.reset())
  browser.on('closed', () => {
    disposed = true
    if (poll) clearTimeout(poll)
    controller.reset()
    if (!content.isDestroyed()) content.close()
    if (readers.get(parent)?.window === browser) readers.delete(parent)
  })
  layout()
  async function pollController() {
    if (disposed) return
    const generation = documentGeneration
    try {
      if (!browser.isFocused() || parent.isDestroyed() || !fullscreen) controller.reset()
      else {
        const sample = await parent.webContents.executeJavaScriptInIsolatedWorld(321, [
          { code: browserPadScript },
        ])
        if (disposed || generation !== documentGeneration || !browser.isFocused()) {
          controller.reset()
          return
        }
        for (const button of controller.sample(readBrowserPad(sample), true, Date.now())) {
          if (button === 1) {
            browser.close()
            break
          }
          if (button === 9) {
            if (controls.isFocused()) content.focus()
            else {
              controls.focus()
              await controls.executeJavaScript("document.querySelector('a[href]')?.focus()")
            }
            continue
          }
          const key = browserControllerKey(button, true)
          if (!key) continue
          const destination = controls.isFocused() ? controls : content
          const modifiers = key.shift ? ['shift' as const] : []
          destination.sendInputEvent({ type: 'keyDown', keyCode: key.key, modifiers })
          // Chromium activates links and buttons on the character event, as a physical key does.
          if (key.key === 'Enter' || key.key === 'Space')
            destination.sendInputEvent({ type: 'char', keyCode: key.key === 'Enter' ? '\r' : ' ', modifiers })
          destination.sendInputEvent({ type: 'keyUp', keyCode: key.key, modifiers })
        }
      }
    } catch {
      controller.reset()
    } finally {
      if (!disposed) poll = setTimeout(() => void pollController(), 50)
    }
  }
  if (fullscreen) {
    content.setZoomFactor(1.5)
    void pollController()
  }
  return {
    window: browser,
    async open(address) {
      const request = ++openRequest
      currentAddress = address
      problem = ''
      documentGeneration++
      controller.reset()
      let timeout: ReturnType<typeof setTimeout> | undefined
      try {
        await toolbarReady
        if (disposed || request !== openRequest) return
        browser.show()
        browser.focus()
        content.focus()
        refreshToolbar()
        await Promise.race([
          content.loadURL(address),
          new Promise<never>((_resolve, reject) => {
            timeout = setTimeout(() => reject(new Error('The page took too long to load.')), 30_000)
          }),
        ])
        if (!disposed && request === openRequest) refreshToolbar()
      } catch (error) {
        if (disposed || request !== openRequest) return
        content.stop()
        browser.destroy()
        throw error
      } finally {
        if (timeout) clearTimeout(timeout)
      }
    },
  }
}
