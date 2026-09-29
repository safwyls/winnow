import { EventEmitter } from 'node:events'
import { runInNewContext } from 'node:vm'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { BrowserWindow } from 'electron'
import type { BackendTransport } from '../src/main/transport'
import { steamFrameNavigationAllowed, steamTokenProbe } from '../src/main/steam-auth-policy'

const native = vi.hoisted(() => ({
  windows: [] as MockWindow[],
  probe: vi.fn(),
  cookies: vi.fn(),
  partition: vi.fn(),
  clearStorage: vi.fn(),
  clearCache: vi.fn(),
  capture: vi.fn(),
}))
interface MockWindow extends EventEmitter {
  webContents: EventEmitter & {
    url: string
    getURL(): string
    isLoading(): boolean
    executeJavaScript: typeof native.probe
  }
  destroyed: boolean
  close(): void
  loadURL(url: string): Promise<void>
}
vi.mock('electron', async () => {
  const { EventEmitter: Emitter } = await import('node:events')
  class Window extends Emitter {
    destroyed = false
    webContents = Object.assign(new Emitter(), {
      url: '',
      getURL() {
        return this.url
      },
      isLoading: () => false,
      executeJavaScript: native.probe,
      setWindowOpenHandler: vi.fn(),
    })
    constructor() {
      super()
      native.windows.push(this)
    }
    isDestroyed() {
      return this.destroyed
    }
    close() {
      this.destroy()
    }
    destroy() {
      if (!this.destroyed) {
        this.destroyed = true
        this.emit('closed')
      }
    }
    async loadURL(url: string) {
      this.webContents.url = url
      this.webContents.emit('did-start-navigation', {}, url, false, true)
    }
  }
  return { BrowserWindow: Window, session: { fromPartition: native.partition } }
})
vi.mock('../src/main/steam-capture', () => ({ captureSteamAccountPages: native.capture }))
import { cancelSteamWindow, signInToSteam } from '../src/main/steam-auth'
import { createAccountBrowser } from '../src/main/account-browser'

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}
const steamId = '76561198000000001'
const identity = () => ({
  steamid: steamId,
  token: `x.${Buffer.from(JSON.stringify({ sub: steamId, exp: Date.now() / 1000 + 3600 })).toString('base64url')}.x`,
})
const request = vi.fn()
const transport = { request } as unknown as BackendTransport
const start = (staySignedIn = true) =>
  signInToSteam(new EventEmitter() as BrowserWindow, transport, { consentGranted: true, staySignedIn })
async function windowReady() {
  await Promise.resolve()
  await Promise.resolve()
  expect(native.windows).toHaveLength(1)
  return native.windows[0]
}
beforeEach(() => {
  vi.useFakeTimers()
  native.windows.length = 0
  native.probe.mockReset().mockImplementation(async () => identity())
  native.cookies.mockReset().mockResolvedValue([{ httpOnly: true, secure: true, value: 'private-refresh' }])
  native.clearStorage.mockReset().mockResolvedValue(undefined)
  native.clearCache.mockReset().mockResolvedValue(undefined)
  native.capture.mockReset().mockResolvedValue({ captureDetail: 'Pages ready for review.' })
  native.partition.mockReset().mockReturnValue(
    Object.assign(new EventEmitter(), {
      setPermissionRequestHandler: vi.fn(),
      setPermissionCheckHandler: vi.fn(),
      cookies: { get: native.cookies },
      clearStorageData: native.clearStorage,
      clearCache: native.clearCache,
    }),
  )
  request.mockReset().mockImplementation(async ({ route }: { route: string }) => ({
    ok: true,
    data:
      route === 'connections.steam.signin'
        ? { attemptId: 'attempt' }
        : { signedIn: true, persisted: true, refreshTokenCaptured: true },
  }))
})
afterEach(() => {
  vi.useRealTimers()
})

