import type { AccountBrowser } from './account-browser'
import type { SteamCapturedPages, SteamCaptureResult } from '../shared/bridge'
import { steamCaptureFailure } from './steam-capture-result'
import {
  steamAccountPages,
  steamCaptureLimits,
  steamCapturePage,
  steamPageStep,
  type SteamPageKind,
} from './steam-page-policy'
export { steamCapturePage } from './steam-page-policy'

type PageKind = SteamPageKind
interface PageSnapshot {
  steamId: string | null
  rows: number
  nextUrl: string | null
  hasMore: boolean
  range?: { from: number; to: number; total: number } | null
  html?: string
}
/** Every invocation checks its own document before reading any account content. */
export function steamCaptureScript(kind: PageKind, action: 'probe' | 'capture' | 'more'): string {
  return `(() => {
    if (location.origin !== 'https://store.steampowered.com' || location.pathname.replace(/^\\/+|\\/+$/g, '').toLowerCase() !== 'account/${kind}') return null;
    if (document.querySelector('input[type="password"]')) return null;
    const kind = ${JSON.stringify(kind)}, action = ${JSON.stringify(action)};
    const table = [...document.querySelectorAll(kind === 'licenses' ? 'table.account_table' : 'table.wallet_history_table')]
      .find(node => node.querySelector(kind === 'licenses' ? 'th.license_date_col' : 'thead th.wht_date'));
    if (!table) return null;
    let steamId = null;
    try { const config = document.getElementById('application_config'); const info = config && JSON.parse(config.getAttribute('data-userinfo') || '{}');
      if (info && /^7656119\\d{10}$/.test(String(info.steamid))) steamId = String(info.steamid); } catch {}
    const visible = node => { if (!node || node.disabled) return false; for (let parent = node; parent; parent = parent.parentElement) {
      const style = getComputedStyle(parent); if (style.display === 'none' || style.visibility === 'hidden') return false; } return true; };
    const next = document.querySelector('a.license_paginator_next');
    const more = [...document.querySelectorAll('#load_more_button,[id*="load_more"],[class*="load_more"],[class*="loadMore"],button,a')]
      .find(node => visible(node) && (node.id === 'load_more_button' || (node.textContent.length < 40 && /load\\s*more/i.test(node.textContent))));
    const value = { steamId, rows: table.querySelectorAll(kind === 'licenses' ? 'td.license_date_col' : 'tr.wallet_table_row').length,
      nextUrl: visible(next) ? next.href : null, hasMore: kind === 'history' && !!more };
    if (kind === 'licenses') {
      const counts = [...document.querySelectorAll('.license_paginator_ctn span')].map(node => /([\\d,]+)\\s*-\\s*([\\d,]+)\\s+of\\s+([\\d,]+)/.exec(node.textContent || '')).find(Boolean);
      value.range = counts ? { from: Number(counts[1].replaceAll(',','')), to: Number(counts[2].replaceAll(',','')), total: Number(counts[3].replaceAll(',','')) } : null;
    }
    if (action === 'more') { if (kind !== 'history' || !more) return false; more.click(); return true; }
    if (action !== 'capture') return value;
    // Transfer only parser input. Page scripts, account config, forms and session fields stay in the private browser.
    const root = document.createElement('div'); root.append(table.cloneNode(true));
    if (kind === 'licenses') for (const node of document.querySelectorAll('.license_paginator_ctn')) root.append(node.cloneNode(true));
    if (kind === 'licenses' && !value.nextUrl) for (const node of root.querySelectorAll('a.license_paginator_next')) node.remove();
    if (kind === 'licenses' && value.nextUrl && !root.querySelector('a.license_paginator_next')) {
      const marker = document.createElement('a'); marker.className = 'license_paginator_next'; root.append(marker); }
    if (kind === 'history' && value.hasMore) { const marker = document.createElement('button'); marker.id = 'load_more_button'; root.append(marker); }
    for (const node of root.querySelectorAll('script,style,input,textarea,form,iframe,object,embed')) node.remove();
    for (const node of root.querySelectorAll('*')) for (const attribute of [...node.attributes]) {
      if (['class','id'].includes(attribute.name)) continue;
      if (attribute.name === 'href') {
        const packageId = /RemoveFreeLicense\\(\\s*(\\d+)/.exec(attribute.value);
        const appId = /[?&]appid=(\\d+)/.exec(attribute.value);
        node.setAttribute('href', packageId ? 'javascript:RemoveFreeLicense(' + packageId[1] + ')' : appId ? '/?appid=' + appId[1] : '#');
      } else if (attribute.name === 'style') {
        node.setAttribute('style', /clear\\s*:\\s*both/i.test(attribute.value) ? 'clear:both' : '');
      } else node.removeAttribute(attribute.name);
    }
    value.html = '<!doctype html><html><body>' + root.innerHTML + '</body></html>';
    return value;
  })()`
}

