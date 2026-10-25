import { BrowserWindow, ipcMain, session, shell, type IpcMainEvent, type WebFrameMain } from 'electron'
import { randomUUID } from 'node:crypto'
import { join } from 'node:path'
import type { BackendTransport } from './transport'
import type {
  EpicAuthChallenge,
  EpicSignInOptions,
  EpicSignInPreparation,
  EpicSignInResult,
} from '../shared/epic'
import { createAccountBrowser, type AccountBrowser } from './account-browser'
import {
  EpicAuthPolicy,
  EpicStrategy,
  epicOrigin,
  readEpicBody,
  validEpicCode,
  validateEpicChallenge,
} from './epic-auth-policy'

interface Attempt {
  parent: BrowserWindow
  clientId: string
  challenge?: EpicAuthChallenge
  policy?: EpicAuthPolicy
  browser?: AccountBrowser
  cancelled: boolean
  completing: boolean
  accepted: boolean
  generation: number
  document?: { token: string; frame: WebFrameMain }
  message?: (kind: string, value: unknown) => void
  finish?: (result: EpicSignInResult) => void
  endBeginning(): void
  closed(): void
  expiry?: ReturnType<typeof setTimeout>
}
const failed = (failure: number, canRetryManually = false): EpicSignInResult => ({
  succeeded: false,
  failure,
  persisted: false,
  ...(canRetryManually ? { canRetryManually: true } : {}),
})
const id = () => randomUUID().replaceAll('-', '')

