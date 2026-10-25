import { app, shell } from 'electron'
import { basename, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const dataArgument = process.argv.indexOf('--data-dir')
if (
  dataArgument < 0 ||
  !basename(resolve(process.argv[dataArgument + 1] ?? '')).startsWith('winnow-igdb-settings-')
)
  throw Error('IGDB verification requires its own isolated data directory.')
app.setAppPath(fileURLToPath(new URL('../..', import.meta.url)))
const fixture = (globalThis.__igdbFixture = { refuseSave: true, links: [], requests: [] })
const backendFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname === '127.0.0.1' && url.pathname === '/api/v1/connections/igdb')
    fixture.requests.push({
      method: init?.method ?? 'GET',
      path: url.pathname,
      simulatedProtectionRefusal: init?.method === 'PUT' && fixture.refuseSave,
    })
  if (
    url.hostname === '127.0.0.1' &&
    url.pathname === '/api/v1/connections/igdb' &&
    init?.method === 'PUT' &&
    fixture.refuseSave
  )
    return Response.json(2)
  const response = await backendFetch(input, init)
  if (url.hostname === '127.0.0.1' && url.pathname === '/api/v1/preferences/presentation' && response.ok) {
    const values = await response.json()
    return Response.json([
      ...values.filter((row) => row.preference !== 'LinkDestination'),
      { preference: 'LinkDestination', value: 'browser' },
    ])
  }
  return response
}
const openExternal = shell.openExternal
shell.openExternal = async (url, options) => {
  if (url === 'https://dev.twitch.tv/console/apps') {
    fixture.links.push(url)
    throw new Error('Fixture browser refusal')
  }
  return openExternal(url, options)
}
await import('../../out/main/index.js')
