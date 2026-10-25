export function validateExternalUrl(value: unknown): string {
  if (typeof value !== 'string' || value.length > 4096 || /[\u0000-\u0020\u007f]/.test(value))
    throw new Error('Invalid external link')
  const url = new URL(value)
  if (url.username || url.password) throw new Error('Credentials are not allowed in links')
  const hostname = url.hostname.replace(/\.$/, '')
  if (
    (url.protocol === 'https:' || url.protocol === 'http:') &&
    hostname &&
    !['localhost', '[::1]', '[::]', '[::ffff:0:0]', '0.0.0.0'].includes(hostname) &&
    !/^127\./.test(hostname) &&
    !/^\[::ffff:7f[0-9a-f]{2}:/.test(hostname) &&
    !hostname.endsWith('.localhost')
  )
    return url.href
  if (
    url.protocol === 'steam:' &&
    !url.port &&
    ((url.hostname === 'store' && /^\/\d{1,10}\/?$/.test(url.pathname)) ||
      (url.hostname === 'nav' && /^\/games\/details\/\d{1,10}\/?$/.test(url.pathname))) &&
    !url.search &&
    !url.hash
  )
    return url.href
  if (!url.port && !url.search && !url.hash) {
    if (
      url.protocol === 'goggalaxy:' &&
      url.hostname.toLowerCase() === 'opengameview' &&
      /^\/gog_\d{1,12}$/.test(url.pathname)
    ) {
      url.hostname = 'opengameview'
      return url.href
    }
    if (url.protocol === 'com.epicgames.launcher:' && url.hostname === 'store' && url.pathname === '/library')
      return url.href
  }
  throw new Error('This link is unavailable. Game actions must use their library commands.')
}

/** Page-controlled navigation cannot invoke native launchers or application origins. */
export function readableWebUrl(value: unknown): string | null {
  try {
    const address = validateExternalUrl(value)
    return /^https?:/.test(address) ? address : null
  } catch {
    return null
  }
}

/** Reading links share the main process policy; launch/install actions use named API commands. */
export function createGameLink(label: unknown, address: unknown, detail?: string) {
  if (typeof label !== 'string' || !label.trim()) return null
  try {
    const url = validateExternalUrl(address)
    return { label, url, ...(detail ? { detail } : {}) }
  } catch {
    return null
  }
}
