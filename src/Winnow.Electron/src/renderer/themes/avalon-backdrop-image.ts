import type { BackdropKey, BackdropPixels } from './avalon-backdrop-model'

/** Each visible transition layer owns its URL; aborted and detached layers release it immediately. */
export async function loadBackdropImage(
  key: BackdropKey,
  width: number,
  signal: AbortSignal,
): Promise<BackdropPixels | null> {
  signal.throwIfAborted()
  const requestId = crypto.randomUUID().replaceAll('-', '')
  const cancel = () => {
    void window.winnow.cancelRequest?.(requestId).catch(() => undefined)
  }
  signal.addEventListener('abort', cancel, { once: true })
  let source: string | null
  try {
    source = await window.winnow.artwork(key.provider, key.id, width, requestId)
  } finally {
    signal.removeEventListener('abort', cancel)
  }
  signal.throwIfAborted()
  if (!source?.startsWith('data:image/png;base64,')) return null
  const bytes = Uint8Array.from(atob(source.slice('data:image/png;base64,'.length)), (character) =>
    character.charCodeAt(0),
  )
  const url = URL.createObjectURL(new Blob([bytes], { type: 'image/png' }))
  const image = new Image()
  let released = false
  const dispose = () => {
    if (released) return
    released = true
    image.src = ''
    URL.revokeObjectURL(url)
  }
  signal.addEventListener('abort', dispose, { once: true })
  try {
    image.src = url
    await image.decode()
    signal.throwIfAborted()
    if (!image.naturalWidth || !image.naturalHeight) {
      dispose()
      return null
    }
    return { source: url, width: image.naturalWidth, height: image.naturalHeight, dispose }
  } catch {
    dispose()
    return null
  } finally {
    signal.removeEventListener('abort', dispose)
  }
}
