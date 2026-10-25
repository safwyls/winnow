import { validateExternalUrl } from './security'
import type { LinkOpenResult } from '../shared/bridge'

/** Only an unambiguous Steam app page can be translated into a native store target. */
export function steamStoreTarget(address: string): string | null {
  let url: URL
  try {
    url = new URL(address)
  } catch {
    return null
  }
  if (
    url.protocol !== 'https:' ||
    url.hostname !== 'store.steampowered.com' ||
    url.port ||
    url.username ||
    url.password ||
    url.search ||
    url.hash
  )
    return null
  const parts = url.pathname.split('/').filter(Boolean)
  if (
    (parts.length !== 2 && parts.length !== 3) ||
    parts[0] !== 'app' ||
    !/^[1-9]\d{0,9}$/.test(parts[1]) ||
    Number(parts[1]) > 4294967295
  )
    return null
  return `steam://store/${parts[1]}`
}

export async function routeLink(
  address: string,
  destination: string | null | undefined,
  adapters: {
    inApp(url: string): Promise<void | boolean>
    external(url: string): Promise<void | boolean>
    hasSteam(): boolean
  },
): Promise<LinkOpenResult> {
  let url: string
  try {
    url = validateExternalUrl(address)
  } catch {
    return { opened: false, message: 'This link is unavailable.' }
  }
  const failed = { opened: false, message: 'Could not open this link. Try again.' }
  try {
    if (!/^https?:/.test(url)) return (await adapters.external(url)) === false ? failed : { opened: true }
    let fallback: string | undefined
    if (destination === 'store') {
      const native = steamStoreTarget(url)
      try {
        if (native && adapters.hasSteam() && (await adapters.external(native)) !== false)
          return { opened: true }
      } catch {}
      fallback = 'This page cannot open in your store client. Opened in your browser.'
    } else if (destination !== 'browser') {
      try {
        if ((await adapters.inApp(url)) !== false) return { opened: true }
      } catch {}
      fallback = 'This page cannot open in Winnow. Opened in your browser.'
    }
    if ((await adapters.external(url)) === false) return failed
    return { opened: true, ...(fallback ? { message: fallback } : {}) }
  } catch {
    return failed
  }
}
