import { app, shell } from 'electron'
import { basename } from 'node:path'

const fixture = (globalThis.__epicWorkflow = {
  challenges: [],
  completions: [],
  opened: [],
  forbidden: [],
  connected: false,
})
app.on('session-created', (profile) => {
  if (basename(profile.getStoragePath() ?? '') !== 'epic') return
  for (const scheme of ['https', 'http'])
    profile.protocol.handle(scheme, (request) => {
      const url = new URL(request.url)
      if (url.protocol !== 'https:' || url.hostname !== 'www.epicgames.com') {
        fixture.forbidden.push(`${url.protocol}//${url.host}${url.pathname}`)
        return new Response('Fixture blocked network', { status: 403 })
      }
      if (url.pathname === '/id/api/redirect')
        return Response.json({ authorizationCode: null, exchangeCode: null })
      return new Response(
        `<!doctype html><html><body style="font:24px system-ui;padding:32px">
      <h1>Epic workflow fixture</h1>
      <a href="https://localhost/launcher/authorized?code=unusable&state=wrong">Finish without a usable response</a>
      </body></html>`,
        { headers: { 'Content-Type': 'text/html' } },
      )
    })
})
shell.openExternal = async (address) => {
  fixture.opened.push(address)
}
const originalFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname !== '127.0.0.1') return originalFetch(input, init)
  if (url.pathname === '/api/v1/connections/stores/epic/sign-in') {
    const response = await originalFetch(input, init)
    if (response.ok) fixture.challenges.push(await response.clone().json())
    return response
  }
  if (url.pathname === '/api/v1/connections/stores/epic/sign-in/complete') {
    const body = JSON.parse(init.body)
    if (body.code !== 'PRIVATE-FIXTURE-MANUAL-CODE') throw Error('Unexpected fixture code')
    fixture.completions.push(body)
    fixture.connected = true
    return Response.json({
      succeeded: true,
      failure: 0,
      persisted: false,
      displayName: 'Fixture Epic account',
    })
  }
  if (url.pathname === '/api/v1/connections/stores') {
    const response = await originalFetch(input, init)
    if (!response.ok) return response
    const snapshot = await response.json()
    snapshot.epic = fixture.connected ? { isLive: true, displayName: 'Fixture Epic account' } : null
    return Response.json(snapshot)
  }
  return originalFetch(input, init)
}
await import('../../out/main/index.js')
