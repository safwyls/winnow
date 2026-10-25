import type { QueryClient } from '@tanstack/react-query'

const primaryKeys = [
  ['api', 'library.get'],
  ['api', 'feed.get'],
  ['api', 'library.workspace', undefined],
  ['api', 'preferences.presentation.get', undefined],
  ['api', 'setup.get', undefined],
]
/** An invalidation can retire a refetch promise before its replacement has published. */
export function primarySnapshotVersions(client: QueryClient) {
  return primaryKeys.map((key) => client.getQueryState(key)?.dataUpdateCount ?? 0)
}
export function waitForPrimarySnapshots(client: QueryClient, previous?: number[]): Promise<void> {
  return new Promise((resolve, reject) => {
    let unsubscribe = () => {},
      queued = false
    const check = () => {
      queued = false
      const states = primaryKeys.map((key) => client.getQueryState(key))
      if (states.some((state) => state?.status === 'error' && state.fetchStatus === 'idle')) {
        unsubscribe()
        reject(new Error('The library could not be prepared.'))
      } else if (
        states.every(
          (state, index) =>
            state?.status === 'success' &&
            state.fetchStatus === 'idle' &&
            (!previous || state.dataUpdateCount > previous[index]),
        )
      ) {
        unsubscribe()
        if ((states[1]?.data as { failed?: boolean })?.failed)
          reject(new Error('Recommendations could not be prepared.'))
        else resolve()
      }
    }
    unsubscribe = client.getQueryCache().subscribe((event) => {
      if (event.type === 'removed' && primaryKeys.some((key) => key[1] === event.query.queryKey[1])) {
        unsubscribe()
        reject(new Error('Preparation was closed.'))
        return
      }
      if (!queued) {
        queued = true
        queueMicrotask(check)
      }
    })
    check()
  })
}
