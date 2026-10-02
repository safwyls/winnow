import { EventEmitter } from 'node:events'
import { runInNewContext } from 'node:vm'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { BrowserWindow } from 'electron'
import type { BackendTransport } from '../src/main/transport'
import type { EpicAuthChallenge } from '../src/shared/epic'
const native = vi.hoisted(() => ({
  ipc: null as unknown as EventEmitter,
  windows: [] as Window[],
  script: vi.fn(),
  open: vi.fn(),
  profile: vi.fn(),
}))
class Window extends EventEmitter {
  destroyed = false
  input = vi.fn()
  token = ''
  replies: unknown[] = []
  popup!: (data: { url: string }) => { action: string }
  webContents = Object.assign(new EventEmitter(), {
    mainFrame: { url: 'about:blank' },
    getURL: () => this.webContents.mainFrame.url,
    isLoading: () => false,
    executeJavaScriptInIsolatedWorld: native.script,
    setWindowOpenHandler: (handler: typeof this.popup) => {
      this.popup = handler
    },
  })
  constructor() {
    super()
    native.windows.push(this)
  }
  isDestroyed() {
    return this.destroyed
  }
  destroy() {
    if (!this.destroyed) {
      this.destroyed = true
      this.emit('closed')
    }
  }
  close() {
    this.destroy()
  }
  setInputEnabled(value: boolean) {
    this.input(value)
  }
  async loadURL(url: string) {
    this.webContents.emit('did-start-navigation', {}, url, false, true)
    this.webContents.mainFrame = { url }
    this.replies = []
    const event = {
      sender: this.webContents,
      senderFrame: this.webContents.mainFrame,
    }
    Object.defineProperty(event, 'returnValue', { set: (value) => this.replies.push(value) })
    native.ipc.emit('winnow:epic:document', event)
    this.token = (this.replies[0] as { documentToken?: string } | undefined)?.documentToken ?? ''
  }
  message(kind: string, value?: unknown, override = {}) {
    native.ipc.emit(
      'winnow:epic:capture',
      { sender: this.webContents, senderFrame: this.webContents.mainFrame, ...override },
      { documentToken: this.token, kind, value },
    )
  }
  ready() {
    this.webContents.emit('dom-ready')
  }
  navigate(address: string) {
    const preventDefault = vi.fn()
    this.webContents.emit('will-navigate', { preventDefault }, address)
    return preventDefault
  }
}
vi.mock('electron', async () => {
  const { EventEmitter } = await import('node:events')
  native.ipc = new EventEmitter()
  return { ipcMain: native.ipc, session: { fromPath: native.profile }, shell: { openExternal: native.open } }
})
vi.mock('../src/main/account-browser', () => ({ createAccountBrowser: () => new Window() }))
import { EpicSignInController } from '../src/main/epic-auth'
const request = vi.fn()
let controller: EpicSignInController, owner: BrowserWindow
const challenge = (): EpicAuthChallenge => ({
  attemptId: 'a'.repeat(32),
  expiresAt: new Date(Date.now() + 600000).toISOString(),
  request: {
    providerName: 'Epic Games',
    consentNotice: 'Explicit fixture consent',
    startUrl: 'https://www.epicgames.com/id/login?state=fixture-state',
    harvestUrl: 'https://www.epicgames.com/id/api/redirect',
    redirectUrl: 'https://localhost/launcher/authorized',
    expectedState: 'fixture-state',
    redirectCodeParameter: 'code',
    stateParameter: 'state',
    additionalNavigableOrigins: ['https://accounts.google.com'],
    jsonCodeFields: [
      { fieldName: 'authorizationCode', kind: 0 },
      { fieldName: 'exchangeCode', kind: 1 },
    ],
    strategies: 15,
    profileKey: 'epic',
    timeout: '00:10:00',
  },
})
const options = { attemptId: 'a'.repeat(32), consentGranted: true }
const completions = () =>
  request.mock.calls.map(([value]) => value).filter((x) => x.route === 'connections.epic.complete')
