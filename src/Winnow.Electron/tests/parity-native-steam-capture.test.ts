// @vitest-environment jsdom
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { EventEmitter } from 'node:events'
import { runInNewContext } from 'node:vm'
import type { BrowserWindow } from 'electron'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { captureSteamAccountPages, steamCapturePage, steamCaptureScript } from '../src/main/steam-capture'

const fixtures = Object.fromEntries(
  ['licenses-page1', 'licenses-final-page', 'purchase-history', 'purchase-history-exhausted'].map((name) => [
    name,
    readFileSync(resolve('../../tests/fixtures/steam-account-pages', `${name}.html`), 'utf8'),
  ]),
)
const steamId = '76561198000000001'
class Browser extends EventEmitter {
  destroyed = false
  document: Document = document
  loaded: string[] = []
  setTitle = vi.fn()
  webContents = Object.assign(new EventEmitter(), {
    url: '',
    getURL: () => this.webContents.url,
    isLoading: () => false,
    executeJavaScript: vi.fn(async (script: string) =>
      runInNewContext(script, {
        location: new URL(this.webContents.url),
        document: this.document,
        getComputedStyle: window.getComputedStyle.bind(window),
      }),
    ),
  })
  constructor(
    readonly page: (
      url: string,
      browser: Browser,
    ) => { fixture: string; identity?: string | null; mutate?: (document: Document) => void } = (url) => ({
      fixture: url.includes('/history/')
        ? 'purchase-history-exhausted'
        : url.includes('?')
          ? 'licenses-final-page'
          : 'licenses-page1',
      identity: steamId,
    }),
  ) {
    super()
  }
  isDestroyed() {
    return this.destroyed
  }
  async loadURL(url: string) {
    this.loaded.push(url)
    this.webContents.url = url
    this.webContents.emit('did-start-navigation', {}, url, false, true)
    const page = this.page(url, this)
    this.document = new DOMParser().parseFromString(fixtures[page.fixture], 'text/html')
    const config = this.document.createElement('div')
    config.id = 'application_config'
    config.setAttribute('data-store_user_config', '{"webapi_token":"must-not-cross-the-bridge"}')
    config.setAttribute('data-userinfo', JSON.stringify({ steamid: page.identity }))
    this.document.body.append(config)
    page.mutate?.(this.document)
  }
  close() {
    this.destroyed = true
    this.emit('closed')
  }
}
beforeEach(() => {
  vi.useFakeTimers()
})
afterEach(() => {
  vi.useRealTimers()
})
async function capture(browser: Browser, options = {}) {
  const pending = captureSteamAccountPages(browser as unknown as BrowserWindow, options)
  await vi.advanceTimersByTimeAsync(500)
  return pending
}