describe('Steam native sign-in races and credential boundaries', () => {
  it.each([false, true])(
    'declined consent cancels before any window or backend request even with purchase capture=%s',
    async (capturePurchaseHistory) => {
      const result = await signInToSteam(new EventEmitter() as BrowserWindow, transport, {
        consentGranted: false,
        staySignedIn: true,
        capturePurchaseHistory,
      })
      expect(result).toMatchObject({ signedIn: false, outcome: 4 })
      expect(result.detail).toBeTruthy()
      expect(native.partition).not.toHaveBeenCalled()
      expect(native.windows).toHaveLength(0)
      expect(request).not.toHaveBeenCalled()
      expect(result.pages).toBeUndefined()
    },
  )

  it('exhausted signed-in mint pages return NoToken rather than a neutral closed-window result', async () => {
    native.probe.mockResolvedValue({ loggedIn: true, token: null, steamid: steamId })
    const result = start(false)
    const browser = await windowReady()
    const navigate = vi.spyOn(browser, 'loadURL')
    await browser.loadURL('https://store.steampowered.com/')
    await vi.advanceTimersByTimeAsync(17_000)
    expect(await result).toMatchObject({ signedIn: false, outcome: 1 })
    expect(native.cookies).not.toHaveBeenCalled()
    expect(request.mock.calls.some(([call]) => call.route === 'connections.steam.complete')).toBe(false)
    expect(navigate.mock.calls.map(([url]) => url)).toEqual([
      'https://store.steampowered.com/',
      'https://store.steampowered.com/explore/',
      'https://store.steampowered.com/replay/',
      'https://store.steampowered.com/points/shop/',
    ])
  })
  it('permits HTTPS challenge frames without granting them top-level navigation or application origins', () => {
    expect(steamFrameNavigationAllowed('https://www.google.com/recaptcha/api2/anchor', false)).toBe(true)
    expect(steamFrameNavigationAllowed('https://www.google.com/recaptcha/api2/anchor', true)).toBe(false)
    for (const address of [
      'winnow-app://app/index.html',
      'winnow-browser://external',
      'file:///C:/private',
      'http://example.com/',
      'https://localhost:4400/',
    ])
      expect(steamFrameNavigationAllowed(address, false)).toBe(false)
  })
  it('cancels immediately during backend begin and cancels a late challenge without opening a window or affecting a new attempt', async () => {
    const begin = deferred<{ ok: boolean; data: { attemptId: string } }>()
    request.mockImplementationOnce(() => begin.promise)
    const parent = new EventEmitter() as BrowserWindow
    const pending = signInToSteam(parent, transport, { consentGranted: true, staySignedIn: true })
    expect(cancelSteamWindow(new EventEmitter() as BrowserWindow)).toBe(false)
    expect(cancelSteamWindow(parent)).toBe(true)
    expect(await pending).toMatchObject({ signedIn: false, outcome: 4 })
    expect(native.windows).toHaveLength(0)
    expect(cancelSteamWindow(parent)).toBe(false)
    const next = start()
    const browser = await windowReady()
    begin.resolve({ ok: true, data: { attemptId: 'late-attempt' } })
    await Promise.resolve()
    await Promise.resolve()
    expect(request).toHaveBeenCalledWith(
      expect.objectContaining({
        route: 'connections.cancel',
        body: expect.objectContaining({ attemptId: 'late-attempt' }),
      }),
    )
    expect(browser.destroyed).toBe(false)
    browser.close()
    await next
    expect(request.mock.calls.some(([call]) => call.route === 'connections.steam.complete')).toBe(false)
  })
  it('the native cancel action closes only its owning Steam window and cannot persist a delayed probe', async () => {
    const probe = deferred<ReturnType<typeof identity>>()
    native.probe.mockReturnValue(probe.promise)
    const parent = new EventEmitter() as BrowserWindow,
      result = signInToSteam(parent, transport, { consentGranted: true, staySignedIn: false })
    const browser = await windowReady()
    await browser.loadURL('https://store.steampowered.com/')
    await vi.advanceTimersByTimeAsync(1000)
    expect(cancelSteamWindow(parent)).toBe(true)
    expect((await result).signedIn).toBe(false)
    probe.resolve(identity())
    await vi.advanceTimersByTimeAsync(1000)
    expect(request.mock.calls.some(([call]) => call.route === 'connections.steam.complete')).toBe(false)
  })
  it('locks physical keyboard and mouse events during desktop capture while Escape still closes', () => {
    const browser = createAccountBrowser(new EventEmitter() as BrowserWindow, {} as any, 'fixture')
    browser.setInputEnabled?.(false)
    for (const kind of ['before-input-event', 'before-mouse-event']) {
      const event = { preventDefault: vi.fn() }
      browser.webContents.emit(kind, event, { type: 'keyDown', key: 'a' })
      expect(event.preventDefault).toHaveBeenCalledOnce()
    }
    browser.webContents.emit(
      'before-input-event',
      { preventDefault: vi.fn() },
      { type: 'keyDown', key: 'Escape' },
    )
    expect(browser.isDestroyed()).toBe(true)
  })
  it('never executes a probe on a credential page and the injected script refuses it independently', async () => {
    const result = start()
    const browser = await windowReady()
    await vi.advanceTimersByTimeAsync(3000)
    expect(native.probe).not.toHaveBeenCalled()
    for (const pathname of [
      '/login/',
      '/join/',
      '/password/reset',
      '/twofactor/',
      '/mobilelogin/',
      '/account/security/',
    ]) {
      const context = {
        location: { origin: 'https://store.steampowered.com', pathname },
        document: new Proxy(
          {},
          {
            get() {
              throw new Error('Credential DOM must never be read')
            },
          },
        ),
      }
      expect(runInNewContext(steamTokenProbe, context)).toBeNull()
    }
    browser.close()
    expect((await result).signedIn).toBe(false)
  })

  it('does not persist credentials when the user closes while cookies are being read', async () => {
    const cookies = deferred<{ httpOnly: boolean; secure: boolean; value: string }[]>()
    native.cookies.mockReturnValue(cookies.promise)
    const result = start()
    const browser = await windowReady()
    await browser.loadURL('https://store.steampowered.com/')
    await vi.advanceTimersByTimeAsync(1000)
    expect(native.cookies).toHaveBeenCalledOnce()
    browser.close()
    expect(await result).toEqual({ signedIn: false, outcome: 4, detail: 'Steam sign-in cancelled.' })
    cookies.resolve([{ httpOnly: true, secure: true, value: 'late-refresh' }])
    await vi.advanceTimersByTimeAsync(1000)
    expect(request.mock.calls.some(([call]) => call.route === 'connections.steam.complete')).toBe(false)
    expect(native.clearStorage).toHaveBeenCalledOnce()
    expect(native.clearCache).toHaveBeenCalledOnce()
  })

  it('expires an attempt even while the document probe is still pending', async () => {
    const probe = deferred<ReturnType<typeof identity>>()
    native.probe.mockReturnValue(probe.promise)
    const result = start(false)
    const browser = await windowReady()
    await browser.loadURL('https://store.steampowered.com/')
    await vi.advanceTimersByTimeAsync(15 * 60_000)
    expect(await result).toEqual({ signedIn: false, outcome: 2, detail: 'Steam sign-in expired. Try again.' })
    probe.resolve(identity())
    await Promise.resolve()
    await Promise.resolve()
    expect(request.mock.calls.some(([call]) => call.route === 'connections.steam.complete')).toBe(false)
  })

  it('discards a token from a document that navigated away and back before the probe resolved', async () => {
    const probe = deferred<ReturnType<typeof identity>>()
    native.probe.mockReturnValue(probe.promise)
    const result = start(false)
    const browser = await windowReady()
    await browser.loadURL('https://store.steampowered.com/')
    await vi.advanceTimersByTimeAsync(1000)
    await browser.loadURL('https://store.steampowered.com/login/')
    await browser.loadURL('https://store.steampowered.com/')
    probe.resolve(identity())
    await Promise.resolve()
    await Promise.resolve()
    browser.close()
    await result
    expect(request.mock.calls.some(([call]) => call.route === 'connections.steam.complete')).toBe(false)
    expect(native.cookies).not.toHaveBeenCalled()
  })

  it('commits a current store identity and keeps private session credentials out of the returned result', async () => {
    const result = start()
    const browser = await windowReady()
    await browser.loadURL('https://store.steampowered.com/explore/')
    await vi.advanceTimersByTimeAsync(1000)
    expect(await result).toEqual({ signedIn: true, persisted: true, refreshTokenCaptured: true })
    expect(request).toHaveBeenCalledWith(
      expect.objectContaining({
        route: 'connections.steam.complete',
        body: expect.objectContaining({ steamId, refreshToken: 'private-refresh' }),
      }),
    )
    expect(browser.destroyed).toBe(true)
    expect(native.clearStorage).toHaveBeenCalledOnce()
    expect(native.clearCache).toHaveBeenCalledOnce()
  })

  it('A_refused_identity_carries_no_credential_at_all and never reaches the session store', async () => {
    native.probe.mockResolvedValue({ ...identity(), steamid: '76561198000000002' })
    const result = start()
    const browser = await windowReady()
    await browser.loadURL('https://store.steampowered.com/')
    await vi.advanceTimersByTimeAsync(1000)
    const refusal = await result
    expect(refusal).toEqual({ signedIn: false, outcome: 3, detail: 'Steam returned a different account from the signed-in page. Sign in again.' })
    expect(request.mock.calls.some(([call]) => call.route === 'connections.steam.complete')).toBe(false)
    expect(native.cookies).not.toHaveBeenCalled()
    expect(native.clearStorage).toHaveBeenCalledOnce()
  })

  it('a browser failure exposes no provider document or credential in the returned explanation', async () => {
    native.probe.mockRejectedValue(new Error('private-page-body private-access-token'))
    const result = start()
    const browser = await windowReady()
    await browser.loadURL('https://store.steampowered.com/')
    await vi.advanceTimersByTimeAsync(1000)
    const failed = await result
    expect(failed).toMatchObject({ signedIn: false, outcome: 6 })
    expect(JSON.stringify(failed)).not.toMatch(/private-page-body|private-access-token/)
    expect(request.mock.calls.some(([call]) => call.route === 'connections.steam.complete')).toBe(false)
  })

  it('does not collect refresh cookies without stay-signed-in consent', async () => {
    const result = start(false)
    const browser = await windowReady()
    await browser.loadURL('https://store.steampowered.com/')
    await vi.advanceTimersByTimeAsync(1000)
    await result
    expect(native.cookies).not.toHaveBeenCalled()
    expect(native.capture).not.toHaveBeenCalled()
    expect(request).toHaveBeenCalledWith(
      expect.objectContaining({
        route: 'connections.steam.complete',
        body: expect.objectContaining({ refreshToken: null }),
      }),
    )
  })

  it('captures account pages only after separate opt-in and leaves their import to review', async () => {
    const pages = { licensesHtml: '<table>private licence contents</table>', historyHtml: '<table>private history contents</table>', additionalLicensesHtml: [], capturedAt: '2026-09-29T00:00:00Z', source: 0, steamId }
    native.capture.mockResolvedValue({ pages, captureDetail: 'Pages ready for review.' })
    const result = signInToSteam(new EventEmitter() as BrowserWindow, transport, {
      consentGranted: true,
      staySignedIn: false,
      capturePurchaseHistory: true,
    })
    const browser = await windowReady()
    await browser.loadURL('https://store.steampowered.com/')
    await vi.advanceTimersByTimeAsync(1000)
    expect(await result).toMatchObject({ pages, captureDetail: 'Pages ready for review.' })
    expect(native.capture).toHaveBeenCalledWith(
      browser,
      expect.objectContaining({ expectedSteamId: steamId }),
    )
    expect(request.mock.calls.some(([call]) => call.route === 'imports.steam.pages')).toBe(false)
    expect(request.mock.calls.some(([call]) => JSON.stringify(call).includes('private licence contents'))).toBe(false)
  })

  it('The_request_reaches_the_browser_session_unchanged including nondefault capture bounds', async () => {
    const result = signInToSteam(new EventEmitter() as BrowserWindow, transport, {
      consentGranted: true, staySignedIn: false, capturePurchaseHistory: true,
      maxLoadMoreClicks: 7, maxLicensesPages: 3,
    })
    const browser = await windowReady()
    await browser.loadURL('https://store.steampowered.com/')
    await vi.advanceTimersByTimeAsync(1000)
    expect((await result).signedIn).toBe(true)
    expect(request).toHaveBeenCalledWith(expect.objectContaining({
      route: 'connections.steam.signin',
      body: expect.objectContaining({ request: {
        consentGranted: true, staySignedIn: false, capturePurchaseHistory: true,
        maxLoadMoreClicks: 7, maxLicensesPages: 3, timeout: '00:15:00',
      } }),
    }))
    expect(native.capture).toHaveBeenCalledWith(browser, expect.objectContaining({
      expectedSteamId: steamId, maxLoadMoreClicks: 7, maxLicensesPages: 3,
    }))
    expect(native.cookies).not.toHaveBeenCalled()
  })

  it.each([-1, 0.5, Infinity, NaN, 2147483648])('rejects malformed capture bound %s before opening a browser', async (maxLoadMoreClicks) => {
    await expect(signInToSteam(new EventEmitter() as BrowserWindow, transport, {
      consentGranted: true, staySignedIn: true, maxLoadMoreClicks,
    })).rejects.toThrow('capture limits')
    expect(native.partition).not.toHaveBeenCalled()
    expect(request).not.toHaveBeenCalled()
  })

  it('releases the active-sign-in guard when creation of a private session fails', async () => {
    native.partition.mockImplementationOnce(() => {
      throw new Error('Session unavailable')
    })
    expect(await start()).toMatchObject({ signedIn: false, outcome: 5 })
    const result = start()
    const browser = await windowReady()
    browser.close()
    expect((await result).signedIn).toBe(false)
  })
})
