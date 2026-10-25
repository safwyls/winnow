import type { AccountBrowser } from './account-browser'
import { readableWebUrl } from './security'
import { steamFrameNavigationAllowed, steamNavigationAllowed } from './steam-auth-policy'

export function steamPopupDestination(
  value: string,
): { kind: 'navigate' | 'external'; url: string } | { kind: 'block' } {
  if (steamNavigationAllowed(value)) return { kind: 'navigate', url: value }
  const url = readableWebUrl(value)
  return url ? { kind: 'external', url } : { kind: 'block' }
}

/** A provider popup never creates an unguarded child or receives the application's preload. */
export function installSteamBrowserPolicy(
  browser: AccountBrowser,
  openExternal: (url: string) => Promise<unknown>,
) {
  browser.webContents.setWindowOpenHandler(({ url }) => {
    const target = steamPopupDestination(url)
    if (!browser.isDestroyed()) {
      if (target.kind === 'navigate') void browser.loadURL(target.url).catch(() => {})
      else if (target.kind === 'external') void openExternal(target.url).catch(() => {})
    }
    return { action: 'deny' }
  })
  browser.on('page-title-updated', (event) => event.preventDefault())
  const navigate = (event: { preventDefault(): void }, url: string) => {
    if (!steamNavigationAllowed(url)) event.preventDefault()
  }
  browser.webContents.on('will-navigate', navigate)
  browser.webContents.on('will-redirect', navigate)
  browser.webContents.on('will-frame-navigate', (event) => {
    if (!steamFrameNavigationAllowed(event.url, event.isMainFrame)) event.preventDefault()
  })
  browser.webContents.on('will-attach-webview', (event) => event.preventDefault())
}
