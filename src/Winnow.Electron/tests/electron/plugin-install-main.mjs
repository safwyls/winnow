const fixture = (globalThis.__pluginInstallFixture = {
  hold: false,
  outcome: 0,
  starts: [],
  refreshes: [],
  operations: {},
  plugins: [],
  finish(id, outcome = fixture.outcome) {
    const operation = fixture.operations[id]
    const pluginId = operation.pluginId
    operation.state = 'completed'
    operation.pluginResult = {
      pluginId,
      outcome,
      message: outcome === 2 ? 'The download failed. Try again.' : 'Plugin settings are ready.',
    }
    if (outcome !== 2 && !fixture.plugins.some((plugin) => plugin.id === pluginId))
      fixture.plugins.push({
        id: pluginId,
        name: pluginId === 'psn' ? 'PlayStation' : 'Xbox',
        description: 'Import your game library.',
        version: '1.0.0',
        capabilities: 'Library sources',
        enabled: true,
        isLoaded: true,
        restartRequired: false,
        status: 'Active',
        canConfigure: true,
        hasAccount: false,
        accountConnected: false,
        settings: [
          {
            key: 'label',
            label: 'Account label',
            value: '',
            isSecret: false,
            isRequired: false,
            isBoolean: false,
            isAdvanced: false,
            hasStoredSecret: false,
          },
        ],
      })
  },
})
const originalFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname !== '127.0.0.1') return originalFetch(input, init)
  const path = url.pathname
  if (path === '/api/v1/operations/plugin-install') {
    const body = JSON.parse(init.body)
    if (!fixture.operations[body.operationId]) {
      fixture.starts.push(body)
      fixture.operations[body.operationId] = {
        id: body.operationId,
        kind: 'plugin-install',
        pluginId: body.request.pluginId,
        state: 'running',
        message: 'Downloading the plugin…',
        updatedAt: new Date().toISOString(),
      }
      if (!fixture.hold) fixture.finish(body.operationId, 2)
    }
    return Response.json(fixture.operations[body.operationId])
  }
  const operation = /^\/api\/v1\/operations\/([^/]+)(\/cancel)?$/.exec(path)
  if (operation && fixture.operations[operation[1]]) {
    const value = fixture.operations[operation[1]]
    if (operation[2]) value.state = 'cancelled'
    return Response.json(value)
  }
  if (path === '/api/v1/connections/plugins') return Response.json(fixture.plugins)
  const refresh = /^\/api\/v1\/connections\/plugins\/([^/]+)\/refresh$/.exec(path)
  if (refresh) {
    fixture.refreshes.push(refresh[1])
    return Response.json(null)
  }
  return originalFetch(input, init)
}
await import('../../out/main/index.js')
