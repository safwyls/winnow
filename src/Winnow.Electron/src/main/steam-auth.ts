import { BrowserWindow, session, type Session } from 'electron'
import { randomUUID } from 'node:crypto'
import type { SteamCaptureResult, SteamSignInOptions, SteamSignInResult } from '../shared/bridge'
import type { BackendTransport } from './transport'
import { captureSteamAccountPages } from './steam-capture'
import { createAccountBrowser, type AccountBrowser } from './account-browser'
import {
  readSteamIdentity,
  steamMintAllowed,
  steamFrameNavigationAllowed,
  steamNavigationAllowed,
  steamTokenProbe,
} from './steam-auth-policy'

let active = false
let activeCancellation: { parent: BrowserWindow; cancel(): void } | undefined
export function cancelSteamWindow(parent: BrowserWindow): boolean {
  if (activeCancellation?.parent !== parent) return false
  activeCancellation.cancel()
  return true
}
export async function signInToSteam(
  parent: BrowserWindow,
  transport: BackendTransport,
  options: SteamSignInOptions,
): Promise<SteamSignInResult> {
  if (
    !options ||
    options.consentGranted !== true ||
    typeof options.staySignedIn !== 'boolean' ||
    (options.capturePurchaseHistory !== undefined && typeof options.capturePurchaseHistory !== 'boolean')
  )
    throw new Error('Agree to connect your Steam account before signing in.')
  if (active) throw new Error('A Steam sign-in is already open.')
  active = true
  const clientId = randomUUID().replaceAll('-', '')
  let attemptId: string | undefined
  let browser: AccountBrowser | undefined
  let privateSession: Session | undefined
  let cancelled = false,
    endBeginning!: () => void
  const cancelledBeginning = new Promise<undefined>((resolve) => {
    endBeginning = () => resolve(undefined)
  })
  const cancellation = {
    parent,
    cancel() {
      cancelled = true
      endBeginning()
      if (browser && !browser.isDestroyed()) browser.close()
    },
  }
  activeCancellation = cancellation
  try {
    privateSession = session.fromPartition(`winnow-steam-${randomUUID()}`, { cache: false })
    privateSession.setPermissionRequestHandler((_contents, _permission, callback) => callback(false))
    privateSession.setPermissionCheckHandler(() => false)
    privateSession.on('will-download', (event) => event.preventDefault())
    const beginning = transport.request<{ attemptId: string }>({
      route: 'connections.steam.signin',
      body: {
        clientId,
        request: {
          consentGranted: true,
          staySignedIn: options.staySignedIn,
          capturePurchaseHistory: options.capturePurchaseHistory === true,
          timeout: '00:15:00',
        },
      },
    })
    // A late backend challenge still belongs to this cancelled client and must be released.
    void beginning
      .then((result) => {
        if (cancelled && result.ok && result.data?.attemptId)
          void transport
            .request({ route: 'connections.cancel', body: { clientId, attemptId: result.data.attemptId } })
            .catch(() => {})
      })
      .catch(() => {})
    const challenge = await Promise.race([beginning, cancelledBeginning])
    if (!challenge)
      return { signedIn: false, outcome: 4, detail: 'Steam sign-in cancelled. Nothing was changed.' }
    if (!challenge.ok || !challenge.data)
      throw new Error(challenge.message || 'Steam sign-in could not start.')
    attemptId = challenge.data.attemptId
    if (cancelled)
      return { signedIn: false, outcome: 4, detail: 'Steam sign-in cancelled. Nothing was changed.' }
    browser = createAccountBrowser(parent, privateSession, 'Connect Steam · Winnow')
    browser.webContents.setWindowOpenHandler(() => ({ action: 'deny' }))
    browser.on('page-title-updated', (event) => event.preventDefault())
    browser.webContents.on('will-navigate', (event, url) => {
      if (!steamNavigationAllowed(url)) event.preventDefault()
    })
    browser.webContents.on('will-redirect', (event, url) => {
      if (!steamNavigationAllowed(url)) event.preventDefault()
    })
    browser.webContents.on('will-frame-navigate', (event) => {
      if (!steamFrameNavigationAllowed(event.url, event.isMainFrame)) event.preventDefault()
    })
    browser.webContents.on('will-attach-webview', (event) => event.preventDefault())
    const authWindow = browser
    const authSession = privateSession
    const outcome = new Promise<SteamSignInResult>((resolve, reject) => {
      let busy = false,
        completed = false,
        settled = false,
        fallback = 0
      let documentGeneration = 0
      authWindow.webContents.on('did-start-navigation', (_event, _url, _isInPlace, isMainFrame) => {
        if (isMainFrame !== false) documentGeneration++
      })
      const deadline = Date.now() + 15 * 60_000
      const mintPages = ['explore/', 'replay/', 'points/shop/']
      let signedInPolls = 0
      const stop = () => {
        clearInterval(timer)
        parent.off('closed', close)
      }
      const finish = (value: SteamSignInResult) => {
        if (!settled) {
          settled = true
          stop()
          resolve(value)
        }
      }
      const fail = (error: unknown) => {
        if (!settled) {
          settled = true
          stop()
          reject(error)
        }
      }
      const close = () => {
        if (!authWindow.isDestroyed()) authWindow.close()
      }
      parent.once('closed', close)
      authWindow.once('closed', () => {
        if (!completed) finish({ signedIn: false, detail: 'Steam sign-in cancelled.' })
      })
      const poll = async () => {
        if (completed || settled || authWindow.isDestroyed()) return
        if (Date.now() >= deadline) {
          finish({ signedIn: false, detail: 'Steam sign-in expired. Try again.' })
          return
        }
        if (busy) return
        const pageUrl = authWindow.webContents.getURL()
        if (!steamMintAllowed(pageUrl) || authWindow.webContents.isLoading()) return
        const generation = documentGeneration
        const currentPage = () =>
          !settled &&
          !completed &&
          !authWindow.isDestroyed() &&
          generation === documentGeneration &&
          !authWindow.webContents.isLoading() &&
          authWindow.webContents.getURL() === pageUrl &&
          steamMintAllowed(authWindow.webContents.getURL())
        busy = true
        try {
          const probe = await authWindow.webContents.executeJavaScript(steamTokenProbe)
          if (!currentPage()) return
          const identity = readSteamIdentity(probe)
          if (!identity) {
            if (probe?.loggedIn && ++signedInPolls >= 4) {
              signedInPolls = 0
              if (fallback < mintPages.length)
                await authWindow.loadURL(`https://store.steampowered.com/${mintPages[fallback++]}`)
              else
                finish({
                  signedIn: false,
                  detail: 'Steam did not provide a session. Try again or use a Web API key.',
                })
            }
            return
          }
          let refreshToken: string | null = null
          if (options.staySignedIn) {
            const cookies = await authSession.cookies.get({
              url: 'https://login.steampowered.com/',
              name: 'steamRefresh_steam',
            })
            if (!currentPage()) return
            refreshToken = cookies.find((cookie) => cookie.httpOnly && cookie.secure)?.value ?? null
          }
          if (!currentPage()) return
          completed = true
          authWindow.setInputEnabled?.(false)
          const result = await transport.request<SteamSignInResult>({
            route: 'connections.steam.complete',
            body: {
              clientId,
              attemptId,
              steamId: identity.steamId,
              accessToken: identity.token,
              refreshToken,
            },
          })
          if (!result.ok || !result.data)
            throw new Error(result.message || 'Steam sign-in could not be saved. Try again.')
          const capture =
            options.capturePurchaseHistory === true
              ? await captureSteamAccountPages(authWindow, { expectedSteamId: identity.steamId, deadline })
              : {}
          finish({ ...result.data, ...capture })
        } catch (error) {
          fail(error)
        } finally {
          busy = false
        }
      }
      const timer = setInterval(() => {
        void poll()
      }, 1000)
    })
    void browser.loadURL('https://store.steampowered.com/login/').catch(() => {
      browser?.close()
    })
    return await outcome
  } finally {
    try {
      if (browser && !browser.isDestroyed()) browser.destroy()
      if (attemptId)
        await transport
          .request({ route: 'connections.cancel', body: { clientId, attemptId } })
          .catch(() => {})
      await privateSession?.clearStorageData().catch(() => {})
      await privateSession?.clearCache().catch(() => {})
    } finally {
      if (activeCancellation === cancellation) activeCancellation = undefined
      active = false
    }
  }
}

