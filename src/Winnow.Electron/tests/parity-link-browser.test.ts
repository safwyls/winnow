import { EventEmitter } from 'node:events'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { BrowserWindow } from 'electron'
const native = vi.hoisted(() => ({
  windows: [] as any[],
  views: [] as any[],
  sessions: [] as any[],
  sample: vi.fn(),
  external: vi.fn(),
  pageLoad: vi.fn(),
}))
vi.mock('electron', async () => {
  const { EventEmitter: Emitter } = await import('node:events')
  let nextId = 0
  const contents = () =>
    Object.assign(new Emitter(), {
      id: ++nextId,
      url: '',
      focused: false,
      destroyed: false,
      getURL() {
        return this.url
      },
      isDestroyed() {
        return this.destroyed
      },
      isFocused() {
        return this.focused
      },
      focus() {
        for (const view of native.views) view.webContents.focused = false
        for (const window of native.windows) window.webContents.focused = false
        this.focused = true
      },
      navigationHistory: {
        canGoBack: vi.fn(() => false),
        canGoForward: vi.fn(() => false),
        goBack: vi.fn(),
        goForward: vi.fn(),
      },
      executeJavaScript: vi.fn(async () => {}),
      executeJavaScriptInIsolatedWorld: native.sample,
      setWindowOpenHandler: vi.fn(),
      sendInputEvent: vi.fn(),
      setZoomFactor: vi.fn(),
      stop: vi.fn(),
      reload: vi.fn(),
      close: vi.fn(),
      async loadURL(this: EventEmitter & { url: string }, url: string) {
        this.url = url
        this.emit('did-start-navigation', {}, url, false, true)
        await native.pageLoad(url)
        this.emit('did-navigate', {}, url)
      },
    })
  class Window extends Emitter {
    id = ++nextId
    webContents = contents()
    destroyed = false
    focused = false
    contentView = { addChildView: vi.fn() }
    constructor(public options: unknown) {
      super()
      native.windows.push(this)
    }
    isDestroyed() {
      return this.destroyed
    }
    isFocused() {
      return this.focused
    }
    getContentBounds() {
      return { width: 1100, height: 850 }
    }
    async loadURL(url: string) {
      this.webContents.url = url
    }
    show = vi.fn()
    focus() {
      this.focused = true
    }
    setMenu = vi.fn()
    destroy() {
      this.destroyed = true
      this.emit('closed')
    }
    close() {
      this.destroy()
    }
  }
  class View {
    webContents = contents()
    setBounds = vi.fn()
    constructor(public options: unknown) {
      native.views.push(this)
    }
  }
  return {
    BrowserWindow: Window,
    WebContentsView: View,
    Menu: { buildFromTemplate: (template: unknown) => template },
    shell: { openExternal: native.external },
    session: {
      fromPartition: vi.fn(() => {
        const value = Object.assign(new Emitter(), {
          setPermissionRequestHandler: vi.fn(),
          setPermissionCheckHandler: vi.fn(),
          webRequest: { onBeforeRequest: vi.fn() },
        })
        native.sessions.push(value)
        return value
      }),
    },
  }
})
import { openLinkBrowser } from '../src/main/link-browser'
import { setPopoutTypography } from '../src/main/popout-typography'
import { DEFAULT_TYPOGRAPHY } from '../src/shared/typography'
function parent(fullscreen = false) {
  return {
    id: 123,
    isFullScreen: () => fullscreen,
    isDestroyed: () => false,
    webContents: Object.assign(new EventEmitter(), {
      executeJavaScriptInIsolatedWorld: native.sample,
      isDestroyed: () => false,
    }),
  } as unknown as BrowserWindow
}
const prevented = () => ({ preventDefault: vi.fn() })
const pad = (pressed: number[] = []) => ({
  index: 0,
  buttons: Array.from({ length: 18 }, (_, index) => pressed.includes(index)),
  axes: [0, 0],
})
beforeEach(() => {
  vi.useFakeTimers()
  native.windows.length = 0
  native.views.length = 0
  native.sessions.length = 0
  native.sample.mockReset().mockResolvedValue(pad())
  native.pageLoad.mockReset().mockResolvedValue(undefined)
  native.external.mockReset().mockResolvedValue(undefined)
})
afterEach(() => {
  for (const window of native.windows) if (!window.destroyed) window.destroy()
  vi.useRealTimers()
})