describe('bounded Steam account capture using sanitized real-page fixtures', () => {
  it('walks license pagination and captures history while stripping session-bearing markup', async () => {
    const browser = new Browser((url) => ({
      fixture: url.includes('/history/')
        ? 'purchase-history-exhausted'
        : url.includes('?')
          ? 'licenses-final-page'
          : 'licenses-page1',
      identity: steamId,
      mutate: (doc) => {
        for (const span of doc.querySelectorAll('.license_paginator_ctn span'))
          span.textContent = url.includes('?')
            ? 'Showing licenses 101-200 of 200'
            : 'Showing licenses 1-100 of 200'
      },
    }))
    const result = await capture(browser, { expectedSteamId: steamId })
    expect(browser.loaded).toHaveLength(3)
    expect(result.pages?.additionalLicensesHtml).toHaveLength(1)
    expect(result.pages?.steamId).toBe(steamId)
    expect(result.licensesTruncated).toBe(false)
    expect(result.historyTruncated).toBe(false)
    expect(result.pages?.licensesHtml).toContain('license_date_col')
    expect(result.pages?.historyHtml).toContain('wallet_table_row')
    expect(JSON.stringify(result.pages)).not.toContain('must-not-cross-the-bridge')
    expect(JSON.stringify(result.pages)).not.toContain('application_config')
    expect(JSON.stringify(result.pages)).not.toContain('continuationToken')
    expect(JSON.stringify(result.pages)).not.toContain('onclick')
  })

  it('reports gaps between captured license pages as incomplete even after the next link disappears', async () => {
    const result = await capture(new Browser())
    expect(result.pages?.additionalLicensesHtml).toHaveLength(1)
    expect(result.licensesTruncated).toBe(true)
  })

  it('refuses account and login sibling paths before reading any document', () => {
    for (const url of [
      'https://store.steampowered.com/login/',
      'https://store.steampowered.com/account/',
      'https://store.steampowered.com/account/licenses/detail/1',
      'https://steamcommunity.com/account/licenses/',
    ]) {
      expect(steamCapturePage(url)).toBeNull()
      expect(
        runInNewContext(steamCaptureScript('licenses', 'capture'), {
          location: new URL(url),
          document: new Proxy(
            {},
            {
              get() {
                throw new Error('Wrong document was touched')
              },
            },
          ),
        }),
      ).toBeNull()
    }
    expect(steamCapturePage('https://store.steampowered.com/account/licenses/?offset=100')).toBe('licenses')
  })

  it('discards every earlier page if the account changes between page types', async () => {
    const browser = new Browser((url) => ({
      fixture: url.includes('/history/') ? 'purchase-history-exhausted' : 'licenses-final-page',
      identity: url.includes('/history/') ? '76561198000000002' : steamId,
    }))
    const result = await capture(browser)
    expect(result.pages).toBeUndefined()
    expect(result.captureDetail).toContain('identity changed')
  })

  it('leaves all pages under an unknown account when neither page exposes an identity', async () => {
    const browser = new Browser((url) => ({
      fixture: url.includes('/history/') ? 'purchase-history-exhausted' : 'licenses-final-page',
      identity: null,
    }))
    const result = await capture(browser)
    expect(result.pages?.steamId).toBeNull()
    expect(result.pages?.historyHtml).toBeTruthy()
  })

  it('never substitutes the signed-in account when a captured page has no observed identity', async () => {
    const browser = new Browser(() => ({ fixture: 'licenses-final-page', identity: null }))
    const result = await capture(browser, { expectedSteamId: steamId })
    expect(result.pages).toBeUndefined()
  })

  it('caps both page walks and preserves incomplete markers for the import parser', async () => {
    const browser = new Browser((url) => ({
      fixture: url.includes('/history/') ? 'purchase-history' : 'licenses-page1',
      identity: steamId,
    }))
    const result = await capture(browser, { maxLicensesPages: 0, maxLoadMoreClicks: 0 })
    expect(browser.loaded).toHaveLength(2)
    expect(result.licensesTruncated).toBe(true)
    expect(result.historyTruncated).toBe(true)
    expect(result.pages?.licensesHtml).toContain('license_paginator_next')
    expect(result.pages?.historyHtml).toContain('load_more_button')
  })

  it('waits for load-more growth and records a complete document after the control disappears', async () => {
    const browser = new Browser((url) => ({
      fixture: url.includes('/history/') ? 'purchase-history' : 'licenses-final-page',
      identity: steamId,
      mutate: (doc) => {
        const more = doc.querySelector<HTMLButtonElement>('#load_more_button')
        if (more)
          more.onclick = () => {
            const row = doc.querySelector('tr.wallet_table_row')!
            row.parentElement!.append(row.cloneNode(true))
            more.style.display = 'none'
          }
      },
    }))
    const result = await capture(browser)
    expect(result.historyTruncated).toBe(false)
    expect(result.pages?.historyHtml).not.toContain('load_more_button')
  })

  it('reports a stalled history as incomplete instead of clicking forever', async () => {
    const browser = new Browser((url) => ({
      fixture: url.includes('/history/') ? 'purchase-history' : 'licenses-final-page',
      identity: steamId,
    }))
    const pending = captureSteamAccountPages(browser as unknown as BrowserWindow)
    await vi.advanceTimersByTimeAsync(16_000)
    const result = await pending
    expect(result.historyTruncated).toBe(true)
    expect(result.pages?.historyHtml).toContain('load_more_button')
  })

  it('returns already captured pages when the private window is closed during a later read', async () => {
    const browser = new Browser((url, current) => {
      if (url.includes('/history/')) current.close()
      return { fixture: 'licenses-final-page', identity: steamId }
    })
    const result = await capture(browser)
    expect(result.pages?.licensesHtml).toBeTruthy()
    expect(result.pages?.historyHtml).toBeUndefined()
    expect(result.historyTruncated).toBe(true)
  })

  it('stops a pending script when the private window closes without returning its late contents', async () => {
    const browser = new Browser()
    let resolve!: (value: unknown) => void
    browser.webContents.executeJavaScript.mockImplementationOnce(
      () =>
        new Promise((done) => {
          resolve = done
        }),
    )
    const pending = captureSteamAccountPages(browser as unknown as BrowserWindow)
    await vi.advanceTimersByTimeAsync(500)
    browser.close()
    await vi.advanceTimersByTimeAsync(250)
    expect((await pending).pages).toBeUndefined()
    resolve({ steamId, rows: 1, nextUrl: null, hasMore: false, html: 'late-private-data' })
    await Promise.resolve()
    expect(browser.webContents.executeJavaScript).toHaveBeenCalledOnce()
  })

  it('rejects an unexpected license paginator without navigating outside the capture paths', async () => {
    const browser = new Browser((url) => ({
      fixture: url.includes('/history/') ? 'purchase-history-exhausted' : 'licenses-page1',
      identity: steamId,
      mutate: (doc) =>
        doc.querySelector('a.license_paginator_next')?.setAttribute('href', 'https://example.com/private'),
    }))
    const result = await capture(browser)
    expect(browser.loaded.some((url) => url.includes('example.com'))).toBe(false)
    expect(result.licensesTruncated).toBe(true)
  })
})
