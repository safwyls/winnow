import { readFile } from 'node:fs/promises'

const fixture = (globalThis.__pluginFixture = {
  plugins: JSON.parse(await readFile(process.env.WINNOW_PLUGIN_FIXTURE, 'utf8')),
  calls: [],
  failSave: false,
  failActivation: false,
  connected: false,
  holdSignIn: false,
  releaseSignIn: null,
})
const originalFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname !== '127.0.0.1' || !url.pathname.startsWith('/api/v1/connections/plugins'))
    return originalFetch(input, init)
  const path = url.pathname.slice('/api/v1/connections/plugins'.length)
  if (path === '/directory') return originalFetch(input, init)
  const body = init?.body ? JSON.parse(init.body) : undefined
  fixture.calls.push({ path, method: init?.method ?? 'GET', body })
  if (!path) return Response.json(fixture.plugins)
  const [, id, ...action] = path.split('/')
  const plugin = fixture.plugins.find((plugin) => plugin.id === id)
  const operation = action.join('/')
  if (operation === 'settings') {
    if (fixture.failSave) return Response.json({ message: 'private storage detail' }, { status: 500 })
    for (const setting of plugin.settings) {
      if (!(setting.key in body.values)) continue
      if (setting.isSecret) setting.hasStoredSecret = true
      else setting.value = body.values[setting.key]
    }
  }
  if (operation === 'enabled') {
    if (fixture.failActivation)
      return Response.json({ message: 'private activation detail' }, { status: 500 })
    plugin.enabled = body.enabled
    plugin.restartRequired = true
  }
  if (operation.startsWith('secrets/'))
    plugin.settings.find((setting) => setting.key === action[1]).hasStoredSecret = false
  if (operation === 'sign-in') {
    if (fixture.holdSignIn)
      await new Promise((done) => {
        fixture.releaseSignIn = done
      })
    return Response.json({
      challenge: {
        attemptId: 'native-plugin-attempt',
        userCode: 'ABCD-EFGH',
        verificationUrl: 'https://login.example.com/device',
        expiresAt: new Date(Date.now() + 60000).toISOString(),
        pollIntervalSeconds: 1,
      },
    })
  }
  if (operation === 'sign-in/poll') {
    plugin.accountConnected = fixture.connected
    return Response.json({ state: fixture.connected ? 2 : 0 })
  }
  if (operation === 'sign-out') plugin.accountConnected = false
  return Response.json(null)
}
await import('../../out/main/index.js')
