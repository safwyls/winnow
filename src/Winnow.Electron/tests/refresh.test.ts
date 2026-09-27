import { describe, expect, it } from 'vitest'
import { QueryClient, QueryObserver } from '@tanstack/react-query'
import { RefreshQueue, refreshSnapshots } from '../src/renderer/refresh'
describe('snapshot invalidation', () => {
  it('refetches after an event races a read started before the refresh queue', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    let reads = 0,
      resolve!: (value: string) => void
    const observer = new QueryObserver(client, {
      queryKey: ['library'],
      queryFn: async () => {
        reads++
        return reads === 1
          ? new Promise<string>((done) => {
              resolve = done
            })
          : 'committed state'
      },
    })
    const unsubscribe = observer.subscribe(() => {})
    const refreshing = refreshSnapshots(client)
    resolve('old state')
    await refreshing
    expect(reads).toBe(2)
    expect(client.getQueryData(['library'])).toBe('committed state')
    unsubscribe()
    client.clear()
  })
  it('retains changes arriving while a snapshot is in flight and coalesces a burst', async () => {
    const reads: Array<() => void> = []
    const queue = new RefreshQueue(() => new Promise<void>((resolve) => reads.push(resolve)))
    queue.request()
    queue.request()
    queue.request()
    expect(reads).toHaveLength(1)
    reads[0]()
    await Promise.resolve()
    await Promise.resolve()
    expect(reads).toHaveLength(2)
    reads[1]()
    await Promise.resolve()
    queue.dispose()
    queue.request()
    expect(reads).toHaveLength(2)
  })
})