const tick = async () => {
  await vi.advanceTimersByTimeAsync(0)
}
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((r) => {
    resolve = r
  })
  return { resolve, promise }
}
async function open() {
  await controller.prepare(owner)
  const result = controller.signIn(owner, options)
  return { result, browser: native.windows.at(-1)! }
}
beforeEach(() => {
  vi.useFakeTimers()
  native.windows.length = 0
  native.script.mockReset().mockResolvedValue(null)
  native.open.mockReset().mockResolvedValue(undefined)
  native.profile.mockReset().mockReturnValue(
    Object.assign(new EventEmitter(), {
      setPermissionRequestHandler: vi.fn(),
      setPermissionCheckHandler: vi.fn(),
    }),
  )
  request.mockReset().mockImplementation(async ({ route }) => ({
    ok: true,
    status: 200,
    data:
      route === 'connections.epic.signin'
        ? challenge()
        : { succeeded: true, failure: 0, persisted: true, displayName: 'Fixture account' },
  }))
  owner = new EventEmitter() as BrowserWindow
  controller = new EpicSignInController(
    { request } as unknown as BackendTransport,
    'C:/Temp/fixture',
    'C:/fixture/epic.cjs',
  )
})
afterEach(() => {
  controller.dispose()
  vi.useRealTimers()
})
describe('Epic native attempt and credential lifecycle', () => {
  it('executes guarded same-origin harvest scripts at five-second intervals with a 150-request ceiling', async () => {
    const fetch = vi.fn(async () => ({ text: async () => '{"authorizationCode":null}' }))
    request.mockImplementation(async ({ route }) => ({
      ok: true,
      status: 200,
      data:
        route === 'connections.epic.signin'
          ? { ...challenge(), expiresAt: new Date(Date.now() + 900000).toISOString() }
          : {},
    }))
    native.script.mockImplementation(async (_world, scripts) => {
      const same = {},
        browser = native.windows.at(-1)!
      return runInNewContext(scripts[0].code, {
        window: { top: same, self: same },
        location: { href: browser.webContents.getURL() },
        document: { contentType: 'text/html' },
        fetch,
        AbortSignal,
      })
    })
    const { browser, result } = await open()
    browser.ready()
    await tick()
    expect(fetch).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(4999)
    expect(fetch).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(1)
    expect(fetch).toHaveBeenCalledTimes(2)
    await vi.advanceTimersByTimeAsync(750000)
    expect(fetch).toHaveBeenCalledTimes(150)
    expect(fetch).toHaveBeenCalledWith(
      challenge().request.harvestUrl,
      expect.objectContaining({ credentials: 'include', cache: 'no-store', redirect: 'error' }),
    )
    await browser.loadURL('https://accounts.google.com/login')
    native.script.mockClear()
    browser.ready()
    await tick()
    expect(native.script).not.toHaveBeenCalled()
    controller.cancel(owner)
    await result
  })
  it('replies exactly once to the synchronous document bootstrap before exposing the bridge', async () => {
    const { browser, result } = await open()
    expect(browser.replies).toHaveLength(1)
    expect(browser.replies[0]).toMatchObject({
      bridge: true,
      origins: ['https://www.epicgames.com:443', 'https://localhost:443'],
      documentToken: expect.any(String),
    })
    controller.cancel(owner)
    await result
  })
  it('keeps host initialization failures recoverable through an explicit manual fallback', async () => {
    native.profile.mockImplementation(() => {
      throw new Error('secret implementation detail')
    })
    await controller.prepare(owner)
    expect(await controller.signIn(owner, options)).toEqual({
      succeeded: false,
      failure: 7,
      persisted: false,
      canRetryManually: true,
    })
    expect(native.open).not.toHaveBeenCalled()
    await controller.openManual(owner, options)
    expect(native.open).toHaveBeenCalledWith(challenge().request.startUrl)
  })
  it.each(['unavailable', 'empty', 'no-session'])(
    'continues %s embedded capture through the same manual attempt and returns no captured code',
    async (cause) => {
      if (cause === 'unavailable')
        native.profile.mockImplementationOnce(() => {
          throw Error('private browser initialization')
        })
      const { browser, result } = await open()
      if (cause === 'empty')
        browser.navigate('https://localhost/launcher/authorized?code=injected&state=wrong')
      if (cause === 'no-session') {
        native.script.mockResolvedValue({
          body: '{"authorizationCode":null,"exchangeCode":null}',
          source: 'session harvest',
        })
        browser.ready()
        await tick()
        browser.close()
      }
      expect(await result).toEqual({
        succeeded: false,
        persisted: false,
        canRetryManually: true,
        failure: cause === 'unavailable' ? 7 : cause === 'empty' ? 8 : 9,
      })
      expect(completions()).toEqual([])
      expect(native.open).not.toHaveBeenCalled()
      await controller.openManual(owner, options)
      expect(native.open).toHaveBeenCalledExactlyOnceWith(challenge().request.startUrl)
      const completed = await controller.completeManual(owner, {
        ...options,
        callback: 'https://localhost/launcher/authorized?code=PRIVATE-MANUAL-CODE&state=fixture-state',
      })
      expect(completed).toMatchObject({ succeeded: true, persisted: true, captureRoute: 'manual' })
      expect(completions()).toHaveLength(1)
      expect(completions()[0].body).toMatchObject({
        attemptId: options.attemptId,
        kind: 0,
        code: 'PRIVATE-MANUAL-CODE',
      })
      expect(JSON.stringify(completed)).not.toContain('PRIVATE-MANUAL-CODE')
      expect(request.mock.calls.filter(([value]) => value.route === 'connections.epic.signin')).toHaveLength(
        1,
      )
      expect(native.open.mock.calls.flat().join(' ')).not.toContain('PRIVATE-MANUAL-CODE')
    },
  )
  it('supports the backend opt-out code endpoint with no state while retaining the correct grant', async () => {
    request.mockImplementation(async ({ route }) => ({
      ok: true,
      status: 200,
      data:
        route === 'connections.epic.signin'
          ? {
              ...challenge(),
              request: {
                ...challenge().request,
                expectedState: null,
                startUrl: challenge().request.harvestUrl,
              },
            }
          : { succeeded: true, failure: 0, persisted: true },
    }))
    await controller.prepare(owner)
    expect(await controller.completeManual(owner, { ...options, callback: 'manual-fixture' })).toMatchObject({
      succeeded: true,
    })
    expect(completions()[0].body).toMatchObject({ state: null, kind: 0, code: 'manual-fixture' })
  })
  it('returns only canonical consent and attempt metadata before any provider navigation', async () => {
    expect(await controller.prepare(owner)).toEqual({
      attemptId: options.attemptId,
      expiresAt: challenge().expiresAt,
      consentNotice: 'Explicit fixture consent',
    })
    expect(native.windows).toHaveLength(0)
    await expect(controller.signIn(owner, { ...options, consentGranted: false })).rejects.toThrow('Agree')
    expect(native.windows).toHaveLength(0)
  })
  it('cancels pending begin immediately and releases a late challenge without affecting the next attempt', async () => {
    const begin = deferred<any>()
    request.mockImplementationOnce(() => begin.promise)
    const first = controller.prepare(owner)
    expect(controller.cancel(owner)).toBe(true)
    expect(await first).toBe(null)
    await controller.prepare(owner)
    begin.resolve({ ok: true, status: 200, data: { ...challenge(), attemptId: 'b'.repeat(32) } })
    await tick()
    expect(request).toHaveBeenCalledWith(
      expect.objectContaining({
        route: 'connections.cancel',
        body: expect.objectContaining({ attemptId: 'b'.repeat(32) }),
      }),
    )
    const second = controller.signIn(owner, options)
    expect(native.windows).toHaveLength(1)
    controller.cancel(owner)
    expect((await second).failure).toBe(6)
    expect(completions()).toHaveLength(0)
  })
  it('binds cancellation and continuation to the owning window and current attempt', async () => {
    await controller.prepare(owner)
    expect(controller.cancel(new EventEmitter() as BrowserWindow)).toBe(false)
    await expect(controller.signIn(new EventEmitter() as BrowserWindow, options)).rejects.toThrow(
      'unavailable',
    )
    await expect(controller.signIn(owner, { ...options, attemptId: 'b'.repeat(32) })).rejects.toThrow(
      'unavailable',
    )
    await expect(controller.prepare(owner)).rejects.toThrow('already open')
  })
  it.each([
    ['exchange', 1, 'launcher bridge'],
    ['redirect', 0, 'redirect'],
    ['JSON body', 0, 'JSON body'],
    ['session harvest', 1, 'session harvest'],
  ] as const)(
    'completes %s exactly once using the correct backend grant and main-owned state',
    async (route, kind, captureRoute) => {
      const { browser, result } = await open()
      if (route === 'exchange') browser.message('exchange', 'fixture-code')
      else if (route === 'redirect')
        expect(
          browser.navigate('https://localhost/launcher/authorized?code=fixture-code&state=fixture-state'),
        ).toHaveBeenCalled()
      else {
        native.script.mockResolvedValue({
          body: JSON.stringify({ [kind ? 'exchangeCode' : 'authorizationCode']: 'fixture-code' }),
          source: route,
        })
        browser.ready()
      }
      await tick()
      browser.message('exchange', 'duplicate')
      expect(await result).toMatchObject({ succeeded: true, persisted: true, captureRoute })
      expect(completions()).toHaveLength(1)
      expect(completions()[0].body).toMatchObject({
        attemptId: options.attemptId,
        state: 'fixture-state',
        code: 'fixture-code',
        kind,
      })
      expect(browser.input).toHaveBeenCalledWith(false)
      expect(browser.destroyed).toBe(true)
    },
  )
  it('rejects social documents, third-party and same-origin subframes, foreign senders and replaced document tokens', async () => {
    const { browser, result } = await open(),
      oldToken = browser.token
    browser.message('exchange', 'iframe-code', { senderFrame: { url: browser.webContents.getURL() } })
    browser.message('exchange', 'foreign-code', { sender: {} })
    await browser.loadURL('https://accounts.google.com/login')
    browser.message('exchange', 'social-code')
    await browser.loadURL('https://www.epicgames.com/id/login')
    native.ipc.emit(
      'winnow:epic:capture',
      { sender: browser.webContents, senderFrame: browser.webContents.mainFrame },
      { documentToken: oldToken, kind: 'exchange', value: 'stale-code' },
    )
    expect(completions()).toHaveLength(0)
    controller.cancel(owner)
    expect((await result).failure).toBe(6)
  })
  it('does not complete a superseded document read after navigation or cancellation', async () => {
    const read = deferred<unknown>()
    native.script.mockReturnValue(read.promise)
    const { browser, result } = await open()
    browser.ready()
    await browser.loadURL('https://accounts.google.com/login')
    read.resolve({ source: 'JSON body', body: '{"authorizationCode":"stale-code"}' })
    await tick()
    expect(completions()).toHaveLength(0)
    controller.cancel(owner)
    expect((await result).failure).toBe(6)
  })
  it('refuses mismatched redirect state without permitting concurrently armed harvesting', async () => {
    const read = deferred<unknown>()
    native.script.mockReturnValue(read.promise)
    const { browser, result } = await open()
    browser.ready()
    browser.navigate('https://localhost/launcher/authorized?code=foreign&state=wrong')
    read.resolve({ source: 'session harvest', body: '{"authorizationCode":"late"}' })
    await tick()
    expect(await result).toMatchObject({ succeeded: false, failure: 8, canRetryManually: true })
    expect(completions()).toHaveLength(0)
  })
  it('harvests after missing state or a signed-in signal and bounds deliberate navigations', async () => {
    const { browser, result } = await open()
    browser.navigate('https://localhost/launcher/authorized?code=unbound')
    expect(browser.webContents.getURL()).toBe(challenge().request.harvestUrl)
    browser.message('signed-in')
    await browser.loadURL('https://www.epicgames.com/id/login')
    browser.message('signed-in')
    expect(browser.webContents.getURL()).toBe('https://www.epicgames.com/id/login')
    expect(completions()).toHaveLength(0)
    controller.cancel(owner)
    await result
  })
  it('distinguishes no-session JSON from an unrelated page and stops a cold-profile navigation loop', async () => {
    native.script.mockResolvedValue({
      body: '{"authorizationCode":null,"exchangeCode":null}',
      source: 'JSON body',
    })
    const { browser, result } = await open()
    for (let i = 0; i < 3; i++) {
      browser.ready()
      await tick()
    }
    expect(await result).toMatchObject({ failure: 9, canRetryManually: true })
    expect(completions()).toHaveLength(0)
  })
  it('does not interrupt the login form when background harvesting reports no session', async () => {
    native.script.mockResolvedValue({ body: '{"authorizationCode":null}', source: 'session harvest' })
    const { browser, result } = await open()
    browser.ready()
    await tick()
    expect(browser.webContents.getURL()).toBe(challenge().request.startUrl)
    browser.close()
    expect((await result).failure).toBe(9)
  })
  it('closing or cancelling an untouched login is neutral and never launches manual fallback automatically', async () => {
    const { browser, result } = await open()
    browser.close()
    expect(await result).toMatchObject({ failure: 6 })
    expect(native.open).not.toHaveBeenCalled()
    expect(completions()).toHaveLength(0)
  })
  it('keeps the actual saved result when cancellation arrives after a code was submitted', async () => {
    const completion = deferred<any>()
    request.mockImplementation(async ({ route }) =>
      route === 'connections.epic.complete'
        ? completion.promise
        : { ok: true, status: 200, data: challenge() },
    )
    const { browser, result } = await open()
    browser.message('exchange', 'fixture')
    expect(controller.cancel(owner)).toBe(false)
    completion.resolve({ ok: true, status: 200, data: { succeeded: true, failure: 0, persisted: false } })
    expect(await result).toMatchObject({ succeeded: true, persisted: false })
  })
  it('expires an idle attempt and closes its provider window', async () => {
    const { browser, result } = await open()
    await vi.advanceTimersByTimeAsync(600000)
    expect((await result).failure).toBe(6)
    expect(browser.destroyed).toBe(true)
    expect(completions()).toHaveLength(0)
  })
  it('uses the system browser only after consent and validates a pasted final address before spending its code', async () => {
    await controller.prepare(owner)
    await expect(controller.openManual(owner, { ...options, consentGranted: false })).rejects.toThrow('Agree')
    expect(native.open).not.toHaveBeenCalled()
    await controller.openManual(owner, options)
    expect(native.open).toHaveBeenCalledWith(challenge().request.startUrl)
    await expect(
      controller.completeManual(owner, {
        ...options,
        callback: 'https://localhost:8443/launcher/authorized?code=evil&state=fixture-state',
      }),
    ).rejects.toThrow('current Epic sign-in')
    expect(completions()).toHaveLength(0)
    expect(
      await controller.completeManual(owner, {
        ...options,
        callback: 'https://localhost/launcher/authorized?code=manual-code&state=fixture-state',
      }),
    ).toMatchObject({ succeeded: true, captureRoute: 'manual' })
  })
  it('preserves specific backend failure enums without exposing reflected codes in diagnostics', async () => {
    request.mockImplementation(async ({ route }) =>
      route === 'connections.epic.complete'
        ? { ok: false, status: 502, message: 'SECRET-CODE https://provider/?code=SECRET-CODE' }
        : { ok: true, status: 200, data: challenge() },
    )
    const { browser, result } = await open()
    browser.message('exchange', 'SECRET-CODE')
    expect(await result).toEqual({ succeeded: false, failure: 4, persisted: false })
  })
  it.each([1, 2, 3, 4, 5])(
    'preserves backend refusal %s without offering another prompt or exposing its private payload',
    async (failure) => {
      request.mockImplementation(async ({ route }) =>
        route === 'connections.epic.complete'
          ? {
              ok: true,
              status: 200,
              data: {
                succeeded: false,
                persisted: false,
                failure,
                code: 'SECRET-CODE',
                detail: 'SECRET-CODE',
              },
            }
          : { ok: true, status: 200, data: challenge() },
      )
      const { browser, result } = await open()
      browser.message('exchange', 'SECRET-CODE')
      const returned = await result
      expect(returned).toEqual({
        succeeded: false,
        persisted: false,
        failure,
        accountId: null,
        displayName: null,
        captureRoute: 'launcher bridge',
      })
      expect(JSON.stringify(returned)).not.toContain('SECRET-CODE')
      expect(native.open).not.toHaveBeenCalled()
      expect(browser.destroyed).toBe(true)
      expect(completions()).toHaveLength(1)
    },
  )
  it('folds approved popups into the guarded view, opens external web help, and blocks application and launcher schemes', async () => {
    const { browser, result } = await open()
    expect(browser.popup({ url: 'https://accounts.google.com/login' })).toEqual({ action: 'deny' })
    expect(browser.webContents.getURL()).toBe('https://accounts.google.com/login')
    browser.popup({ url: 'https://help.example.test/' })
    expect(native.open).toHaveBeenCalledWith('https://help.example.test/')
    for (const url of ['winnow-app://app/', 'steam://run/1', 'https://127.0.0.1/secret']) {
      browser.popup({ url })
      expect(browser.navigate(url)).toHaveBeenCalled()
    }
    expect(native.open).toHaveBeenCalledTimes(1)
    controller.cancel(owner)
    await result
  })
})