class CaptureStopped extends Error {}
class IdentityChanged extends Error {}
export async function captureSteamAccountPages(
  browser: AccountBrowser,
  options: {
    expectedSteamId?: string
    deadline?: number
    maxLicensesPages?: number
    maxLoadMoreClicks?: number
  } = {},
): Promise<SteamCaptureResult> {
  if (browser.isDestroyed())
    return steamCaptureFailure('cancelled', 'The window closed before account pages could be captured.')
  const deadline = options.deadline ?? Date.now() + 15 * 60_000
  const { licenses: licensesCap, history: historyCap } = steamCaptureLimits(options)
  const pages: SteamCapturedPages = {
    additionalLicensesHtml: [],
    capturedAt: new Date().toISOString(),
    source: 0,
    steamId: null,
  }
  let observed = false,
    generation = 0,
    licensesTruncated = false,
    historyTruncated = false
  let licensesStoppedBecause: SteamCaptureResult['licensesStoppedBecause'],
    historyStoppedBecause: SteamCaptureResult['historyStoppedBecause'],
    licensesPagesWalked = 0,
    loadMoreClicks = 0,
    interrupted = false
  const notes: string[] = []
  const navigation = (_event: unknown, _url: string, _inPlace: boolean, main: boolean) => {
    if (main !== false) generation++
  }
  browser.webContents.on('did-start-navigation', navigation)
  let stop!: (reason: Error) => void
  const stopped = new Promise<never>((_resolve, reject) => {
    stop = reject
  })
  const watch = setInterval(() => {
    if (browser.isDestroyed()) stop(new CaptureStopped('The capture window closed.'))
    else if (Date.now() >= deadline) stop(new CaptureStopped('The capture reached its time limit.'))
  }, 250)
  const check = () => {
    if (browser.isDestroyed() || Date.now() >= deadline)
      throw new CaptureStopped('The capture ended before every page was read.')
  }
  const bounded = async <T>(work: Promise<T>) => {
    const result = await Promise.race([work, stopped])
    check()
    return result
  }
  const pause = () => bounded(new Promise<void>((resolve) => setTimeout(resolve, 250)))
  const sameIdentity = (before: string | null, after: string | null) => {
    if (
      before !== after ||
      (options.expectedSteamId !== undefined && before !== options.expectedSteamId) ||
      (observed && before !== pages.steamId)
    )
      throw new IdentityChanged()
    pages.steamId = before
    observed = true
  }
  const visit = async (url: string, kind: PageKind) => {
    check()
    if (steamCapturePage(url) !== kind) throw new Error('Steam offered an unexpected page; it was not read.')
    await bounded(browser.loadURL(url))
    while (browser.webContents.isLoading() || steamCapturePage(browser.webContents.getURL()) !== kind)
      await pause()
    const pageGeneration = generation,
      pageUrl = browser.webContents.getURL()
    const execute = async (action: 'probe' | 'capture' | 'more') => {
      check()
      const valid = () =>
        !browser.isDestroyed() &&
        generation === pageGeneration &&
        !browser.webContents.isLoading() &&
        browser.webContents.getURL() === pageUrl
      if (!valid()) throw new Error('The Steam page changed during capture.')
      const result = await bounded(browser.webContents.executeJavaScript(steamCaptureScript(kind, action)))
      if (!valid()) throw new Error('The Steam page changed during capture.')
      return result as PageSnapshot | boolean | null
    }
    let probe = (await execute('probe')) as PageSnapshot | null
    const readyDeadline = Math.min(deadline, Date.now() + 15_000)
    while (!probe && Date.now() < readyDeadline) {
      await pause()
      probe = (await execute('probe')) as PageSnapshot | null
    }
    if (!probe) throw new Error('Steam did not display a recognizable account table.')
    browser.setInputEnabled?.(false)
    return { probe, execute }
  }
  try {
    try {
      let url: string = steamAccountPages.licenses
      let covered = 0,
        rows = 0,
        rowsBefore = -1,
        expectedTotal: number | undefined,
        contiguous = true
      const visited = new Set<string>()
      for (let followed = 0; ; followed++) {
        if (visited.has(url)) {
          licensesTruncated = true
          licensesStoppedBecause = 'stalled'
          break
        }
        visited.add(url)
        browser.setTitle(`Reading Steam licences (${followed + 1} of at most ${licensesCap + 1}) · Winnow`)
        if (followed > 0) licensesPagesWalked++
        const { probe, execute } = await visit(url, 'licenses')
        const capture = (await execute('capture')) as PageSnapshot | null
        if (!capture?.html) throw new Error('The licence page could not be read completely.')
        sameIdentity(probe.steamId, capture.steamId)
        if (Buffer.byteLength(capture.html) > 64 * 1024 * 1024)
          throw new Error('The licence page is too large to capture.')
        if (Buffer.byteLength(JSON.stringify(pages)) + Buffer.byteLength(capture.html) > 120 * 1024 * 1024)
          throw new Error('The capture reached its size limit.')
        if (!pages.licensesHtml) pages.licensesHtml = capture.html
        else pages.additionalLicensesHtml.push(capture.html)
        rows += capture.rows
        if (capture.range) {
          const { from, to, total } = capture.range
          if (
            from < 1 ||
            to < from ||
            to > total ||
            from > covered + 1 ||
            (expectedTotal !== undefined && total !== expectedTotal)
          )
            contiguous = false
          covered = Math.max(covered, to)
          expectedTotal = total
        } else if (followed > 0 || capture.nextUrl) contiguous = false
        licensesTruncated =
          !!capture.nextUrl || !contiguous || (expectedTotal !== undefined && covered !== expectedTotal)
        const decision = steamPageStep(licensesPagesWalked, rowsBefore, rows, !!capture.nextUrl, licensesCap)
        if (decision !== 'continue') {
          licensesStoppedBecause = decision === 'exhausted' && licensesTruncated ? 'stalled' : decision
          break
        }
        if (!capture.nextUrl || steamCapturePage(capture.nextUrl) !== 'licenses')
          throw new Error('Steam offered an unexpected licence page; it was not read.')
        rowsBefore = rows
        url = capture.nextUrl
      }
    } catch (failure) {
      if (failure instanceof IdentityChanged || failure instanceof CaptureStopped) throw failure
      licensesTruncated = true
      licensesStoppedBecause = 'failed'
      notes.push('Some licence pages could not be captured.')
    }
    try {
      browser.setTitle('Reading Steam purchase history · Winnow')
      const { probe, execute } = await visit(steamAccountPages.history, 'history')
      let latest = probe,
        rowsBefore = -1
      for (;;) {
        const decision = steamPageStep(loadMoreClicks, rowsBefore, latest.rows, latest.hasMore, historyCap)
        if (decision !== 'continue') {
          historyTruncated = decision !== 'exhausted'
          historyStoppedBecause = decision
          break
        }
        browser.setTitle(
          `Loading Steam purchase history (${loadMoreClicks + 1} of at most ${historyCap}) · Winnow`,
        )
        if (!(await execute('more'))) {
          historyTruncated = true
          historyStoppedBecause = 'stalled'
          break
        }
        loadMoreClicks++
        const growthDeadline = Math.min(deadline, Date.now() + 15_000),
          before = latest.rows
        rowsBefore = before
        do {
          await pause()
          const next = (await execute('probe')) as PageSnapshot | null
          if (!next) throw new Error('The purchase-history page changed during capture.')
          sameIdentity(probe.steamId, next.steamId)
          latest = next
        } while (latest.rows <= before && latest.hasMore && Date.now() < growthDeadline)
      }
      const capture = (await execute('capture')) as PageSnapshot | null
      if (!capture?.html) throw new Error('The purchase-history page could not be read completely.')
      sameIdentity(probe.steamId, capture.steamId)
      if (
        Buffer.byteLength(capture.html) > 64 * 1024 * 1024 ||
        Buffer.byteLength(JSON.stringify(pages)) + Buffer.byteLength(capture.html) > 128 * 1024 * 1024
      )
        throw new Error('The purchase-history page is too large to capture.')
      pages.historyHtml = capture.html
      historyTruncated ||= capture.hasMore
      historyStoppedBecause ??= historyTruncated ? 'stalled' : 'exhausted'
    } catch (failure) {
      if (failure instanceof IdentityChanged || failure instanceof CaptureStopped) throw failure
      historyTruncated = true
      historyStoppedBecause = 'failed'
      notes.push('Purchase history could not be captured completely.')
    }
  } catch (failure) {
    if (failure instanceof IdentityChanged)
      return steamCaptureFailure(
        'failed',
        'Steam account identity changed or could not be confirmed. These pages were discarded.',
      )
    licensesTruncated ||= !pages.licensesHtml
    historyTruncated ||= !pages.historyHtml
    interrupted = failure instanceof CaptureStopped
    licensesStoppedBecause ??= 'interrupted'
    historyStoppedBecause ??= 'interrupted'
    notes.push('Capture stopped before every page was read.')
  } finally {
    clearInterval(watch)
    browser.webContents.off('did-start-navigation', navigation)
  }
  const any = !!pages.licensesHtml || !!pages.historyHtml
  return {
    captureOutcome: any
      ? licensesTruncated || historyTruncated
        ? 'partial'
        : 'captured'
      : interrupted
        ? browser.isDestroyed()
          ? 'cancelled'
          : 'no-session'
        : 'failed',
    licensesStoppedBecause,
    historyStoppedBecause,
    licensesPagesWalked,
    loadMoreClicks,
    ...(any ? { pages } : {}),
    licensesTruncated,
    historyTruncated,
    captureDetail: any
      ? [
          ...notes,
          licensesTruncated || historyTruncated
            ? 'This capture is incomplete. Review it before importing.'
            : 'Account pages captured. Review them before importing.',
        ].join(' ')
      : 'No account pages were captured. You can try again or import saved pages.',
  }
}
