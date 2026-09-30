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
  it.each(['licenses', 'history'] as const)(
    'recognizes the original signed-in %s fixture and refuses its login form',
    async (kind) => {
      const browser = new Browser(() => ({
        fixture: kind === 'licenses' ? 'licenses-page1' : 'purchase-history',
        identity: steamId,
      }))
      await browser.loadURL(`https://store.steampowered.com/account/${kind}/`)
      expect(browser.document.querySelector('#account_pulldown')).not.toBeNull()
      const before = await browser.webContents.executeJavaScript(steamCaptureScript(kind, 'probe'))
      expect(before.rows).toBe(kind === 'licenses' ? 14 : 13)
      expect(before.steamId).toBe(steamId)
      const password = browser.document.createElement('input')
      password.type = 'password'
      browser.document.body.append(password)
      expect(await browser.webContents.executeJavaScript(steamCaptureScript(kind, 'probe'))).toBeNull()
    },
  )
  it('counts real history rows without the obsolete transactions id and clicks the actual load-more control', async () => {
    const browser = new Browser(() => ({ fixture: 'purchase-history', identity: steamId }))
    await browser.loadURL('https://store.steampowered.com/account/history/')
    expect(browser.document.getElementById('store_transactions')).toBeNull()
    expect(
      browser.document.querySelectorAll('table.wallet_history_table tbody tr.wallet_table_row'),
    ).toHaveLength(13)
    const more = browser.document.querySelector<HTMLButtonElement>('#load_more_button')!
    const click = vi.fn()
    more.onclick = click
    expect(await browser.webContents.executeJavaScript(steamCaptureScript('history', 'probe'))).toMatchObject(
      { rows: 13, hasMore: true },
    )
    expect(await browser.webContents.executeJavaScript(steamCaptureScript('history', 'more'))).toBe(true)
    expect(click).toHaveBeenCalledOnce()
  })
  it('keeps each captured licence table header once and the final paginator exhausted', async () => {
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
    const result = await capture(browser)
    expect(result.captureOutcome).toBe('captured')
    const pages = [result.pages!.licensesHtml!, ...result.pages!.additionalLicensesHtml].map((html) =>
      new DOMParser().parseFromString(html, 'text/html'),
    )
    expect(pages).toHaveLength(2)
    for (const [index, page] of pages.entries()) {
      expect(page.querySelectorAll('table.account_table')).toHaveLength(1)
      expect(page.querySelectorAll('table.account_table th.license_date_col')).toHaveLength(1)
      expect(page.querySelectorAll('table.account_table td.license_date_col')).toHaveLength(
        index === 0 ? 14 : 3,
      )
      expect(page.querySelector('#load_more_button')).toBeNull()
    }
    expect(pages[0].querySelector('a.license_paginator_next')).not.toBeNull()
    expect(pages[1].querySelector('a.license_paginator_next')).toBeNull()
    expect(pages[1].querySelector('.license_paginator_ctn')!.textContent).toContain('101-200 of 200')
    expect(result.licensesTruncated).toBe(false)
    expect(browser.loaded[1]).toContain('/account/licenses/?')
  })
  describe.each(['licenses', 'history'] as const)('%s script isolation', (kind) => {
    it.each(['probe', 'capture', 'more'] as const)(
      'answers instead of throwing when the page fails during %s',
      async (action) => {
        const browser = new Browser(() => ({
          fixture: kind === 'licenses' ? 'licenses-page1' : 'purchase-history',
          identity: steamId,
        }))
        await browser.loadURL(`https://store.steampowered.com/account/${kind}/`)
        browser.document.querySelector = () => {
          throw Error('page changed during read')
        }
        await expect(
          browser.webContents.executeJavaScript(steamCaptureScript(kind, action)),
        ).resolves.toBeNull()
      },
    )
  })
  it('defaults to a fifteen minute account session before returning no-session without pages', async () => {
    const browser = new Browser()
    browser.loadURL = async () => {
      browser.webContents.url = 'https://store.steampowered.com/login/'
    }
    let settled = false
    const pending = captureSteamAccountPages(browser as unknown as BrowserWindow).then((result) => {
      settled = true
      return result
    })
    await vi.advanceTimersByTimeAsync(15 * 60_000 - 1)
    expect(settled).toBe(false)
    await vi.advanceTimersByTimeAsync(251)
    expect(await pending).toMatchObject({
      captureOutcome: 'no-session',
      licensesPagesWalked: 0,
      loadMoreClicks: 0,
    })
  })
  it('a complete run reports twelve history expansions and both captured documents with their timestamp', async () => {
    const capturedAt = new Date().toISOString()
    let clicks = 0
    const browser = new Browser((url) => ({
      fixture: url.includes('/history/') ? 'purchase-history' : 'licenses-final-page',
      identity: steamId,
      mutate: (doc) => {
        for (const span of doc.querySelectorAll('.license_paginator_ctn span'))
          span.textContent = 'Showing licenses 1-1 of 1'
        const more = doc.querySelector<HTMLButtonElement>('#load_more_button')
        if (more)
          more.onclick = () => {
            const row = doc.querySelector('tr.wallet_table_row')!
            row.parentElement!.append(row.cloneNode(true))
            if (++clicks === 12) more.remove()
          }
      },
    }))
    const pending = captureSteamAccountPages(browser as unknown as BrowserWindow)
    await vi.advanceTimersByTimeAsync(4000)
    const result = await pending
    expect(result).toMatchObject({
      captureOutcome: 'captured',
      loadMoreClicks: 12,
      historyStoppedBecause: 'exhausted',
      licensesPagesWalked: 0,
    })
    expect(result.pages).toMatchObject({ capturedAt, source: 0, steamId })
    expect(result.pages?.licensesHtml).toBeTruthy()
    expect(result.pages?.historyHtml).toBeTruthy()
    expect(result.captureDetail).not.toMatch(/incomplete|failed|could not|stopped/i)
  })

  it.each([
    [9, false],
    [50, true],
  ] as const)(
    'reports %s further licence pages with capped=%s after the real page walk',
    async (further, capped) => {
      const browser = new Browser((url) => ({
        fixture: url.includes('/history/') ? 'purchase-history-exhausted' : 'licenses-page1',
        identity: steamId,
        mutate: (doc) => {
          if (!url.includes('/licenses/')) return
          const index = Number(new URL(url).searchParams.get('index') || '0')
          for (const span of doc.querySelectorAll('.license_paginator_ctn span'))
            span.textContent = `Showing licenses ${index + 1}-${index + 1} of ${further + (capped ? 2 : 1)}`
          for (const next of doc.querySelectorAll('a.license_paginator_next')) {
            if (index === further && !capped) next.remove()
            else
              next.setAttribute('href', `https://store.steampowered.com/account/licenses/?index=${index + 1}`)
          }
        },
      }))
      const result = await capture(browser, { maxLicensesPages: 50 })
      expect(result.licensesPagesWalked).toBe(further)
      expect(result.pages?.additionalLicensesHtml).toHaveLength(further)
      expect(result.licensesStoppedBecause).toBe(capped ? 'cap' : 'exhausted')
      expect(result.licensesTruncated).toBe(capped)
    },
  )
  it.each([
    ['account/licenses/', 'licenses'],
    ['account/licenses', 'licenses'],
    ['account/LICENSES/', 'licenses'],
    ['account/licenses/?continuationToken=A5F2C1&offset=100', 'licenses'],
    ['account/licenses?offset=900&continuationToken=ZZ', 'licenses'],
    ['account/history/', 'history'],
    ['account/history', 'history'],
    ['account/history/?l=english', 'history'],
  ] as const)('the actual capture script accepts the source page spelling %s', async (path, kind) => {
    const browser = new Browser(() => ({
      fixture: kind === 'licenses' ? 'licenses-final-page' : 'purchase-history-exhausted',
      identity: steamId,
    }))
    await browser.loadURL(`https://store.steampowered.com/${path}`)
    const captured = await browser.webContents.executeJavaScript(steamCaptureScript(kind, 'capture'))
    expect(captured.steamId).toBe(steamId)
    expect(captured.html).toContain(kind === 'licenses' ? 'license_date_col' : 'wallet_table_row')
    expect(captured.html).not.toContain('must-not-cross-the-bridge')
  })

  it('counts further licence pages and stops when a followed document adds no rows', async () => {
    const browser = new Browser((url) => ({
      fixture: url.includes('/history/') ? 'purchase-history-exhausted' : 'licenses-page1',
      identity: steamId,
      mutate: (doc) => {
        if (!url.includes('/licenses/')) return
        doc
          .querySelector('a.license_paginator_next')
          ?.setAttribute(
            'href',
            `https://store.steampowered.com/account/licenses/?offset=${url.includes('?') ? '200' : '100'}`,
          )
        if (url.includes('?'))
          for (const row of doc.querySelectorAll('tr')) if (!row.querySelector('th')) row.remove()
      },
    }))
    const result = await capture(browser)
    expect(browser.loaded).toHaveLength(3)
    expect(browser.loaded.some((url) => url.includes('offset=200'))).toBe(false)
    expect(result.licensesPagesWalked).toBe(1)
    expect(result.licensesStoppedBecause).toBe('stalled')
    expect(result.captureOutcome).toBe('partial')
    expect(result.pages?.licensesHtml).toBeTruthy()
    expect(result.pages?.historyHtml).toBeTruthy()
  })

  it('follows past an initially empty licence table, independently caps further pages, and does not click history at zero', async () => {
    const browser = new Browser((url) => ({
      fixture: url.includes('/history/') ? 'purchase-history' : 'licenses-page1',
      identity: steamId,
      mutate: (doc) => {
        if (!url.includes('/licenses/')) return
        const page = Number(new URL(url).searchParams.get('page') || '0')
        doc
          .querySelector('a.license_paginator_next')
          ?.setAttribute('href', `https://store.steampowered.com/account/licenses/?page=${page + 1}`)
        if (page === 0)
          for (const row of doc.querySelectorAll('tr')) if (!row.querySelector('th')) row.remove()
      },
    }))
    const result = await capture(browser, { maxLicensesPages: 3, maxLoadMoreClicks: 0 })
    expect(browser.loaded.filter((url) => url.includes('/licenses/'))).toHaveLength(4)
    expect(result.licensesPagesWalked).toBe(3)
    expect(result.pages?.additionalLicensesHtml).toHaveLength(3)
    expect(result.licensesStoppedBecause).toBe('cap')
    expect(result.loadMoreClicks).toBe(0)
    expect(result.historyStoppedBecause).toBe('cap')
  })

  it('uses exhaustion before the cap and cap before stalled growth in the running history loop', async () => {
    const run = async (hide: boolean) => {
      const browser = new Browser((url) => ({
        fixture: url.includes('/history/') ? 'purchase-history' : 'licenses-final-page',
        identity: steamId,
        mutate: (doc) => {
          const more = doc.querySelector<HTMLButtonElement>('#load_more_button')
          if (more && hide)
            more.onclick = () => {
              more.remove()
            }
        },
      }))
      const pending = captureSteamAccountPages(browser as unknown as BrowserWindow, { maxLoadMoreClicks: 1 })
      await vi.advanceTimersByTimeAsync(16_000)
      return pending
    }
    expect(await run(false)).toMatchObject({
      loadMoreClicks: 1,
      historyStoppedBecause: 'cap',
      historyTruncated: true,
    })
    expect(await run(true)).toMatchObject({
      loadMoreClicks: 1,
      historyStoppedBecause: 'exhausted',
      historyTruncated: false,
    })
  })

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
    expect(result.captureOutcome).toBe('captured')
    expect(result.licensesStoppedBecause).toBe('exhausted')
    expect(result.historyStoppedBecause).toBe('exhausted')
    expect(result.licensesPagesWalked).toBe(1)
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
    expect(result.licensesStoppedBecause).toBe('stalled')
    expect(result.captureOutcome).toBe('partial')
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
    expect(result.captureOutcome).toBe('failed')
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
    expect(result.licensesStoppedBecause).toBe('cap')
    expect(result.historyStoppedBecause).toBe('cap')
    expect(result.loadMoreClicks).toBe(0)
    expect(result.licensesPagesWalked).toBe(0)
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
    expect(result.historyStoppedBecause).toBe('exhausted')
    expect(result.loadMoreClicks).toBe(1)
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
    expect(result.historyStoppedBecause).toBe('stalled')
    expect(result.loadMoreClicks).toBe(1)
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
    expect(result.historyStoppedBecause).toBe('interrupted')
    expect(result.captureOutcome).toBe('partial')
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
    const stopped = await pending
    expect(stopped.pages).toBeUndefined()
    expect(stopped.captureOutcome).toBe('cancelled')
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
    expect(result.licensesStoppedBecause).toBe('failed')
  })

  it('reports licence rendering count differences as complete when contiguous ranges and visible controls reach the end', async () => {
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
    const result = await capture(browser)
    const html = result.pages!.licensesHtml! + result.pages!.additionalLicensesHtml.join('')
    expect((html.match(/license_date_col/g) ?? []).length).toBeLessThan(200)
    expect(result.licensesStoppedBecause).toBe('exhausted')
    expect(result.licensesTruncated).toBe(false)
  })

  it('ignores a stylesheet-hidden load-more control in the live DOM', async () => {
    const browser = new Browser((url) => ({
      fixture: url.includes('/history/') ? 'purchase-history' : 'licenses-final-page',
      identity: steamId,
      mutate: (doc) => {
        const more = doc.querySelector<HTMLButtonElement>('#load_more_button')
        if (more) more.parentElement!.style.display = 'none'
      },
    }))
    const result = await capture(browser)
    expect(result.historyStoppedBecause).toBe('exhausted')
    expect(result.loadMoreClicks).toBe(0)
    expect(result.pages!.historyHtml).not.toContain('load_more_button')
  })

  it('keeps an unsigned-in timeout neutral and a recognizable-page failure actionable', async () => {
    const unsigned = new Browser()
    unsigned.loadURL = async () => {
      unsigned.webContents.url = 'https://store.steampowered.com/login/'
    }
    const noSession = captureSteamAccountPages(unsigned as unknown as BrowserWindow, {
      deadline: Date.now() + 500,
    })
    await vi.advanceTimersByTimeAsync(750)
    const absent = await noSession
    expect(absent).toMatchObject({ captureOutcome: 'no-session', loadMoreClicks: 0, licensesPagesWalked: 0 })
    expect(absent.captureDetail).toBeTruthy()
    expect(absent.pages).toBeUndefined()
    const broken = new Browser()
    broken.webContents.executeJavaScript.mockResolvedValue(null)
    const failed = captureSteamAccountPages(broken as unknown as BrowserWindow)
    await vi.advanceTimersByTimeAsync(32_000)
    const failure = await failed
    expect(failure).toMatchObject({ captureOutcome: 'failed', loadMoreClicks: 0, licensesPagesWalked: 0 })
    expect(failure.captureDetail).toBeTruthy()
    expect(failure.pages).toBeUndefined()
  })
})
