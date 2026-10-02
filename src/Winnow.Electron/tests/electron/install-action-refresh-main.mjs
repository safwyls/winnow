import { shell } from 'electron'

// Only the HTTP data boundary and OS dispatch are replaced. Main, preload, the
// production query cache, launch feedback, details and both shells remain real.
const state = (globalThis.installRefreshFixture = {
  store: 'epic',
  installed: false,
  cached: false,
  hold: false,
  external: [],
  actions: [],
  forbidden: [],
  release: null,
})
shell.openExternal = async (url) => {
  state.external.push(url)
}
shell.openPath = async (path) => {
  state.forbidden.push(`openPath:${path}`)
  return 'Fixture forbids opening files'
}
const originalFetch = globalThis.fetch
function facts() {
  const title = state.store === 'steam' ? 'Portal 2' : 'Moonlighter'
  const catalog = state.cached ? 'catalog-id' : 'sample-catalog'
  const namespace = state.cached ? 'moon-ns' : 'sample-namespace'
  const entry = {
    ownershipId: 1,
    releaseId: 1,
    workId: 1,
    title,
    store: state.store,
    installed: state.installed,
    playtimeMinutes: 0,
    lastPlayedAt: null,
  }
  return { title, catalog, namespace, entry }
}
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname !== '127.0.0.1') {
    state.forbidden.push(`network:${url.href}`)
    throw Error('Install refresh fixture forbids external network requests')
  }
  const { title, catalog, namespace, entry } = facts()
  if (/^\/api\/v1\/entries\/\d+\/actions$/.test(url.pathname)) {
    state.actions.push({
      ownershipId: Number(url.pathname.split('/')[4]),
      ...JSON.parse(init.body),
      installed: state.installed,
    })
    if (state.hold)
      await new Promise((resolve) => {
        state.release = resolve
      })
    return Response.json(0)
  }
  if (url.pathname === '/api/v1/library')
    return Response.json({
      games: [
        {
          workId: 1,
          title,
          bucket: 'never_played',
          playtimeMinutes: 0,
          lastPlayedAt: null,
          entries: [entry],
        },
      ],
      lists: [],
    })
  if (url.pathname === '/api/v1/games/1/details')
    return Response.json({
      workId: 1,
      sessions: {},
      events: [],
      journalEntries: [],
      ratings: [],
      achievements: [],
      ownerships: [
        {
          id: 1,
          releaseId: 1,
          store: state.store,
          installed: state.installed,
          installPath: state.installed ? 'C:\\Games\\Moonlighter' : null,
        },
      ],
    })
  if (url.pathname === '/api/v1/games/1/metadata') {
    if (init?.method && init.method !== 'GET') {
      state.forbidden.push('metadata write')
      throw Error('Unsaved draft must not be written')
    }
    return Response.json({
      workId: 1,
      title,
      revision: 'unchanged',
      isPinned: false,
      fields: [{ field: 'name', value: title, source: 'igdb' }],
    })
  }
  const response = await originalFetch(input, init)
  if (response.ok && url.pathname === '/api/v1/library/workspace') {
    const body = await response.json()
    return Response.json({
      ...body,
      works: [{ id: 1, name: title }],
      releases: [{ id: 1, workId: 1, name: title }],
      ownerships: [{ id: 1, releaseId: 1, store: state.store, installed: state.installed }],
      buckets: [],
      identityLinks: [],
      lifecycle: [],
      pluginActions: {},
      externalIds: [
        { releaseId: 1, provider: state.store, providerId: state.store === 'steam' ? '620' : catalog },
      ],
      epicLaunchKeys: {
        [catalog]: { namespace, catalogItemId: catalog, artifactId: state.cached ? 'Eagle' : 'Moonlighter' },
      },
      storefronts: {
        [`epic:${namespace}`]: {
          storeUrl: `https://store.epicgames.com/${state.cached ? '' : 'en-US/'}p/moonlighter`,
        },
      },
    })
  }
  return response
}
await import('../../out/main/index.js')
