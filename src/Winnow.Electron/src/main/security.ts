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

export { validateExternalUrl, readableWebUrl } from '../shared/external-links'

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