export async function captureSteamPages(
  parent: BrowserWindow,
  options: { consentGranted: boolean },
): Promise<SteamCaptureResult> {
  if (options?.consentGranted !== true)
    throw new Error('Agree to read your Steam account pages before opening the capture window.')
  if (active) throw new Error('A Steam window is already open.')
  active = true
  let profile: Session | undefined, browser: AccountBrowser | undefined
  const close = () => {
    if (browser && !browser.isDestroyed()) browser.close()
  }
  const cancellation = { parent, cancel: close }
  activeCancellation = cancellation
  try {
    profile = session.fromPartition(`winnow-steam-capture-${randomUUID()}`, { cache: false })
    profile.setPermissionRequestHandler((_contents, _permission, callback) => callback(false))
    profile.setPermissionCheckHandler(() => false)
    profile.on('will-download', (event) => event.preventDefault())
    browser = createAccountBrowser(parent, profile, 'Capture Steam account pages · Winnow')
    browser.webContents.setWindowOpenHandler(() => ({ action: 'deny' }))
    browser.on('page-title-updated', (event) => event.preventDefault())
    browser.webContents.on('will-navigate', (event, url) => {
      if (!steamNavigationAllowed(url)) event.preventDefault()
    })
    browser.webContents.on('will-redirect', (event, url) => {
      if (!steamNavigationAllowed(url)) event.preventDefault()
    })
    browser.webContents.on('will-frame-navigate', (event) => {
      if (!steamFrameNavigationAllowed(event.url, event.isMainFrame)) event.preventDefault()
    })
    browser.webContents.on('will-attach-webview', (event) => event.preventDefault())
    parent.once('closed', close)
    return await captureSteamAccountPages(browser)
  } finally {
    parent.off('closed', close)
    try {
      if (browser && !browser.isDestroyed()) browser.destroy()
      await profile?.clearStorageData().catch(() => {})
      await profile?.clearCache().catch(() => {})
    } finally {
      if (activeCancellation === cancellation) activeCancellation = undefined
      active = false
    }
  }
}
