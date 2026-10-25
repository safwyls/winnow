import { QueryClient } from '@tanstack/react-query'
import { describe, it, expect } from 'vitest'
import { primarySnapshotVersions, waitForPrimarySnapshots } from '../src/renderer/startup/readiness'

const keys = [
  ['api', 'library.get'],
  ['api', 'feed.get'],
  ['api', 'library.workspace', undefined],
  ['api', 'preferences.presentation.get', undefined],
  ['api', 'setup.get', undefined],
]
function readyClient() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  for (const key of keys) client.setQueryData(key, key[1] === 'feed.get' ? { failed: false } : {})
  return client
}
describe('published startup readiness', () => {
  it('a warm cancelled read cannot reveal its reverted old snapshot before a replacement publishes', async () => {
    const client = readyClient(),
      previous = primarySnapshotVersions(client),
      key = keys[1]
    for (const current of keys.filter((value) => value !== key)) client.setQueryData(current, {})
    const retired = client.fetchQuery({ queryKey: key, queryFn: () => new Promise(() => {}) }).catch(() => {})
    await client.cancelQueries({ queryKey: key })
    await retired
    let ready = false,
      release!: (value: unknown) => void
    const barrier = waitForPrimarySnapshots(client, previous).then(() => {
      ready = true
    })
    await Promise.resolve()
    expect(client.getQueryState(key)?.status).toBe('success')
    expect(ready).toBe(false)
    const replacement = client.fetchQuery({
      queryKey: key,
      queryFn: () =>
        new Promise((resolve) => {
          release = resolve
        }),
    })
    await Promise.resolve()
    expect(ready).toBe(false)
    release({ failed: false })
    await replacement
    await barrier
    expect(ready).toBe(true)
    client.clear()
  })
  it('waits for a cancelled initial primary read replacement instead of its retired promise', async () => {
    const client = readyClient(),
      key = keys[1]
    client.removeQueries({ queryKey: key })
    const retired = client
      .fetchQuery({
        queryKey: key,
        queryFn: ({ signal }) =>
          new Promise((_resolve, reject) => signal.addEventListener('abort', () => reject(signal.reason))),
      })
      .catch(() => {})
    await client.cancelQueries({ queryKey: key })
    await retired
    let ready = false,
      release!: (value: unknown) => void
    const barrier = waitForPrimarySnapshots(client).then(() => {
      ready = true
    })
    const replacement = client.fetchQuery({
      queryKey: key,
      queryFn: () =>
        new Promise((resolve) => {
          release = resolve
        }),
    })
    await Promise.resolve()
    expect(ready).toBe(false)
    release({ failed: false })
    await replacement
    await barrier
    expect(ready).toBe(true)
    client.clear()
  })
  it.each(keys.map((key) => [key[1], key] as const))(
    'keeps stale %s data covered until the warm refresh publishes',
    async (_route, key) => {
      const client = readyClient()
      let release!: (value: unknown) => void,
        ready = false
      const read = client.fetchQuery({
        queryKey: key,
        queryFn: () =>
          new Promise((resolve) => {
            release = resolve
          }),
      })
      const barrier = waitForPrimarySnapshots(client).then(() => {
        ready = true
      })
      await Promise.resolve()
      expect(ready).toBe(false)
      release({ failed: false })
      await read
      await barrier
      expect(ready).toBe(true)
      client.clear()
    },
  )
  it('does not wait for optional recommendation shelves or artwork', async () => {
    const client = readyClient()
    void client
      .fetchQuery({ queryKey: ['api', 'feed.supplement', 1], queryFn: () => new Promise(() => {}) })
      .catch(() => {})
    void client
      .fetchQuery({ queryKey: ['artwork-image', 1], queryFn: () => new Promise(() => {}) })
      .catch(() => {})
    await waitForPrimarySnapshots(client)
    client.clear()
  })
  it('refuses both hard read failures and the primary feed soft-failure flag', async () => {
    const client = readyClient()
    await client
      .fetchQuery({ queryKey: keys[0], queryFn: () => Promise.reject(Error('fixture')) })
      .catch(() => {})
    await expect(waitForPrimarySnapshots(client)).rejects.toThrow('could not be prepared')
    client.setQueryData(keys[0], {})
    client.setQueryData(keys[1], { failed: true })
    await expect(waitForPrimarySnapshots(client)).rejects.toThrow('Recommendations')
    client.clear()
  })
  it('removes a pending readiness subscription when the library scope closes', async () => {
    const client = readyClient()
    client.removeQueries({ queryKey: keys[1] })
    const barrier = waitForPrimarySnapshots(client)
    client.clear()
    await expect(barrier).rejects.toThrow('closed')
  })
})
