import type { QueryClient } from '@tanstack/react-query'

interface PendingWrites {
  count: number
  settled: Promise<void>
  release(): void
}
const pending = new WeakMap<QueryClient, PendingWrites>()

/** A refresh must observe the final committed membership, including compensating writes. */
export function beginLibraryWrite(client: QueryClient): () => void {
  let writes = pending.get(client)
  if (!writes) {
    let release!: () => void
    const settled = new Promise<void>((resolve) => {
      release = resolve
    })
    writes = { count: 0, settled, release }
    pending.set(client, writes)
    // Only reads predating the first write are stale. Later refreshes wait below.
    void client.cancelQueries({ queryKey: ['api', 'library.get'] })
  }
  writes.count++
  let completed = false
  return () => {
    if (completed) return
    completed = true
    if (--writes.count === 0) {
      pending.delete(client)
      writes.release()
    }
  }
}

export async function waitForLibraryWrites(client: QueryClient, signal: AbortSignal): Promise<void> {
  signal.throwIfAborted()
  while (true) {
    const writes = pending.get(client)
    if (!writes) return
    await new Promise<void>((resolve, reject) => {
      const aborted = () => {
        cleanup()
        reject(signal.reason)
      }
      const cleanup = () => signal.removeEventListener('abort', aborted)
      signal.addEventListener('abort', aborted, { once: true })
      void writes.settled.then(() => {
        cleanup()
        resolve()
      })
    })
    signal.throwIfAborted()
  }
}
