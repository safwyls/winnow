import { readableWebUrl } from './security'

/** The sign-in window is a private browser, never the application's trusted renderer. */
export function steamNavigationAllowed(value: string): boolean {
  try {
    const url = new URL(value)
    return (
      url.protocol === 'https:' &&
      !url.username &&
      !url.password &&
      !url.port &&
      [
        'store.steampowered.com',
        'login.steampowered.com',
        'steamcommunity.com',
        'help.steampowered.com',
        'www.steamcommunity.com',
        'www.steampowered.com',
      ].includes(url.hostname)
    )
  } catch {
    return false
  }
}

/** Embedded challenges may belong to a third party; they never acquire a host bridge or mint access. */
export function steamFrameNavigationAllowed(value: string, mainFrame: boolean): boolean {
  return mainFrame ? steamNavigationAllowed(value) : !!readableWebUrl(value)?.startsWith('https:')
}

/** Trusted login pages can be displayed, but only noncredential store pages may be probed. */
export function steamMintAllowed(value: string): boolean {
  if (!steamNavigationAllowed(value)) return false
  const url = new URL(value)
  const path = url.pathname.toLowerCase().split('/').filter(Boolean).join('/')
  return (
    url.origin === 'https://store.steampowered.com' &&
    !['login', 'join', 'password', 'twofactor', 'mobilelogin', 'account/security'].some((prefix) =>
      path.startsWith(prefix),
    )
  )
}

export function readSteamIdentity(
  probe: unknown,
  now = Date.now(),
): { steamId: string; token: string } | null {
  if (!probe || typeof probe !== 'object') return null
  const { token, steamid } = probe as { token?: unknown; steamid?: unknown }
  if (typeof token !== 'string' || token.length > 32768) return null
  try {
    const pieces = token.split('.')
    if (pieces.length !== 3) return null
    const claims = JSON.parse(Buffer.from(pieces[1], 'base64url').toString('utf8'))
    if (
      typeof claims.sub !== 'string' ||
      !/^7656119\d{10}$/.test(claims.sub) ||
      !Number.isFinite(claims.exp) ||
      claims.exp * 1000 <= now
    )
      return null
    // The backend validates the token's remaining claims before storing it. These
    // checks bind the observed page to its credential without logging either.
    if (steamid && String(steamid) !== claims.sub)
      throw new Error('Steam returned a different account from the signed-in page. Sign in again.')
    return { steamId: claims.sub, token }
  } catch (error) {
    if (error instanceof Error && error.message.startsWith('Steam returned')) throw error
    return null
  }
}

// Ported from SteamSignInScripts.Mint. This never reads or submits a login form.
export const steamTokenProbe = `(() => {
  const path = location.pathname.toLowerCase().split('/').filter(Boolean).join('/');
  if (location.origin !== 'https://store.steampowered.com' ||
      ['login', 'join', 'password', 'twofactor', 'mobilelogin', 'account/security'].some(prefix => path.startsWith(prefix))) return null;
  let token = null, steamid = null, loggedIn = false;
  const config = document.getElementById('application_config');
  if (config) {
    try { token = JSON.parse(config.getAttribute('data-store_user_config') || '{}').webapi_token || null; } catch {}
    try { const info = JSON.parse(config.getAttribute('data-userinfo') || '{}'); steamid = info.steamid ? String(info.steamid) : null; loggedIn = !!info.logged_in; } catch {}
  }
  if (!token && typeof window.g_wapit === 'string') token = window.g_wapit;
  loggedIn ||= !!document.getElementById('account_pulldown') || !!document.querySelector('a[href*="/logout"]');
  return { token, steamid, loggedIn };
})()`
