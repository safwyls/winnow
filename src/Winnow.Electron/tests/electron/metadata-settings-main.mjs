import { app } from 'electron'
import { fileURLToPath } from 'node:url'

app.setAppPath(fileURLToPath(new URL('../..', import.meta.url)))
const fixture = (globalThis.__metadataFixture = {
  starts: [],
  operations: new Map(),
  state: 'running',
  message: 'Matching games with IGDB…',
  result: 0,
  refuseNextStart: false,
})
const backendFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname !== '127.0.0.1') return backendFetch(input, init)
  const snapshot = (id) => ({
    id,
    kind: 'metadata-sync',
    state: fixture.state,
    message: fixture.message,
    metadataResult: fixture.state === 'completed' ? fixture.result : null,
    updatedAt: '2026-09-30T00:00:00Z',
  })
  if (url.pathname === '/api/v1/operations/metadata-sync' && init?.method === 'POST') {
    const { operationId } = JSON.parse(init.body)
    fixture.starts.push(operationId)
    fixture.operations.set(operationId, true)
    if (fixture.refuseNextStart) {
      fixture.refuseNextStart = false
      throw Error('Private transport diagnostic after the request was accepted')
    }
    return Response.json(snapshot(operationId))
  }
  if (url.pathname.startsWith('/api/v1/operations/')) {
    const id = url.pathname.split('/').at(-1)
    if (fixture.operations.has(id)) return Response.json(snapshot(id))
  }
  if (url.pathname === '/api/v1/operations')
    return Response.json([...fixture.operations.keys()].map(snapshot))
  if (url.pathname === '/api/v1/preferences/artwork-sources')
    return Response.json([
      { id: 'steam', label: 'Steam' },
      { id: 'plugin:steamgriddb', label: 'SteamGridDB' },
      { id: 'igdb', label: 'IGDB' },
    ])
  return backendFetch(input, init)
}
await import('../../out/main/index.js')
