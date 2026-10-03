import { request } from '../api/client'
import type { BackdropSelection } from './avalon-backdrop-model'

export async function resolveBackdropSelection(
  workId: number,
  aspectRatio: number,
  coverWorkId: number | undefined,
  signal: AbortSignal,
): Promise<BackdropSelection> {
  const selectionRead = request<BackdropSelection>(
    'artwork.backdrop',
    { workId, aspectRatio },
    undefined,
    signal,
  )
  if (coverWorkId === undefined || coverWorkId === workId) return selectionRead
  // Header preferences change the cover fallback; hero metadata belongs to the canonical work.
  try {
    const [selection, cover] = await Promise.all([
      selectionRead,
      request<{ current: { previewKey: BackdropSelection['coverKey'] } | null }>(
        'artwork.get',
        { workId: coverWorkId, slot: 'Cover' },
        undefined,
        signal,
      ),
    ])
    return { ...selection, coverKey: cover.current?.previewKey ?? null }
  } catch {
    signal.throwIfAborted()
    return selectionRead
  }
}
