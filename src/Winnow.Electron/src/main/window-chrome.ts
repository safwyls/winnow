export const TITLEBAR_HEIGHT = 32

export function titlebarOptions(platform: string) {
  return platform === 'win32' || platform === 'linux'
    ? {
        titleBarStyle: 'hidden' as const,
        titleBarOverlay: { color: '#00000000', symbolColor: '#f1ece4', height: TITLEBAR_HEIGHT },
      }
    : {}
}

export function applyWindowChrome(
  window: { isDestroyed(): boolean; setTitleBarOverlay(options: { symbolColor: string }): void },
  platform: string,
  value: unknown,
): boolean {
  if (
    !value ||
    typeof value !== 'object' ||
    Array.isArray(value) ||
    Object.keys(value).some((key) => key !== 'text') ||
    !('text' in value) ||
    typeof value.text !== 'string' ||
    !/^#[0-9a-f]{6}$/i.test(value.text)
  )
    throw Error('Invalid window chrome.')
  if (window.isDestroyed() || (platform !== 'win32' && platform !== 'linux')) return false
  window.setTitleBarOverlay({ symbolColor: value.text })
  return true
}
