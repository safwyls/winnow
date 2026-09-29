import { describe, expect, it, vi } from 'vitest'
import { QueryClient, QueryObserver } from '@tanstack/react-query'
import { RefreshQueue, refreshJournalSnapshot, refreshSnapshots, shouldRefreshArtwork } from '../src/renderer/refresh'
describe('snapshot invalidation', () => {
  it('refreshes a journal event in loaded activity pages without reloading the timeline', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const request = vi.fn(async () => ({ ok: true, status:200, data:{sessionId:42,note:'Saved elsewhere',rating:4,revision:'new'} }))
    vi.stubGlobal('window', { winnow:{request} })
    const key=['api','activity.query','2026-09-01','2026-10-01',0]
    client.setQueryData(key, { pages:[{rows:[],next:{atUtc:'2026-09-10',id:44}},{rows:[{ownershipId:2,atUtc:'2026-09-09',session:{id:42},note:null}],next:null}],pageParams:[null,{atUtc:'2026-09-10',id:44}] })
    try {
      await refreshJournalSnapshot(client,42)
      const data=client.getQueryData<any>(key)
      expect(data.pages).toHaveLength(2)
      expect(data.pages[1].rows[0].note.note).toBe('Saved elsewhere')
      expect(data.pageParams).toEqual([null,{atUtc:'2026-09-10',id:44}])
      expect(request).toHaveBeenCalledOnce()
      expect(request).toHaveBeenCalledWith({route:'journal.get',params:{sessionId:42},body:undefined})
    } finally { client.clear();vi.unstubAllGlobals() }
  })
  it('forces image reads for explicit artwork changes and a required resync', () => {
    expect(shouldRefreshArtwork({ kind: 'library.changed', resource: 'works/7/artwork' })).toBe(true)
    expect(shouldRefreshArtwork({ kind: 'resync-required' })).toBe(true)
    expect(shouldRefreshArtwork({ kind: 'library.changed', resource: 'works/7/metadata' })).toBe(false)
    expect(shouldRefreshArtwork({ kind: 'feed.changed' })).toBe(false)
  })
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
