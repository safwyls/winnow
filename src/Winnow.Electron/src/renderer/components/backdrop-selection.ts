import { request } from '../api/client'
import { normalizeBackdropSelection, type BackdropSelection } from './backdrop-model'

export async function resolveBackdropSelection(
  workId: number,
  aspectRatio: number,
  coverWorkId: number | undefined,
  signal: AbortSignal,
): Promise<BackdropSelection> {
  // Consumers read candidates without further checks; an unexpected payload must not throw in them.
  const selectionRead = request<unknown>('artwork.backdrop', { workId, aspectRatio }, undefined, signal).then(
    normalizeBackdropSelection,
  )
  if (coverWorkId === undefined || coverWorkId === workId) return selectionRead
  // Header preferences change the cover fallback; hero metadata belongs to the canonical work.
  try {
    const [selection, cover] = await Promise.all([
      selectionRead,
      request<{ current?: { previewKey?: BackdropSelection['coverKey'] } | null } | null>(
        'artwork.get',
        { workId: coverWorkId, slot: 'Cover' },
        undefined,
        signal,
      ),
    ])
    return normalizeBackdropSelection({ ...selection, coverKey: cover?.current?.previewKey ?? null })
  } catch {
    signal.throwIfAborted()
    return selectionRead
  }
}
