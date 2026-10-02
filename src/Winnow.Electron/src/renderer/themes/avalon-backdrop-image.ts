import type { BackdropKey, BackdropPixels } from './avalon-backdrop-model'
import { decodeArtworkImage } from '../components/artwork-images'

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
  return source ? decodeArtworkImage(source, signal) : null
}