describe('isolated reusable native reading browser', () => {
  it('updates only the retained local toolbar, measures its font and resize height, and unsubscribes on close', async () => {
    const application = parent(true)
    await openLinkBrowser(application, 'https://example.test/article')
    const window = native.windows[0],
      controls = window.webContents,
      provider = native.views[0]
    let height = 170
    controls.executeJavaScript.mockImplementation(async (script: string) =>
      script.includes('document.fonts.ready') ? height : undefined,
    )
    controls.executeJavaScript.mockClear()
    provider.webContents.executeJavaScript.mockClear()
    setPopoutTypography(application.webContents, { ...DEFAULT_TYPOGRAPHY, sizePercent: 120 })
    await vi.advanceTimersByTimeAsync(0)
    expect(
      controls.executeJavaScript.mock.calls.some(([script]: [string]) =>
        script.includes('style.setProperty'),
      ),
    ).toBe(true)
    expect(
      controls.executeJavaScript.mock.calls.some(([script]: [string]) => script.includes('document.write')),
    ).toBe(false)
    expect(provider.webContents.executeJavaScript).not.toHaveBeenCalled()
    expect(provider.setBounds).toHaveBeenLastCalledWith({ x: 0, y: 170, width: 1100, height: 680 })
    height = 220
    window.emit('resize')
    await vi.advanceTimersByTimeAsync(0)
    expect(provider.setBounds).toHaveBeenLastCalledWith({ x: 0, y: 220, width: 1100, height: 630 })
    window.close()
    controls.executeJavaScript.mockClear()
    setPopoutTypography(application.webContents, { ...DEFAULT_TYPOGRAPHY, sizePercent: 80 })
    await vi.advanceTimersByTimeAsync(0)
    expect(controls.executeJavaScript).not.toHaveBeenCalled()
  })
  it('reuses one window for HTTP and HTTPS with a separate trusted toolbar and no page bridge', async () => {
    const owner = parent()
    await openLinkBrowser(owner, 'http://example.com/article')
    await openLinkBrowser(owner, 'https://example.com/next')
    expect(native.windows).toHaveLength(1)
    expect(native.views).toHaveLength(1)
    const window = native.windows[0],
      page = native.views[0]
    expect(window.webContents.url).toBe('about:blank')
    expect(page.webContents.url).toBe('https://example.com/next')
    expect(window.options.webPreferences).toMatchObject({
      sandbox: true,
      contextIsolation: true,
      nodeIntegration: false,
      webviewTag: false,
      disableDialogs: true,
    })
    expect(page.options.webPreferences).not.toHaveProperty('preload')
    expect(page.options.webPreferences).toMatchObject({
      nodeIntegration: false,
      nodeIntegrationInWorker: false,
      webSecurity: true,
    })
    expect(page.setBounds).toHaveBeenCalledWith({ x: 0, y: 92, width: 1100, height: 758 })
    expect(window.webContents.executeJavaScript).toHaveBeenCalledWith(
      expect.stringContaining('https://example.com/next'),
    )
  })
  it('blocks application, native, file, data and toolbar schemes at every website navigation gate', async () => {
    await openLinkBrowser(parent(), 'https://example.com/')
    const page = native.views[0].webContents
    for (const url of [
      'winnow-app://app/index.html',
      'winnow-theme://evil/main.js',
      'winnow-browser://external',
      'file:///C:/private',
      'data:text/html,unsafe',
      'steam://store/10',
      'goggalaxy://opengameview/gog_1',
    ]) {
      for (const kind of ['will-navigate', 'will-redirect']) {
        const event = prevented()
        page.emit(kind, event, url)
        expect(event.preventDefault).toHaveBeenCalledOnce()
      }
      const event = { ...prevented(), url, isMainFrame: false }
      page.emit('will-frame-navigate', event)
      expect(event.preventDefault).toHaveBeenCalledOnce()
      expect(page.setWindowOpenHandler.mock.calls[0][0]({ url })).toEqual({ action: 'deny' })
    }
    expect(native.external).not.toHaveBeenCalled()
    expect(page.url).toBe('https://example.com/')
  })
  it('permits web redirects frames and popups but denies downloads and permissions', async () => {
    await openLinkBrowser(parent(), 'https://start.example.com/')
    const page = native.views[0].webContents,
      partition = native.sessions[0]
    for (const url of [
      'http://example.com:8080/article?q=hello#section',
      'https://www.youtube.com/embed/1',
    ]) {
      const redirect = prevented()
      page.emit('will-redirect', redirect, url)
      expect(redirect.preventDefault).not.toHaveBeenCalled()
      const frame = { ...prevented(), url, isMainFrame: false }
      page.emit('will-frame-navigate', frame)
      expect(frame.preventDefault).not.toHaveBeenCalled()
      expect(page.setWindowOpenHandler.mock.calls[0][0]({ url })).toEqual({ action: 'deny' })
    }
    const download = prevented()
    partition.emit('will-download', download)
    expect(download.preventDefault).toHaveBeenCalledOnce()
    const permission = vi.fn()
    partition.setPermissionRequestHandler.mock.calls[0][0](page, 'media', permission)
    expect(permission).toHaveBeenCalledWith(false)
    expect(partition.setPermissionCheckHandler.mock.calls[0][0]()).toBe(false)
  })
  it('accepts toolbar commands only from its top frame and displays browser launch failure', async () => {
    await openLinkBrowser(parent(), 'http://example.com/article')
    const controls = native.windows[0].webContents,
      page = native.views[0].webContents
    const send = (url: string, main = true) =>
      controls.emit(
        'did-navigate-in-page',
        {},
        `${controls.url}#${url.replace('winnow-browser://', '')}`,
        main,
      )
    send('winnow-browser://external', false)
    send('winnow-browser://external?url=file:///private')
    expect(native.external).not.toHaveBeenCalled()
    native.external.mockRejectedValueOnce(Error('browser missing'))
    send('winnow-browser://external')
    await Promise.resolve()
    await Promise.resolve()
    await Promise.resolve()
    expect(native.external).toHaveBeenCalledWith('http://example.com/article')
    expect(controls.executeJavaScript).toHaveBeenCalledWith(
      expect.stringContaining('Could not open this page in your browser'),
    )
    page.navigationHistory.canGoBack.mockReturnValue(true)
    send('winnow-browser://back')
    expect(page.navigationHistory.goBack).toHaveBeenCalledOnce()
  })
  it('network request gating cannot load an app document through a frame or a website-owned toolbar clone', async () => {
    await openLinkBrowser(parent(), 'https://example.com/')
    const controls = native.windows[0].webContents,
      page = native.views[0].webContents,
      check = native.sessions[0].webRequest.onBeforeRequest.mock.calls[0][0]
    const decide = (webContentsId: number, url: string, resourceType = 'mainFrame') => {
      const answer = vi.fn()
      check({ webContentsId, url, resourceType }, answer)
      return answer.mock.calls[0][0].cancel
    }
    expect(decide(controls.id, controls.url)).toBe(false)
    expect(decide(page.id, controls.url)).toBe(true)
    expect(decide(page.id, 'winnow-app://app/index.html', 'subFrame')).toBe(true)
    expect(decide(page.id, 'http://127.0.0.1:4400/api/v1/library', 'xhr')).toBe(true)
    expect(decide(page.id, 'http://example.com/article')).toBe(false)
  })
  it('destroys failed startup and its page so the router can use the browser fallback', async () => {
    native.pageLoad.mockRejectedValueOnce(Error('offline'))
    await expect(openLinkBrowser(parent(), 'https://example.com/')).rejects.toThrow('offline')
    expect(native.windows[0].destroyed).toBe(true)
    expect(native.views[0].webContents.close).toHaveBeenCalledOnce()
  })
  it('a superseded navigation failure does not destroy the newly requested page', async () => {
    let reject!: (error: Error) => void
    native.pageLoad.mockImplementationOnce(
      () =>
        new Promise((_resolve, fail) => {
          reject = fail
        }),
    )
    const owner = parent(),
      old = openLinkBrowser(owner, 'https://example.com/old')
    await vi.advanceTimersByTimeAsync(0)
    await openLinkBrowser(owner, 'https://example.com/new')
    reject(Error('ERR_ABORTED'))
    await old
    expect(native.windows[0].destroyed).toBe(false)
    expect(native.views[0].webContents.url).toBe('https://example.com/new')
  })
  it('times out a stalled initial load without leaving an invisible browser process', async () => {
    native.pageLoad.mockReturnValue(new Promise(() => {}))
    const opened = openLinkBrowser(parent(), 'https://example.com/')
    const rejected = expect(opened).rejects.toThrow('too long')
    await vi.advanceTimersByTimeAsync(30_000)
    await rejected
    expect(native.windows[0].destroyed).toBe(true)
  })
  it('fullscreen sends bounded controller keys from the trusted parent and drops stale navigation samples', async () => {
    const owner = parent(true)
    await openLinkBrowser(owner, 'http://example.com/')
    const window = native.windows[0],
      page = native.views[0].webContents
    expect(window.options.fullscreen).toBe(true)
    expect(page.setZoomFactor).toHaveBeenCalledWith(1.5)
    await vi.advanceTimersByTimeAsync(50)
    native.sample.mockResolvedValue(pad([13]))
    await vi.advanceTimersByTimeAsync(50)
    expect(page.sendInputEvent).toHaveBeenCalledWith({ type: 'keyDown', keyCode: 'Down', modifiers: [] })
    native.sample.mockResolvedValue(pad())
    await vi.advanceTimersByTimeAsync(50)
    native.sample.mockResolvedValue(pad([0]))
    await vi.advanceTimersByTimeAsync(50)
    expect(page.sendInputEvent).toHaveBeenCalledWith({ type: 'char', keyCode: '\r', modifiers: [] })
    expect(owner.webContents.executeJavaScriptInIsolatedWorld).toHaveBeenCalledWith(321, [
      { code: expect.stringContaining('navigator.getGamepads') },
    ])
    let resolve!: (value: unknown) => void
    native.sample.mockImplementationOnce(
      () =>
        new Promise((done) => {
          resolve = done
        }),
    )
    await vi.advanceTimersByTimeAsync(50)
    page.emit('did-start-navigation', {}, 'http://example.com/new', false, true)
    resolve(pad([1]))
    await Promise.resolve()
    await Promise.resolve()
    expect(window.destroyed).toBe(false)
    native.sample.mockResolvedValue(pad())
    await vi.advanceTimersByTimeAsync(50)
    native.sample.mockResolvedValue(pad([1]))
    await vi.advanceTimersByTimeAsync(50)
    expect(window.destroyed).toBe(true)
  })
})
