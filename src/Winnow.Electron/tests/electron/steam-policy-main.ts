import { app, BrowserWindow, session } from 'electron'
import { mkdirSync } from 'node:fs'
import { isAbsolute, join } from 'node:path'
import { createAccountBrowser } from '../../src/main/account-browser'
import { installSteamBrowserPolicy } from '../../src/main/steam-browser-policy'
import { captureSteamAccountPages } from '../../src/main/steam-capture'

const directory = process.argv[process.argv.indexOf('--data-dir') + 1]
if (!directory || !isAbsolute(directory) || !directory.includes('winnow-electron-steam-policy-'))
  throw Error('An isolated Steam policy fixture directory is required.')
mkdirSync(directory, { recursive: true })
app.setPath('userData', directory)
app.setPath('sessionData', join(directory, 'chromium'))
const state = {
  external: [] as string[],
  requests: [] as string[],
  forbidden: [] as string[],
  accountRequests: [] as { url: string; cache: string | null; hasSession: boolean }[],
}
const links = `<h1>Steam policy fixture</h1><a target="_blank" href="https://help.steampowered.com/en/">Valve help</a>
<a target="_blank" href="https://example.com/support">External help</a>
<a target="_blank" href="winnow-app://app/index.html">Application URL</a>
<a target="_blank" href="http://127.0.0.1:4400/private">Local service</a>
<a target="_blank" href="steam://run/10">Launcher command</a>`
app.whenReady().then(async () => {
  const owner = new BrowserWindow({
    show: false,
    fullscreen: process.argv.includes('--fullscreen'),
    webPreferences: { sandbox: true, contextIsolation: true, nodeIntegration: false },
  })
  await owner.loadURL('data:text/html,<h1>Steam policy fixture owner</h1>')
  const profile = session.fromPartition('steam-policy-fixture')
  await profile.cookies.set({
    url: 'https://store.steampowered.com/',
    name: 'winnow-fixture-session',
    value: 'test-only',
    secure: true,
    httpOnly: true,
  })
  profile.setPermissionRequestHandler((_c, _p, callback) => callback(false))
  profile.setPermissionCheckHandler(() => false)
  for (const scheme of ['https', 'http'])
    profile.protocol.handle(scheme, async (request) => {
      const url = new URL(request.url)
      state.requests.push(request.url)
      if (url.hostname === 'store.steampowered.com' && url.pathname.startsWith('/account/'))
        state.accountRequests.push({
          url: request.url,
          cache: request.headers.get('cache-control'),
          hasSession:
            browser.webContents.session === profile &&
            (
              await browser.webContents.session.cookies.get({
                url: request.url,
                name: 'winnow-fixture-session',
              })
            ).some((cookie) => cookie.value === 'test-only'),
        })
      if (!['store.steampowered.com', 'help.steampowered.com'].includes(url.hostname)) {
        state.forbidden.push(request.url)
        return new Response('No external network', { status: 403 })
      }
      if (url.pathname === '/account/licenses/')
        return new Response(null, {
          status: 302,
          headers: { Location: 'https://store.steampowered.com/account/LICENSES?offset=0' },
        })
      let content = links
      if (url.pathname.toLowerCase() === '/account/licenses') {
        const next = url.searchParams.get('offset') === '0'
        content = `<div class="license_paginator_ctn"><span>Showing licenses ${next ? '1-1' : '2-2'} of 2</span>${next ? '<a class="license_paginator_next" href="https://store.steampowered.com/account/licenses?offset=1">Next</a>' : ''}</div>
      <table class="account_table"><tr><th class="license_date_col">Date</th><th>Item</th><th>Acquisition Method</th></tr><tr><td class="license_date_col">Sep 28, 2026</td><td>Fixture ${next ? 'one' : 'two'}</td><td>Steam Store</td></tr></table>`
      }
      if (url.pathname === '/account/history/')
        content =
          '<table class="wallet_history_table"><thead><tr><th class="wht_date">Date</th></tr></thead><tbody><tr class="wallet_table_row"><td>Fixture history</td></tr></tbody></table>'
      return new Response(
        `<!doctype html><html><body style="font:24px system-ui;padding:32px;display:grid;gap:20px">${content}<div id="application_config" data-userinfo='{"steamid":"76561198000000001"}' data-store_user_config='{"webapi_token":"private-fixture-token"}'></div></body></html>`,
        { headers: { 'Content-Type': 'text/html' } },
      )
    })
  const browser = createAccountBrowser(owner, profile, 'Steam policy fixture')
  installSteamBrowserPolicy(browser, async (url) => {
    state.external.push(url)
  })
  Object.assign(globalThis, {
    steamPolicyTest: { browser, state, capture: () => captureSteamAccountPages(browser) },
  })
  await browser.loadURL('https://store.steampowered.com/login/')
})
app.on('window-all-closed', () => app.quit())
