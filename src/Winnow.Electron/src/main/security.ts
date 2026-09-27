import { isAbsolute, relative, resolve, sep } from 'node:path'

export function containedPath(root: string, relativePath: string): string {
  if (!relativePath || relativePath.includes('\\') || relativePath.includes('\0') || isAbsolute(relativePath))
    throw new Error('Invalid package path')
  const path = resolve(root, relativePath)
  const difference = relative(resolve(root), path)
  if (!difference || difference === '..' || difference.startsWith(`..${sep}`) || isAbsolute(difference))
    throw new Error('Path leaves its package')
  return path
}

export function validateExternalUrl(value: unknown): string {
  if (typeof value !== 'string' || value.length > 4096 || /[\u0000-\u0020]/.test(value))
    throw new Error('Invalid external link')
  const url = new URL(value)
  if (url.username || url.password) throw new Error('Credentials are not allowed in links')
  if (
    url.protocol === 'https:' &&
    url.hostname &&
    !['localhost', '127.0.0.1', '[::1]'].includes(url.hostname)
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
  throw new Error('Only HTTPS and Steam store or game-details links may be opened')
}

export function trustedRendererUrl(value: string, developmentOrigin?: string): boolean {
  try {
    const url = new URL(value)
    if (developmentOrigin)
      return (
        url.origin === new URL(developmentOrigin).origin &&
        url.pathname === '/' &&
        !url.username &&
        !url.password
      )
    return (
      url.protocol === 'winnow-app:' &&
      url.host === 'app' &&
      url.pathname === '/index.html' &&
      !url.search &&
      !url.username &&
      !url.password
    )
  } catch {
    return false
  }
}

export function contentSecurityPolicy(developmentOrigin?: string): string {
  const development = developmentOrigin
    ? ` ${new URL(developmentOrigin).origin} ${new URL(developmentOrigin).origin.replace('http:', 'ws:')}`
    : ''
  return `default-src 'none'; script-src 'self' winnow-theme:${developmentOrigin ? " 'unsafe-inline'" : ''}; style-src 'self' 'unsafe-inline' winnow-theme:; img-src 'self' data: blob: winnow-theme:; font-src 'self' data: winnow-theme:; connect-src 'self' winnow-theme:${development}; object-src 'none'; base-uri 'none'; frame-src 'none'; form-action 'none'`
}
