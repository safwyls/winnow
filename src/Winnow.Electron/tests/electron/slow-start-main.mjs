import { app } from 'electron'
import { fileURLToPath } from 'node:url'

app.setAppPath(fileURLToPath(new URL('../..', import.meta.url)))
let release
const pending = new Promise((resolve) => {
  release = resolve
})
globalThis.__slowConnection = { release }
const backendFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname === '127.0.0.1' && url.pathname === '/api/v1/events') {
    await new Promise((resolve, reject) => {
      const abort = () => reject(init.signal.reason)
      init?.signal?.addEventListener('abort', abort, { once: true })
      if (init?.signal?.aborted) abort()
      void pending.then(resolve).finally(() => init?.signal?.removeEventListener('abort', abort))
    })
  }
  return backendFetch(input, init)
}
await import('../../out/main/index.js')