/** Codes remain in main. One owner, backend challenge and document generation bind every capture. */
export class EpicSignInController {
  private current?: Attempt
  constructor(
    private readonly transport: Pick<BackendTransport, 'request'>,
    private readonly profileRoot: string,
    private readonly preload: string,
  ) {
    ipcMain.on('winnow:epic:document', this.document)
    ipcMain.on('winnow:epic:capture', this.capture)
  }
  dispose() {
    if (this.current) this.cancel(this.current.parent)
    ipcMain.off('winnow:epic:document', this.document)
    ipcMain.off('winnow:epic:capture', this.capture)
  }
  private document = (event: IpcMainEvent) => {
    const a = this.current
    if (
      !a?.browser ||
      !a.policy ||
      a.cancelled ||
      a.completing ||
      event.sender !== a.browser.webContents ||
      event.senderFrame !== event.sender.mainFrame
    ) {
      event.returnValue = null
      return
    }
    // The preload is synchronous so the narrow launcher facade exists before Epic's first script.
    // Per-document tokens also reject queued messages from a replaced same-origin document.
    const token = randomUUID()
    a.document = { token, frame: event.senderFrame }
    event.returnValue = {
      documentToken: token,
      origins: a.policy.trusted,
      bridge: a.policy.has(EpicStrategy.bridge),
    }
  }
  private capture = (event: IpcMainEvent, message: unknown) => {
    const a = this.current,
      m = message as { documentToken?: unknown; kind?: unknown; value?: unknown }
    if (
      !a?.browser ||
      !a.policy ||
      a.cancelled ||
      a.completing ||
      !a.accepted ||
      !a.document ||
      !m ||
      event.sender !== a.browser.webContents ||
      event.senderFrame !== event.sender.mainFrame ||
      event.senderFrame !== a.document.frame ||
      m.documentToken !== a.document.token ||
      !a.policy.trusts(event.senderFrame.url) ||
      a.policy.navigation(event.senderFrame.url) !== 'allow' ||
      !a.policy.has(EpicStrategy.bridge)
    )
      return
    if (m.kind === 'signed-in' || (m.kind === 'exchange' && validEpicCode(m.value)))
      a.message?.(m.kind, m.value)
  }
  async prepare(parent: BrowserWindow): Promise<EpicSignInPreparation | null> {
    if (this.current) throw new Error('An Epic sign-in is already open. Finish or cancel it first.')
    let endBeginning!: () => void
    const cancelled = new Promise<null>((resolve) => {
      endBeginning = () => resolve(null)
    })
    const a: Attempt = {
      parent,
      clientId: id(),
      cancelled: false,
      completing: false,
      accepted: false,
      generation: 0,
      endBeginning,
      closed: () => this.cancel(parent),
    }
    this.current = a
    parent.once('closed', a.closed)
    const beginning = this.transport.request<EpicAuthChallenge>({
      route: 'connections.epic.signin',
      body: { clientId: a.clientId },
    })
    void beginning
      .then((result) => {
        if (a.cancelled && result.ok && result.data?.attemptId)
          void this.cancelChallenge(a.clientId, result.data.attemptId)
      })
      .catch(() => {})
    try {
      const result = await Promise.race([beginning, cancelled])
      if (!result || a.cancelled) return null
      if (!result.ok || !result.data)
        throw new Error(
          result.status === 409
            ? 'Epic sign-in is not configured.'
            : 'Epic sign-in could not start. Try again after reconnecting.',
        )
      try {
        a.challenge = validateEpicChallenge(result.data)
      } catch (error) {
        if (/^[a-f0-9]{32}$/.test(result.data.attemptId))
          void this.cancelChallenge(a.clientId, result.data.attemptId)
        throw error
      }
      a.policy = new EpicAuthPolicy(a.challenge.request)
      a.expiry = setTimeout(() => this.cancel(parent), Date.parse(a.challenge.expiresAt) - Date.now())
      return {
        attemptId: a.challenge.attemptId,
        expiresAt: a.challenge.expiresAt,
        consentNotice: a.challenge.request.consentNotice,
      }
    } catch (error) {
      this.release(a)
      throw error instanceof Error && error.message.startsWith('Epic ')
        ? error
        : new Error('Epic sign-in could not start. Try again.')
    }
  }
  cancel(parent: BrowserWindow): boolean {
    const a = this.current
    if (!a || a.parent !== parent) return false
    // A sent single-use code may already be saved. Preserve its truthful completion result.
    if (a.completing) return false
    a.cancelled = true
    a.endBeginning()
    a.finish?.(failed(6))
    this.release(a)
    return true
  }
  private async cancelChallenge(clientId: string, attemptId: string) {
    await this.transport
      .request({ route: 'connections.cancel', body: { clientId, attemptId } })
      .catch(() => {})
  }
  private release(a: Attempt) {
    clearTimeout(a.expiry)
    a.parent.off('closed', a.closed)
    a.generation++
    a.document = undefined
    a.message = undefined
    if (a.browser && !a.browser.isDestroyed()) a.browser.destroy()
    a.browser = undefined
    if (a.challenge) void this.cancelChallenge(a.clientId, a.challenge.attemptId)
    if (this.current === a) this.current = undefined
  }
  private require(
    parent: BrowserWindow,
    options: EpicSignInOptions,
  ): Attempt & { challenge: EpicAuthChallenge; policy: EpicAuthPolicy } {
    const a = this.current
    if (!options || options.consentGranted !== true)
      throw new Error('Agree to connect your Epic account before signing in.')
    if (
      !a ||
      a.parent !== parent ||
      !a.challenge ||
      !a.policy ||
      a.cancelled ||
      a.completing ||
      a.challenge.attemptId !== options.attemptId ||
      Date.parse(a.challenge.expiresAt) <= Date.now()
    )
      throw new Error('This Epic sign-in is unavailable or expired. Start again.')
    a.accepted = true
    return a as Attempt & { challenge: EpicAuthChallenge; policy: EpicAuthPolicy }
  }
  async openManual(parent: BrowserWindow, options: EpicSignInOptions): Promise<void> {
    const a = this.require(parent, options)
    if (a.browser) throw new Error('Close the Epic sign-in window before using your browser.')
    try {
      await shell.openExternal(a.challenge.request.startUrl)
    } catch {
      throw new Error('Your browser could not open. Try again.')
    }
  }
  async completeManual(
    parent: BrowserWindow,
    options: EpicSignInOptions & { callback: string },
  ): Promise<EpicSignInResult> {
    const a = this.require(parent, options)
    if (a.browser) throw new Error('Close the Epic sign-in window before submitting an address.')
    if (typeof options.callback !== 'string' || options.callback.length > 16384)
      throw new Error('Paste the complete final sign-in address.')
    const input = options.callback.trim(),
      r = a.challenge.request
    let code = input
    if (r.expectedState || /^https?:/i.test(input)) {
      if (!a.policy.redirect(input) || !['matched', 'not-required'].includes(a.policy.state(input)))
        throw new Error(
          'This address does not belong to the current Epic sign-in. Paste its complete final address.',
        )
      code = a.policy.parameter(input, r.redirectCodeParameter) ?? ''
    }
    if (!validEpicCode(code)) throw new Error('The Epic response contains no valid authorization code.')
    return this.complete(a, code, 0, 'manual')
  }
  private async complete(
    a: Attempt,
    code: string,
    kind: 0 | 1,
    captureRoute: NonNullable<EpicSignInResult['captureRoute']>,
  ): Promise<EpicSignInResult> {
    if (a.cancelled || a.completing || this.current !== a || !a.challenge) return failed(6)
    a.completing = true
    a.browser?.setInputEnabled?.(false)
    try {
      const response = await this.transport.request<EpicSignInResult>({
        route: 'connections.epic.complete',
        body: {
          clientId: a.clientId,
          attemptId: a.challenge.attemptId,
          code,
          kind,
          state: a.challenge.request.expectedState ?? null,
        },
      })
      const r = response.data
      if (
        !response.ok ||
        !r ||
        typeof r.succeeded !== 'boolean' ||
        typeof r.persisted !== 'boolean' ||
        !Number.isInteger(r.failure) ||
        r.failure < 0 ||
        r.failure > 9
      )
        return failed(response.status === 0 || response.status >= 500 ? 4 : 5)
      return {
        succeeded: r.succeeded,
        failure: r.failure,
        persisted: r.persisted,
        accountId: typeof r.accountId === 'string' ? r.accountId.slice(0, 128) : null,
        displayName: typeof r.displayName === 'string' ? r.displayName.slice(0, 256) : null,
        captureRoute,
      }
    } catch {
      return failed(4)
    } finally {
      this.release(a)
    }
  }
  async signIn(parent: BrowserWindow, options: EpicSignInOptions): Promise<EpicSignInResult> {
    const a = this.require(parent, options)
    if (a.browser) throw new Error('An Epic sign-in window is already open.')
    let browser: AccountBrowser
    try {
      const profile = session.fromPath(join(this.profileRoot, 'epic'), { cache: true })
      profile.setPermissionRequestHandler((_contents, _permission, callback) => callback(false))
      profile.setPermissionCheckHandler(() => false)
      // Assign once to a persistent Session; repeated attempts must not accumulate handlers.
      profile.removeListener('will-download', preventDownload)
      profile.on('will-download', preventDownload)
      browser = createAccountBrowser(parent, profile, 'Connect Epic Games · Winnow', {
        preload: this.preload,
      })
      a.browser = browser
    } catch {
      return failed(7, true)
    }
    return new Promise<EpicSignInResult>((resolve) => {
      let settled = false,
        captured = false,
        busy = false,
        attempts = 0,
        harvestNavigations = 0,
        returnsToLogin = 0,
        noSession = false
      const finish = (result: EpicSignInResult) => {
        if (settled) return
        settled = true
        clearInterval(timer)
        a.finish = undefined
        a.message = undefined
        a.document = undefined
        a.generation++
        a.browser = undefined
        if (!browser.isDestroyed()) browser.destroy()
        if (!result.canRetryManually) this.release(a)
        resolve(result)
      }
      a.finish = finish
      const capture = (code: string, kind: 0 | 1, route: NonNullable<EpicSignInResult['captureRoute']>) => {
        if (settled || captured || a.cancelled || !validEpicCode(code)) return
        captured = true
        clearInterval(timer)
        void this.complete(a, code, kind, route).then(finish)
      }
      const navigate = (address: string) => {
        if (!settled && !captured) void browser.loadURL(address).catch(() => {})
      }
      const harvest = () => {
        if (
          a.policy.has(EpicStrategy.harvest) &&
          a.challenge.request.harvestUrl &&
          harvestNavigations < 2 &&
          !captured
        ) {
          harvestNavigations++
          navigate(a.challenge.request.harvestUrl)
        }
      }
      const redirect = (address: string) => {
        const state = a.policy.state(address)
        // A mismatched redirect must not fall through to another concurrently armed capture route.
        if (state === 'mismatch') {
          finish(failed(8, true))
          return
        }
        if (state === 'missing') {
          harvest()
          return
        }
        const code = a.policy.parameter(address, a.challenge.request.redirectCodeParameter)
        if (validEpicCode(code)) capture(code, 0, 'redirect')
        else harvest()
      }
      const navigation = (event: { preventDefault(): void }, address: string) => {
        const decision = a.policy.navigation(address)
        if (decision !== 'allow') event.preventDefault()
        if (decision === 'redirect') redirect(address)
      }
      browser.webContents.on('will-navigate', navigation)
      browser.webContents.on('will-redirect', navigation)
      browser.webContents.on('will-frame-navigate', (event) => {
        if (event.isMainFrame) navigation(event, event.url)
        else if (!a.policy.frame(event.url, false)) event.preventDefault()
      })
      browser.webContents.on('did-start-navigation', (_event, _url, inPlace, main) => {
        if (main !== false && !inPlace) {
          a.generation++
          a.document = undefined
          attempts = 0
        }
      })
      browser.webContents.on('will-attach-webview', (event) => event.preventDefault())
      browser.webContents.setWindowOpenHandler(({ url }) => {
        const decision = a.policy.popup(url)
        if (decision === 'redirect') redirect(url)
        else if (decision === 'allow') navigate(url)
        else if (decision === 'external') void shell.openExternal(url).catch(() => {})
        return { action: 'deny' }
      })
      browser.on('page-title-updated', (event) => event.preventDefault())
      browser.once('closed', () => {
        if (!captured) finish(failed(noSession ? 9 : 6, noSession))
      })
      a.message = (kind, value) => {
        if (kind === 'exchange' && validEpicCode(value)) capture(value, 1, 'launcher bridge')
        else if (kind === 'signed-in') harvest()
      }
      const probe = async () => {
        if (
          settled ||
          captured ||
          busy ||
          a.cancelled ||
          browser.isDestroyed() ||
          browser.webContents.isLoading()
        )
          return
        const address = browser.webContents.getURL(),
          generation = a.generation,
          r = a.challenge.request
        if (!a.policy.trusts(address) || a.policy.navigation(address) !== 'allow') return
        busy = true
        try {
          const fetch =
            a.policy.has(EpicStrategy.harvest) &&
            !!r.harvestUrl &&
            epicOrigin(address) === epicOrigin(r.harvestUrl) &&
            attempts++ < 150
          const body = a.policy.has(EpicStrategy.body) && r.jsonCodeFields.length > 0
          const script = `(async()=>{if(window.top!==window.self||location.href!==${JSON.stringify(address)})return null;
            if(${body}&&document.contentType==='application/json')return {body:document.body?.innerText?.slice(0,1048577)??'',source:'JSON body'};
            if(!${fetch})return null;
            try{const response=await fetch(${JSON.stringify(r.harvestUrl ?? '')},{credentials:'include',cache:'no-store',redirect:'error',signal:AbortSignal.timeout(8000)});return {body:(await response.text()).slice(0,1048577),source:'session harvest'}}catch{return null}})()`
          const value: unknown = await browser.webContents.executeJavaScriptInIsolatedWorld(1003, [
            { code: script },
          ])
          if (
            settled ||
            captured ||
            a.cancelled ||
            a.generation !== generation ||
            browser.isDestroyed() ||
            browser.webContents.getURL() !== address
          )
            return
          const result = value as { body?: unknown; source?: unknown } | null
          if (result && (result.source === 'JSON body' || result.source === 'session harvest')) {
            const reading = readEpicBody(result.body, r.jsonCodeFields)
            if (reading.outcome === 'code') {
              capture(reading.code, reading.kind, result.source)
              return
            }
            if (reading.outcome === 'no-session') {
              noSession = true
              if (result.source === 'JSON body') {
                if (returnsToLogin >= 2) {
                  finish(failed(9, true))
                  return
                }
                returnsToLogin++
                navigate(r.startUrl)
                return
              }
            }
          }
          if (a.policy.leftJourney(address)) harvest()
        } catch {
          /* Navigation and window closure invalidate a pending script without leaking its URL. */
        } finally {
          busy = false
        }
      }
      browser.webContents.on('dom-ready', () => {
        void probe()
      })
      const timer = setInterval(() => {
        void probe()
      }, 5000)
      navigate(a.challenge.request.startUrl)
    })
  }
}
function preventDownload(event: { preventDefault(): void }) {
  event.preventDefault()
}
